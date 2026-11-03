using System.Net;
using System.Text.Json;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;
namespace VoiceLite.Tests;

public sealed class DeviceSelectionTests
{
    [Fact] public async Task Environment_check_blocks_generation_and_duplicate_checks_and_can_be_cancelled()
    {
        var vm = new MainViewModel(new CapturingGeneration());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = vm.CheckEnvironmentAsync(async token => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); return "ready"; });
        await started.Task;
        Assert.True(vm.IsBusy); Assert.False(vm.GenerateCommand.CanExecute(null)); Assert.True(vm.CancelCommand.CanExecute(null));
        await vm.CheckEnvironmentAsync(_ => throw new InvalidOperationException("Must not start a second probe"));
        vm.CancelCurrentOperation(); await pending;
        Assert.False(vm.IsBusy); Assert.Contains("取消", vm.Status); Assert.Empty(vm.Error);
    }

    [Theory]
    [InlineData(ComputeDevice.Auto, true, true)]
    [InlineData(ComputeDevice.Auto, false, false)]
    [InlineData(ComputeDevice.Nvidia, true, true)]
    [InlineData(ComputeDevice.Cpu, true, false)]
    public async Task Requested_device_resolves_to_matching_precision_and_deadlines(ComputeDevice requested, bool cuda, bool expectedGpu)
    {
        using var folder = new EngineFixture(); int calls = 0;
        var resolver = new EngineDeviceResolver((_, _) => { calls++; return Task.FromResult(cuda); });
        var options = await resolver.ResolveAsync(folder.Root, folder.Root, requested);
        Assert.Equal(expectedGpu, options.UseGpu);
        Assert.Equal(requested == ComputeDevice.Cpu ? 0 : 1, calls);
        Assert.Equal(TimeSpan.FromMinutes(expectedGpu ? 3 : 5), options.StartupTimeout);
        Assert.Equal(TimeSpan.FromMinutes(expectedGpu ? 3 : 10), options.RequestTimeout);
        var config = (Dictionary<string, object>)EngineLayout.Config(options)["custom"];
        Assert.Equal(expectedGpu ? "cuda" : "cpu", config["device"]);
        Assert.Equal(expectedGpu, config["is_half"]);
    }

    [Fact] public async Task Explicit_nvidia_does_not_silently_fall_back_and_probe_errors_remain_errors()
    {
        using var folder = new EngineFixture();
        var resolver = new EngineDeviceResolver((_, _) => Task.FromResult(false));
        Assert.Contains("CUDA", (await Assert.ThrowsAsync<VoiceLiteException>(() => resolver.ResolveAsync(folder.Root, folder.Root, ComputeDevice.Nvidia))).Message);
        var broken = new EngineDeviceResolver((_, _) => throw new VoiceLiteException("broken runtime"));
        await Assert.ThrowsAsync<VoiceLiteException>(() => broken.ResolveAsync(folder.Root, folder.Root, ComputeDevice.Auto));
    }

    [Fact] public async Task Device_probe_respects_cancellation()
    {
        using var folder = new EngineFixture(); using var cancel = new CancellationTokenSource();
        var resolver = new EngineDeviceResolver(async (_, token) => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, token); return false; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => resolver.ResolveAsync(folder.Root, folder.Root, ComputeDevice.Auto, cancel.Token));
    }

    [Fact] public async Task Device_choice_is_sent_by_viewmodel_and_survives_restart()
    {
        using var folder = new EngineFixture();
        var service = new CapturingGeneration(complete: true);
        var vm = new MainViewModel(service) { IsMock = true, Text = "test", Device = ComputeDevice.Cpu };
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(ComputeDevice.Cpu, service.Last!.Device);
        var store = new SettingsStore(folder.Root);
        await store.SaveAsync(new UserSettings(Device: vm.Device));
        Assert.Equal(ComputeDevice.Cpu, (await store.LoadAsync()).Device);
        await File.WriteAllTextAsync(Path.Combine(folder.Root, "settings.json"), "{\"EngineRoot\":\"old\"}");
        Assert.Equal(ComputeDevice.Auto, (await store.LoadAsync()).Device);
        await File.WriteAllTextAsync(Path.Combine(folder.Root, "settings.json"), "{\"Device\":99}");
        Assert.Equal(ComputeDevice.Auto, (await store.LoadAsync()).Device);
    }

    [Fact] public async Task Generation_uses_resolved_device_and_does_not_retry_model_failure_on_cpu()
    {
        using var folder = new EngineFixture();
        var resolver = new FixedResolver(); var manager = new CapturingManager();
        var client = new CapturingClient();
        var service = new GenerationService(folder.Root, manager, client, new FixedImporter(folder.Root), resolver);
        var result = await service.GenerateAsync(new(folder.Root, "reference.wav", "reference", "en", "text", "en", Device: ComputeDevice.Cpu), new Progress<string>(), default);
        Assert.False(manager.Options!.UseGpu); Assert.False(client.Request!.UseGpu);
        Assert.Equal(TimeSpan.FromMinutes(10), client.Timeout);
        Assert.Contains("CPU", result.DeviceName); Assert.Equal(1, manager.Starts);
        client.Fail = true;
        await Assert.ThrowsAsync<VoiceLiteException>(() => service.GenerateAsync(new(folder.Root, "reference.wav", "reference", "en", "text", "en"), new Progress<string>(), default));
        Assert.Equal(2, manager.Starts); Assert.Equal(1, manager.Stops);
    }

    [Fact] public async Task Cpu_oom_diagnostic_mentions_system_memory_and_device_flag_is_not_sent_to_api()
    {
        var request = Fixtures.Request() with { UseGpu = false };
        Assert.False(JsonDocument.Parse(JsonSerializer.Serialize(request)).RootElement.TryGetProperty("UseGpu", out _));
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) {
            Content = new StringContent("{\"Exception\":\"out of memory\"}")
        }));
        var error = await Assert.ThrowsAsync<VoiceLiteException>(() => new TtsApiClient(handler).GenerateAsync(new Lease(), request, TimeSpan.FromSeconds(5), default));
        Assert.Contains("系统内存", error.Message); Assert.DoesNotContain("显存", error.Message);
        Assert.Contains("系统内存", AppLog.EngineDiagnostic("DefaultCPUAllocator: not enough memory")!);
    }

    private sealed class FixedResolver : IEngineDeviceResolver
    {
        public Task<EngineOptions> ResolveAsync(string engineRoot, string dataRoot, ComputeDevice device, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EngineOptions(engineRoot, dataRoot, RequestTimeout: TimeSpan.FromMinutes(10), UseGpu: false));
    }
    private sealed class FixedImporter(string root) : IAudioImportService
    {
        public async Task<string> ImportAsync(string source, string ffmpeg, string destinationDirectory, CancellationToken cancellationToken)
        {
            var path = Path.Combine(root, "imported.wav"); await File.WriteAllBytesAsync(path, Fixtures.Wav(3), cancellationToken); return path;
        }
    }
    private sealed class CapturingManager : IEngineManager
    {
        public EngineOptions? Options; public int Starts, Stops;
        public Task<IEngineSession> StartAsync(EngineOptions options, CancellationToken cancellationToken = default) { Options = options; Starts++; return Task.FromResult<IEngineSession>(new Lease()); }
        public Task StopAsync() { Stops++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class CapturingClient : ITtsApiClient
    {
        public TtsRequest? Request; public TimeSpan Timeout; public bool Fail;
        public Task<byte[]> GenerateAsync(IEngineSession session, TtsRequest request, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Request = request; Timeout = timeout;
            if (Fail) throw new VoiceLiteException("model failed");
            return Task.FromResult(Fixtures.Wav());
        }
    }
}
