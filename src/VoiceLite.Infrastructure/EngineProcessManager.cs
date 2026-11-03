using VoiceLite.Core;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
namespace VoiceLite.Infrastructure;

public sealed class EngineProcessManager(IEnvironmentProbe? probe = null) : IEngineManager
{
    private readonly IEnvironmentProbe environmentProbe = probe ?? new EnvironmentProbe();
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private WindowsChildProcess? child;
    private Task outputPump = Task.CompletedTask, errorPump = Task.CompletedTask;
    private ProcessJob? job;
    private OwnedSession? session;
    private CancellationTokenSource? lifetime;
    private EngineOptions? activeOptions;
    private bool disposed;
    private string? lastDiagnostic;

    public async Task<IEngineSession> StartAsync(EngineOptions options, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (session is not null && activeOptions == options)
            {
                try { session.VerifyOwnership(); return session; }
                catch (VoiceLiteException) { }
            }
            await StopCoreAsync();
            if (options.Port is < 0 or > 65535) throw new VoiceLiteException("端口必须在 1–65535 之间，或使用 0 自动选择。");
            int port;
            try
            {
                using var reservation = new TcpListener(IPAddress.Loopback, options.Port);
                reservation.Server.ExclusiveAddressUse = true;
                reservation.Start(); port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            }
            catch (SocketException) { throw new VoiceLiteException("本地端口已被其他进程占用，未与其通信。请选择其他端口或重试。"); }
            var report = await environmentProbe.InspectAsync(options, cancellationToken);
            if (!report.IsReady) throw new VoiceLiteException(string.Join("；", report.Items.Where(x => !x.Passed).Take(4).Select(x => $"{x.Name}：{x.Message}")));
            Directory.CreateDirectory(options.DataRoot);
            var config = Path.Combine(options.DataRoot, $"engine-{Environment.ProcessId}-{Guid.NewGuid():N}.yaml");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(EngineLayout.Config(options)), cancellationToken);
            var log = new AppLog(options.DataRoot);
            var info = new ProcessStartInfo(Path.Combine(options.EngineRoot, "runtime/python.exe")) {
                WorkingDirectory = options.EngineRoot, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "-B", Path.Combine(AppContext.BaseDirectory, "engine_runner.py"), Path.Combine(options.EngineRoot, "api_v2.py"), "-a", "127.0.0.1", "-p", port.ToString(), "-c", config }) info.ArgumentList.Add(arg);
            info.Environment["PYTHONDONTWRITEBYTECODE"] = "1"; info.Environment["PYTHONUNBUFFERED"] = "1";
            info.Environment["HF_HUB_OFFLINE"] = "1"; info.Environment["TRANSFORMERS_OFFLINE"] = "1";
            info.Environment["HF_HUB_DISABLE_TELEMETRY"] = "1"; info.Environment["G2PW"] = "0";
            lifetime = new(); lastDiagnostic = null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            deadline.CancelAfter(options.StartupTimeout ?? TimeSpan.FromSeconds(180));
            void OnLine(string line)
            {
                var diagnostic = AppLog.EngineDiagnostic(line);
                if (diagnostic is not null) { lastDiagnostic = diagnostic; log.Write("engine", diagnostic); }
            }
            try
            {
                job = new ProcessJob();
                child = WindowsChildProcess.Create(info); process = child.Process;
                child.ResumeInto(job);
                outputPump = PumpAsync(child.StandardOutput, OnLine); errorPump = PumpAsync(child.StandardError, OnLine);
                log.Write("start", $"PID={process.Id}; loopback-port={port}; model={EngineLayout.Version}");
                var owned = new OwnedSession(process, port);
                while (true)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    if (process.HasExited) throw new VoiceLiteException($"引擎启动失败（退出码 {process.ExitCode}）。{lastDiagnostic ?? "请检查模型、运行环境和 NVIDIA 驱动。"}");
                    var owners = WindowsPorts.Listeners(port);
                    if (owners.Any(x => x.ProcessId != process.Id || !x.Address.Equals(IPAddress.Loopback)))
                        throw new VoiceLiteException("端口被未知进程占用，未发送任何 API 请求。");
                    if (owners.Count > 0)
                    {
                        using var client = LoopbackHttp.Create(owned);
                        using var response = await client.GetAsync("openapi.json", deadline.Token);
                        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 256 * 1024)
                            throw new VoiceLiteException("引擎 API 契约检查失败。");
                        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                        using var schema = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token);
                        if (!IsExpectedSchema(schema.RootElement)) throw new VoiceLiteException("端口服务未提供已核实的 /tts 接口。");
                        owned.VerifyOwnership();
                        session = owned; activeOptions = options;
                        log.Write("ready", "进程归属和 OpenAPI 契约检查通过；尚不代表已完成合成。");
                        return owned;
                    }
                    await Task.Delay(150, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { await StopCoreAsync(); throw new VoiceLiteException("引擎启动超时或已停止，请检查模型和显卡驱动后重试。"); }
            catch { await StopCoreAsync(); throw; }
        }
        finally { gate.Release(); }
    }
    private static async Task PumpAsync(StreamReader reader, Action<string> receive)
    {
        try { while (await reader.ReadLineAsync() is { } line) receive(line); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
    private static bool IsExpectedSchema(JsonElement root)
    {
        try
        {
            _ = root.GetProperty("paths").GetProperty("/tts").GetProperty("post");
            var properties = root.GetProperty("components").GetProperty("schemas").GetProperty("TTS_Request").GetProperty("properties");
            return properties.TryGetProperty("text", out _) && properties.TryGetProperty("ref_audio_path", out _) && properties.TryGetProperty("prompt_lang", out _);
        }
        catch (KeyNotFoundException) { return false; }
    }
    public async Task StopAsync()
    {
        try { lifetime?.Cancel(); } catch (ObjectDisposedException) { }
        await gate.WaitAsync();
        try { await StopCoreAsync(); } finally { gate.Release(); }
    }
    private async Task StopCoreAsync()
    {
        session?.Invalidate(); session = null; activeOptions = null;
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
        job?.Dispose(); job = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (InvalidOperationException) { }
            finally
            {
                await Task.WhenAll(outputPump, errorPump).WaitAsync(TimeSpan.FromSeconds(10));
                child?.Dispose(); child = null; process = null;
                outputPump = Task.CompletedTask; errorPump = Task.CompletedTask;
            }
        }
    }
    public async ValueTask DisposeAsync() { disposed = true; await StopAsync(); }
    private sealed class OwnedSession(Process process, int port) : IEngineSession
    {
        private bool invalid;
        public Uri Endpoint { get; } = new($"http://127.0.0.1:{port}/");
        internal void Invalidate() => invalid = true;
        public void VerifyOwnership()
        {
            if (invalid || process.HasExited) throw new VoiceLiteException("本应用的引擎已退出，请重新生成。");
            var owners = WindowsPorts.Listeners(port);
            if (owners.Count != 1 || owners[0].ProcessId != process.Id || !owners[0].Address.Equals(IPAddress.Loopback))
                throw new VoiceLiteException("无法确认端口属于本应用的引擎，已拒绝通信。");
        }
    }
}
