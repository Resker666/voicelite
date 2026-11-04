using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;
using VoiceLite.Core;
using VoiceLite.Desktop;
using VoiceLite.Infrastructure;

namespace VoiceLite.WorkflowProbe;

// Explicit, opt-in real-engine acceptance tests; never run by the ordinary unit suite.
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] is "--quality" or "--references")
        {
            try { QualityComparison.RunAsync(args[1], args[0] == "--references").GetAwaiter().GetResult(); return 0; }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: VoiceLite.WorkflowProbe <engine> <reference.wav with .json transcript> <new-data-directory>");
            return 2;
        }
        var app = new ProbeApplication(args);
        // Read the product's actual styles without constructing its Application/startup.
        var product = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "ProductAppStyles.xml")).Root!;
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(wpf + "ResourceDictionary",
            product.Attributes().Where(x => x.IsNamespaceDeclaration), product.Element(wpf + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        return app.Run();
    }
}

internal sealed class ProbeApplication(string[] arguments) : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Deliberately bypass the product startup: use only the supplied fresh data directory.
        try { await new WorkflowChecks(arguments).RunAsync(); Shutdown(0); }
        catch (Exception exception) { Console.Error.WriteLine(exception); Shutdown(1); }
    }
}

internal sealed class WorkflowChecks(string[] arguments)
{
    private readonly List<object> results = [];
    private const string LongText = "这是一段用于验证取消与恢复的本地测试语音。";

