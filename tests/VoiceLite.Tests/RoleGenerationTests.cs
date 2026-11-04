using System.Text.Json;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class RoleGenerationTests
{
    [Fact] public async Task Role_generation_uses_verified_weights_and_its_own_reference_defaults_then_resets_to_base()
    {
        using var folder = new EngineFixture();
        var role = await new RoleLibrary(folder.Root).ImportAsync(RoleFixture.Zip(folder.Root));
        var manager = new Manager(); var imports = new Imports(); var client = new Client();
        var service = new GenerationService(folder.Root, manager, client, imports, new Resolver());
        var input = new GenerationInput(folder.Root, "ignored", "", "en", "测试", "zh", RoleId: role.Id);
        await service.GenerateAsync(input, new Progress<string>(), default);
        Assert.Equal(role.Weights, manager.Options!.Weights);
        Assert.Equal(role.ReferencePath, imports.Sources[0]);
        Assert.Equal(role.AuxiliaryReferencePaths, imports.Sources.Skip(1));
        Assert.Equal(role.PromptText, client.Request!.PromptText);
        Assert.Equal(role.Language, client.Request.PromptLanguage);
        await service.GenerateAsync(input with { RoleId = null, ReferencePath = role.ReferencePath, PromptText = "自定义" }, new Progress<string>(), default);
        Assert.Null(manager.Options!.Weights);
        Assert.Equal("自定义", client.Request.PromptText);
    }

    [Fact] public async Task Damaged_role_cannot_start_or_contact_the_engine()
    {
        using var folder = new EngineFixture();
        var role = await new RoleLibrary(folder.Root).ImportAsync(RoleFixture.Zip(folder.Root));
        await File.WriteAllBytesAsync(role.Weights!.GptPath, [9, 2, 3, 4]);
        var manager = new Manager(); var imports = new Imports(); var client = new Client();
        var service = new GenerationService(folder.Root, manager, client, imports, new Resolver());
        await Assert.ThrowsAsync<VoiceLiteException>(() => service.GenerateAsync(new(folder.Root, role.ReferencePath, role.PromptText, "zh", "测试", "zh", RoleId: role.Id), new Progress<string>(), default));
        Assert.Null(manager.Options); Assert.Null(client.Request); Assert.Empty(imports.Sources);
    }

    [Fact] public async Task Mock_does_not_load_any_selected_role_weights()
    {
        using var folder = new EngineFixture(); var manager = new Manager();
        var service = new GenerationService(folder.Root, manager);
        var audio = await service.GenerateAsync(new("", "", "", "zh", "测试", "zh", IsMock: true, RoleId: "missing"), new Progress<string>(), default);
        Assert.True(audio.IsMock); Assert.Null(manager.Options);
    }

    [Fact] public void Engine_configuration_contains_both_selected_weights_or_both_original_weights()
    {
        var options = new EngineOptions("C:\\engine", "C:\\data");
        var method = typeof(EngineLayout).GetMethod("Config", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var weights = new ModelWeights("C:\\roles\\one.ckpt", "C:\\roles\\two.pth");
        var selected = JsonSerializer.SerializeToElement(method.Invoke(null, [options with { Weights = weights }])).GetProperty("custom");
        Assert.Equal(weights.GptPath, selected.GetProperty("t2s_weights_path").GetString());
        Assert.Equal(weights.SovitsPath, selected.GetProperty("vits_weights_path").GetString());
        var original = JsonSerializer.SerializeToElement(method.Invoke(null, [options])).GetProperty("custom");
        Assert.EndsWith("s1v3.ckpt", original.GetProperty("t2s_weights_path").GetString());
        Assert.EndsWith("s2Gv2ProPlus.pth", original.GetProperty("vits_weights_path").GetString());
    }

    [Fact] public async Task Changing_weight_pair_or_returning_to_default_restarts_the_owned_process()
    {
        using var folder = new EngineFixture();
        await using var manager = new EngineProcessManager(new Ready());
        var options = folder.Options() with { Weights = new("C:\\a.ckpt", "C:\\a.pth") };
        var a = await manager.StartAsync(options);
        Assert.Same(a, await manager.StartAsync(options with { Weights = new("C:\\a.ckpt", "C:\\a.pth") }));
        var b = await manager.StartAsync(options with { Weights = new("C:\\b.ckpt", "C:\\b.pth") });
        Assert.NotSame(a, b); Assert.Throws<VoiceLiteException>(a.VerifyOwnership);
        var original = await manager.StartAsync(options with { Weights = null });
        Assert.NotSame(b, original); Assert.Throws<VoiceLiteException>(b.VerifyOwnership);
        original.VerifyOwnership();
    }

    private sealed class Resolver : IEngineDeviceResolver
    {
        public Task<EngineOptions> ResolveAsync(string engineRoot, string dataRoot, ComputeDevice device, CancellationToken cancellationToken = default) => Task.FromResult(new EngineOptions(engineRoot, dataRoot));
    }
    private sealed class Manager : IEngineManager
    {
        public EngineOptions? Options;
        public Task<IEngineSession> StartAsync(EngineOptions options, CancellationToken cancellationToken = default) { Options = options; return Task.FromResult<IEngineSession>(new Lease()); }
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Imports : IAudioImportService
    {
        public List<string> Sources { get; } = [];
        public Task<string> ImportAsync(string source, string ffmpeg, string destinationDirectory, CancellationToken cancellationToken) { Sources.Add(source); return Task.FromResult(Path.Combine(destinationDirectory, Guid.NewGuid() + ".wav")); }
    }
    private sealed class Client : ITtsApiClient
    {
        public TtsRequest? Request;
        public Task<byte[]> GenerateAsync(IEngineSession session, TtsRequest request, TimeSpan timeout, CancellationToken cancellationToken) { Request = request; return Task.FromResult(Fixtures.Wav()); }
    }
    private sealed class Ready : IEnvironmentProbe
    {
        public Task<ProbeReport> InspectAsync(EngineOptions options, CancellationToken cancellationToken = default) => Task.FromResult(new ProbeReport([new("fixture", true, "fixture")]));
    }
}
