using System.Collections;
using System.Text.Json;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class AuxiliaryReferenceTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Generation_imports_sends_and_cleans_all_references(bool cancel)
    {
        using var folder = new EngineFixture();
        var input = Input(folder.Root, 2);
        var importer = new Imports(); var client = new Client { Wait = cancel }; var manager = new Manager();
        var service = new GenerationService(folder.Root, manager, client, importer, new Resolver());
        using var cancellation = new CancellationTokenSource();
        var generation = service.GenerateAsync(input, new Progress<string>(), cancellation.Token);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (cancel) { cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generation); }
        else await generation;
        Assert.Equal(3, importer.Files.Count);
        Assert.Equal(importer.Files.Skip(1), client.Request!.AuxiliaryReferencePaths);
        Assert.All(importer.Files, path => Assert.False(File.Exists(path)));
    }

    [Theory] [InlineData(4, false)] [InlineData(2, true)]
    public async Task Too_many_or_duplicate_references_are_rejected(int count, bool duplicate)
    {
        using var folder = new EngineFixture();
        var importer = new Imports();
        var input = Input(folder.Root, count, duplicate);
        var service = new GenerationService(folder.Root, new Manager(), new Client(), importer, new Resolver());
        await Assert.ThrowsAsync<VoiceLiteException>(() => service.GenerateAsync(input, new Progress<string>(), default));
        Assert.Empty(importer.Files);
    }

    [Fact] public async Task Settings_round_trip_auxiliary_paths_and_accept_old_settings()
    {
        using var folder = new EngineFixture();
        var paths = new[] { Path.Combine(folder.Root, "one.wav"), Path.Combine(folder.Root, "two.ogg") };
        var file = Path.Combine(folder.Root, "settings.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { AuxiliaryReferencePaths = paths }));
        var store = new SettingsStore(folder.Root);
        await store.SaveAsync(await store.LoadAsync());
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        Assert.Equal(paths, saved.RootElement.GetProperty("AuxiliaryReferencePaths").EnumerateArray().Select(x => x.GetString()!));
        await File.WriteAllTextAsync(file, "{}");
        var legacy = JsonSerializer.SerializeToElement(await store.LoadAsync());
        Assert.Empty(legacy.GetProperty("AuxiliaryReferencePaths").EnumerateArray());
    }

    [Fact] public void View_model_exposes_reference_list_limits_duplicates_and_clears_when_primary_changes()
    {
        using var folder = new EngineFixture();
        var vm = new MainViewModel(new CaptureService()) { ReferencePath = Path.Combine(folder.Root, "main.wav") };
        var property = typeof(MainViewModel).GetProperty("AuxiliaryReferences");
        Assert.NotNull(property); // This is also the WPF binding contract.
        var collection = Assert.IsAssignableFrom<IList>(property.GetValue(vm));
        var add = typeof(MainViewModel).GetMethod("AddAuxiliaryReferences"); Assert.NotNull(add);
        var paths = Enumerable.Range(0, 4).Select(i => Path.Combine(folder.Root, i + ".wav")).ToArray();
        foreach (var path in paths) File.WriteAllBytes(path, Fixtures.Wav(3));
        add.Invoke(vm, [new[] { paths[0], paths[0], paths[1], paths[2], paths[3] }]);
        Assert.Equal(3, collection.Count);
        Assert.NotEmpty(vm.Error);
        vm.ReferencePath = Path.Combine(folder.Root, "different.wav");
        Assert.Empty(collection);
    }

    private static GenerationInput Input(string root, int count, bool duplicate = false)
    {
        var node = JsonSerializer.SerializeToNode(new GenerationInput(root, Path.Combine(root, "main.wav"), "reference", "en", "text", "en"))!;
        var paths = Enumerable.Range(0, count).Select(i => Path.Combine(root, (duplicate ? 0 : i) + ".wav")).ToArray();
        node["AuxiliaryReferencePaths"] = JsonSerializer.SerializeToNode(paths);
        return node.Deserialize<GenerationInput>()!;
    }
    private sealed class Imports : IAudioImportService
    {
        public List<string> Files { get; } = [];
        public async Task<string> ImportAsync(string source, string ffmpeg, string destinationDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destinationDirectory);
            var path = Path.Combine(destinationDirectory, Guid.NewGuid() + ".wav");
            await File.WriteAllBytesAsync(path, Fixtures.Wav(3), cancellationToken); Files.Add(path); return path;
        }
    }
    private sealed class Resolver : IEngineDeviceResolver
    {
        public Task<EngineOptions> ResolveAsync(string engineRoot, string dataRoot, ComputeDevice device, CancellationToken cancellationToken = default) => Task.FromResult(new EngineOptions(engineRoot, dataRoot));
    }
    private sealed class Manager : IEngineManager
    {
        public Task<IEngineSession> StartAsync(EngineOptions options, CancellationToken cancellationToken = default) => Task.FromResult<IEngineSession>(new Lease());
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Client : ITtsApiClient
    {
        public bool Wait; public TtsRequest? Request;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<byte[]> GenerateAsync(IEngineSession session, TtsRequest request, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Request = request; Started.TrySetResult();
            if (Wait) await Task.Delay(Timeout.Infinite, cancellationToken);
            return Fixtures.Wav();
        }
    }
    private sealed class CaptureService : IGenerationService
    {
        public Task<GeneratedAudio> GenerateAsync(GenerationInput input, IProgress<string> progress, CancellationToken cancellationToken) => Task.FromResult(new GeneratedAudio("result.wav", WaveInfo.Inspect(Fixtures.Wav()), false));
    }
}