    public async Task RunAsync()
    {
        var (engine, data) = PhysicalDirectories.ValidateExperiment(arguments[0], arguments[2]);
        var reference = Path.GetFullPath(arguments[1]);
        using var transcript = JsonDocument.Parse(await File.ReadAllTextAsync(Path.ChangeExtension(reference, ".json")));
        var prompt = transcript.RootElement.GetProperty("text").GetString() ?? "";
        var language = transcript.RootElement.GetProperty("language").GetString() ?? "";
        Require(prompt.Length > 0 && language is "zh" or "en", "Supply the reference's actual text and language.");
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(reference));
        Directory.CreateDirectory(data);
        await using var manager = new EngineProcessManager();
        using var client = new ObservedClient(data, engine);
        var vm = new MainViewModel(new GenerationService(data, manager, client)) {
            EngineRoot = engine, ReferencePath = reference, PromptText = prompt,
            PromptLanguage = language, TextLanguage = "zh", Device = ComputeDevice.Nvidia
        };
        vm.PropertyChanged += (_, change) => {
            if (change.PropertyName == nameof(vm.Status)) Console.WriteLine($"[{DateTimeOffset.Now:O}] {vm.Status}");
        };
        var window = new MainWindow(vm, manager, new SettingsStore(data), data);
        window.Title += " · 工作流验证";
        window.Show();
        Exception? failure = null;
        try
        {
            await RecordAsync("cancel_in_flight", async () => {
                var (generation, lease) = await StartInFlightAsync(vm, client);
                var clock = Stopwatch.StartNew();
                Require(vm.CancelCommand.CanExecute(null), "Cancel command is disabled during inference.");
                vm.CancelCommand.Execute(null);
                await generation.WaitAsync(TimeSpan.FromSeconds(30));
                Require(!vm.IsBusy && vm.Error.Length == 0 && !vm.HasResult && vm.Status.Contains("已取消"), "Cancellation did not restore the UI state.");
                await CheckStoppedAsync(lease);
                Require(ResultCount(data) == 0, "A cancelled request left a result.");
                CheckTemporaryFiles(data);
                return new { request_in_flight = true, lease.Pid, cleanup_seconds = clock.Elapsed.TotalSeconds, status = vm.Status };
            });

            await RecordAsync("generate_after_cancel", async () => await GenerateAgainAsync(vm, client, data, "after-cancel"));

            await RecordAsync("engine_crash_in_flight", async () => {
                var previousResults = ResultCount(data);
                var (generation, lease) = await StartInFlightAsync(vm, client);
                var clock = Stopwatch.StartNew();
                // Only this test's verified, still-owned process handle may be terminated.
                lease.Session.VerifyOwnership();
                Require(!lease.Process.HasExited, "Engine already exited before fault injection.");
                lease.Process.Kill(entireProcessTree: true);
                await generation.WaitAsync(TimeSpan.FromSeconds(30));
                Require(!vm.IsBusy && vm.Error.Length > 0 && !vm.HasResult, "Engine failure did not produce a retryable UI error.");
                await CheckStoppedAsync(lease);
                Require(ResultCount(data) == previousResults, "A failed request left a result.");
                CheckTemporaryFiles(data);
                return new { request_in_flight = true, lease.Pid, cleanup_seconds = clock.Elapsed.TotalSeconds, error = vm.Error };
            });

            await RecordAsync("generate_after_engine_crash", async () => await GenerateAgainAsync(vm, client, data, "after-crash"));

            await RecordAsync("close_window_in_flight", async () => {
                var previousResults = ResultCount(data);
                var (generation, lease) = await StartInFlightAsync(vm, client);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                window.Closed += (_, _) => closed.TrySetResult();
                var clock = Stopwatch.StartNew();
                window.Close(); // Exercise the actual WPF OnClosing path.
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await generation.WaitAsync(TimeSpan.FromSeconds(30));
                await CheckStoppedAsync(lease);
                Require(!vm.IsBusy && !vm.HasResult && vm.Error.Length == 0 && vm.Status.Contains("已取消"),
                    "Closing did not cancel generation cleanly.");
                Require(ResultCount(data) == previousResults, "Closing during inference left a result.");
                CheckTemporaryFiles(data);
                return new { request_in_flight = true, lease.Pid, close_seconds = clock.Elapsed.TotalSeconds, status = vm.Status };
            });
        }
        catch (Exception exception) { failure = exception; }
        try
        {
            await window.CloseSafelyAsync().WaitAsync(TimeSpan.FromSeconds(30));
            await manager.StopAsync();
            foreach (var lease in client.Leases) await CheckStoppedAsync(lease);
            Require(originalHash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(reference))), "Original reference was changed.");
            CheckTemporaryFiles(data);
        }
        catch (Exception exception) { failure ??= exception; }
        var report = new {
            passed = failure is null, real_engine = true, device = "NVIDIA CUDA", clean_machine = false,
            client_version = typeof(App).Assembly.GetName().Version?.ToString(3),
            original_reference_unchanged = originalHash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(reference))),
            cases = results, error = failure?.ToString()
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(data, "workflow-report.json"), json);
        Console.WriteLine(json);
        if (failure is not null) throw new InvalidOperationException("Real workflow acceptance failed; see workflow-report.json.", failure);
    }

    private async Task RecordAsync(string name, Func<Task<object>> check)
    {
        Console.WriteLine($"BEGIN {name}");
        var clock = Stopwatch.StartNew();
        var evidence = await check().WaitAsync(TimeSpan.FromMinutes(7));
        results.Add(new { name, passed = true, seconds = clock.Elapsed.TotalSeconds, evidence });
        Console.WriteLine($"PASS {name} ({clock.Elapsed.TotalSeconds:F3}s)");
    }

    private static async Task<(Task Generation, EngineLease Lease)> StartInFlightAsync(MainViewModel vm, ObservedClient client)
    {
        vm.Text = string.Concat(Enumerable.Repeat(LongText, 8));
        var observed = client.ObserveNext();
        var generation = vm.GenerateCommand.ExecuteAsync(null);
        await Task.WhenAny(observed, generation).WaitAsync(TimeSpan.FromMinutes(4));
        Require(observed.IsCompletedSuccessfully, "Generation failed before /tts: " + vm.Error);
        var request = await observed.WaitAsync(TimeSpan.FromMinutes(4));
        await Task.Delay(1500);
        Require(vm.IsBusy && !generation.IsCompleted && !request.Request.IsCompleted, "No real /tts request is in flight.");
        request.Lease.Session.VerifyOwnership();
        return (generation, request.Lease);
    }

    private static async Task<object> GenerateAgainAsync(MainViewModel vm, ObservedClient client, string data, string label)
    {
        vm.Text = "你好，这是 VoiceLite 在中断后重新生成的真实语音。";
        var previous = client.Leases.Last();
        var observed = client.ObserveNext();
        await vm.GenerateCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromMinutes(6));
        Require(!vm.IsBusy && vm.HasResult && vm.Error.Length == 0 && vm.ActualDevice == "NVIDIA CUDA", "Real generation did not recover: " + vm.Error);
        var current = (await observed.WaitAsync(TimeSpan.FromSeconds(1))).Lease;
        Require(current.Pid != previous.Pid || current.StartTime != previous.StartTime, "Recovery reused the terminated engine.");
        var exported = Path.Combine(data, label + ".wav");
        await WaveExporter.SaveAsync(vm.ResultPath, exported, protectedReferencePath: vm.ReferencePath);
        var wave = WaveInfo.Inspect(await File.ReadAllBytesAsync(exported));
        Require(wave.DurationSeconds > 0 && wave.SampleRate == 32000, "Recovery did not return a valid model WAV.");
        CheckTemporaryFiles(data);
        return new { current.Pid, wave.DurationSeconds, wave.SampleRate, exported = Path.GetFileName(exported) };
    }

    private static async Task CheckStoppedAsync(EngineLease lease)
    {
        await lease.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Require(!IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == lease.Session.Endpoint.Port), "The stopped engine's port is still listening.");
    }
    private static int ResultCount(string data) => Directory.Exists(Path.Combine(data, "results")) ? Directory.GetFiles(Path.Combine(data, "results"), "*.wav").Length : 0;
    private static void CheckTemporaryFiles(string data)
    {
        Require(!Directory.EnumerateFiles(data, "*.partial", SearchOption.AllDirectories).Any(), "A partial result was left behind.");
        Require(!Directory.Exists(Path.Combine(data, "references")) || !Directory.EnumerateFiles(Path.Combine(data, "references")).Any(), "An imported reference was not cleaned up.");
    }
    internal static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}

