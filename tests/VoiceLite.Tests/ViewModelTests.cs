using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;
namespace VoiceLite.Tests;

public sealed class ViewModelTests
{
    [Fact] public async Task Missing_text_shows_error_without_starting_generation()
    {
        var service = new CapturingGeneration(); var vm = new MainViewModel(service);
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.False(service.Started.Task.IsCompleted); Assert.NotEmpty(vm.Error); Assert.False(vm.IsBusy);
    }
    [Fact] public async Task Cancel_clears_busy_and_does_not_publish_a_result()
    {
        var service = new CapturingGeneration(); var vm = Valid(service);
        var task = vm.GenerateCommand.ExecuteAsync(null);
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(vm.IsBusy); vm.CancelCommand.Execute(null); await task;
        Assert.False(vm.IsBusy); Assert.Empty(vm.ResultPath); Assert.Contains("取消", vm.Status);
    }
    [Fact] public async Task Engine_error_is_visible_and_allows_retry()
    {
        var service = new CapturingGeneration(fail: true); var vm = Valid(service);
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Contains("显存", vm.Error); Assert.False(vm.IsBusy); Assert.Empty(vm.ResultPath);
    }
    [Fact] public async Task Mock_result_is_explicitly_labeled()
    {
        var service = new CapturingGeneration(complete: true); var vm = Valid(service); vm.IsMock = true;
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Contains("模拟", vm.Status); Assert.NotEmpty(vm.ResultPath); Assert.True(service.Last!.IsMock);
    }
    [Fact] public async Task Settings_survive_restart_and_invalid_json_is_recoverable()
    {
        using var folder = new EngineFixture(); var store = new SettingsStore(Path.Combine(folder.Root, "settings"));
        var settings = new UserSettings("E:\\引擎 目录", "E:\\参考.wav", "参考文字", "zh", "en");
        await store.SaveAsync(settings); Assert.Equal(settings, await new SettingsStore(Path.Combine(folder.Root, "settings")).LoadAsync());
        await File.WriteAllTextAsync(Path.Combine(folder.Root, "settings", "settings.json"), "broken-json");
        Assert.Equal(new UserSettings(), await store.LoadAsync());
    }
    private static MainViewModel Valid(IGenerationService service) => new(service) { EngineRoot="fixture", ReferencePath="reference.wav", PromptText="参考文字", Text="合成文字" };
}
internal sealed class CapturingGeneration(bool fail = false, bool complete = false) : IGenerationService
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal GenerationInput? Last;
    public async Task<GeneratedAudio> GenerateAsync(GenerationInput input, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Last = input; Started.TrySetResult();
        if (fail) throw new VoiceLiteException("显存不足");
        if (!complete) await Task.Delay(Timeout.Infinite, cancellationToken);
        return new("result.wav", new WaveInfo(32000, 1, 16, 32000), input.IsMock);
    }
}
