using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;
namespace VoiceLite.Tests;

public sealed class WorkflowTests
{
    [Fact] public async Task Mock_pipeline_produces_labeled_wave_without_loading_a_model()
    {
        using var folder = new EngineFixture();
        var result = await new GenerationService(folder.Root, new ForbiddenEngine()).GenerateAsync(new("", "", "", "zh", "模拟", "zh", true), new Progress<string>(), default);
        Assert.True(result.IsMock); Assert.True(File.Exists(result.FilePath));
        Assert.True(WaveInfo.Inspect(await File.ReadAllBytesAsync(result.FilePath)).DurationSeconds > 0);
    }
    [Fact] public async Task Invalid_export_preserves_destination_and_leaves_no_partial_file()
    {
        using var folder = new EngineFixture();
        var source = Path.Combine(folder.Root, "bad.wav"); var target = Path.Combine(folder.Root, "保留.wav");
        await File.WriteAllBytesAsync(source, [1, 2]); await File.WriteAllTextAsync(target, "original");
        await Assert.ThrowsAsync<VoiceLiteException>(() => WaveExporter.SaveAsync(source, target));
        Assert.Equal("original", await File.ReadAllTextAsync(target));
        Assert.Empty(Directory.GetFiles(folder.Root, "*.partial"));
    }
    [Fact] public async Task Valid_wave_exports_to_a_chinese_path()
    {
        using var folder = new EngineFixture();
        var source = Path.Combine(folder.Root, "source.wav"); var target = Path.Combine(folder.Root, "声音 结果.wav");
        await File.WriteAllBytesAsync(source, Fixtures.Wav()); await WaveExporter.SaveAsync(source, target);
        Assert.Equal(Fixtures.Wav(), await File.ReadAllBytesAsync(target));
    }
}
internal sealed class ForbiddenEngine : IEngineManager
{
    public Task<IEngineSession> StartAsync(EngineOptions options, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Mock must not start the engine");
    public Task StopAsync() => throw new InvalidOperationException("Mock must not touch the engine");
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