internal sealed record EngineLease(Process Process, IEngineSession Session, DateTime StartTime)
{
    public int Pid => Process.Id;
}
internal sealed record PendingRequest(EngineLease Lease, Task<byte[]> Request);

internal sealed class ObservedClient(string data, string engine) : ITtsApiClient, IDisposable
{
    private readonly TtsApiClient inner = new();
    private TaskCompletionSource<PendingRequest>? next;
    public List<EngineLease> Leases { get; } = [];
    public Task<PendingRequest> ObserveNext()
    {
        next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return next.Task;
    }
    public async Task<byte[]> GenerateAsync(IEngineSession session, TtsRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        session.VerifyOwnership();
        var line = File.ReadLines(new AppLog(data).FilePath).Last(x => x.Contains("[start]"));
        var match = Regex.Match(line, @"PID=(\d+); loopback-port=(\d+);");
        WorkflowChecks.Require(match.Success && int.Parse(match.Groups[2].Value) == session.Endpoint.Port, "Engine log does not match the owned session.");
        var process = Process.GetProcessById(int.Parse(match.Groups[1].Value));
        _ = process.SafeHandle; // Hold the original process identity across exit/PID reuse.
        WorkflowChecks.Require(string.Equals(process.MainModule?.FileName, Path.Combine(engine, "runtime", "python.exe"), StringComparison.OrdinalIgnoreCase), "Refusing to target an unrelated process.");
        session.VerifyOwnership();
        var lease = new EngineLease(process, session, process.StartTime);
        Leases.Add(lease);
        var pending = inner.GenerateAsync(session, request, timeout, cancellationToken);
        next?.TrySetResult(new(lease, pending));
        return await pending;
    }
    public void Dispose() { foreach (var lease in Leases) lease.Process.Dispose(); }
}
