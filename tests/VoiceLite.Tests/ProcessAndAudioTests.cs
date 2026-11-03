using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class ProcessAndAudioTests
{
    [Fact] public async Task Occupied_port_is_not_contacted_or_terminated()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        await using var manager = new EngineProcessManager(new ReadyProbe());
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await Assert.ThrowsAsync<VoiceLiteException>(() => manager.StartAsync(new("missing-engine", Path.GetTempPath(), port)));
        Assert.False(listener.Pending());
    }
    [Fact] public async Task Ready_process_is_reused_and_stop_reaps_its_child()
    {
        using var folder = new EngineFixture();
        File.WriteAllText(Path.Combine(folder.Root, "spawn-child.flag"), "yes");
        await using var manager = new EngineProcessManager(new ReadyProbe());
        var first = await manager.StartAsync(folder.Options());
        first.VerifyOwnership();
        Assert.Same(first, await manager.StartAsync(folder.Options()));
        var parent = int.Parse(File.ReadAllText(Path.Combine(folder.Root, "parent.pid")));
        var child = int.Parse(File.ReadAllText(Path.Combine(folder.Root, "child.pid")));
        await manager.StopAsync();
        Assert.False(IsRunning(parent)); Assert.False(IsRunning(child));
        Assert.Throws<VoiceLiteException>(first.VerifyOwnership);
    }
    [Theory] [InlineData("fail.flag")] [InlineData("bad-schema.flag")]
    public async Task Exit_or_wrong_schema_does_not_become_ready(string flag)
    {
        using var folder = new EngineFixture(); File.WriteAllText(Path.Combine(folder.Root, flag), "yes");
        await using var manager = new EngineProcessManager(new ReadyProbe());
        await Assert.ThrowsAsync<VoiceLiteException>(() => manager.StartAsync(folder.Options()));
    }
    [Fact] public async Task Startup_deadline_and_cancel_reap_the_process()
    {
        using var folder = new EngineFixture(); File.WriteAllText(Path.Combine(folder.Root, "hang.flag"), "yes");
        await using var manager = new EngineProcessManager(new ReadyProbe());
        await Assert.ThrowsAsync<VoiceLiteException>(() => manager.StartAsync(folder.Options() with { StartupTimeout = TimeSpan.FromMilliseconds(250) }));
        using var cancel = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.StartAsync(folder.Options(), cancel.Token));
    }
    [FfmpegFact] public async Task Import_handles_chinese_spaces_and_preserves_source()
    {
        using var folder = new EngineFixture();
        var source = Path.Combine(folder.Root, "参考 声音.wav"); await File.WriteAllBytesAsync(source, Fixtures.Wav(3));
        var before = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var imported = await new AudioImportService().ImportAsync(source, Ffmpeg, Path.Combine(folder.Root, "录音 导入"), default);
        Assert.NotEqual(source, imported);
        var info = WaveInfo.Inspect(await File.ReadAllBytesAsync(imported));
        Assert.Equal(3, info.DurationSeconds); Assert.Equal(32000, info.SampleRate); Assert.Equal(1, info.Channels);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(source)));
    }
    [FfmpegTheory] [InlineData(true)] [InlineData(false)]
    public async Task Invalid_or_short_audio_leaves_no_import(bool corrupt)
    {
        using var folder = new EngineFixture();
        var source = Path.Combine(folder.Root, "bad.wav"); await File.WriteAllBytesAsync(source, corrupt ? [1, 2, 3] : Fixtures.Wav());
        var destination = Path.Combine(folder.Root, "imports");
        await Assert.ThrowsAsync<VoiceLiteException>(() => new AudioImportService().ImportAsync(source, Ffmpeg, destination, default));
        Assert.Empty(Directory.Exists(destination) ? Directory.GetFiles(destination) : []);
    }
    [Fact] public async Task Import_honors_cancel_before_start()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AudioImportService().ImportAsync("missing.wav", Ffmpeg, Path.GetTempPath(), cancel.Token));
    }
    private static string Ffmpeg => AudioTestTools.Ffmpeg;
    private static bool IsRunning(int id) { try { using var process = Process.GetProcessById(id); return !process.HasExited; } catch (ArgumentException) { return false; } }
}
internal static class AudioTestTools
{
    internal static string Ffmpeg
    {
        get
        {
            var root = Environment.GetEnvironmentVariable("VOICELITE_TEST_ENGINE_ROOT");
            if (!string.IsNullOrWhiteSpace(root)) return Path.Combine(root, "runtime/ffmpeg.exe");
            return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Where(x => x.Length > 0).Select(x => Path.Combine(x, "ffmpeg.exe")).FirstOrDefault(File.Exists) ?? "ffmpeg.exe";
        }
    }
}
internal sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute() { if (!File.Exists(AudioTestTools.Ffmpeg)) Skip = "Set VOICELITE_TEST_ENGINE_ROOT or put ffmpeg.exe on PATH for audio integration tests."; }
}
internal sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    public FfmpegTheoryAttribute() { if (!File.Exists(AudioTestTools.Ffmpeg)) Skip = "Set VOICELITE_TEST_ENGINE_ROOT or put ffmpeg.exe on PATH for audio integration tests."; }
}
internal sealed class ReadyProbe : IEnvironmentProbe
{
    public Task<ProbeReport> InspectAsync(EngineOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new ProbeReport([new("fixture", true, "isolated test engine")]));
}
internal sealed class EngineFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "VoiceLite 测试 " + Guid.NewGuid());
    internal EngineFixture()
    {
        var runtime = Path.Combine(Root, "runtime"); Directory.CreateDirectory(runtime);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "TestEngine")))
            File.Copy(file, Path.Combine(runtime, Path.GetFileName(file) == "VoiceLite.TestEngine.exe" ? "python.exe" : Path.GetFileName(file)));
        File.WriteAllText(Path.Combine(Root, "api_v2.py"), "fixture");
    }
    internal EngineOptions Options() => new(Root, Path.Combine(Root, "data"), StartupTimeout: TimeSpan.FromSeconds(10));
    public void Dispose()
    {
        // Windows can briefly retain an image section after its process has exited.
        for (int attempt = 0; ; attempt++)
        {
            try { Directory.Delete(Root, true); return; }
            catch (Exception exception) when (attempt < 20 && exception is IOException or UnauthorizedAccessException) { Thread.Sleep(25); }
        }
    }
}
