using System.Reflection;
using System.Text.Json;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class RoleViewModelTests
{
    private static MainViewModel ViewModel(IGenerationService service, IRoleLibrary library)
    {
        var constructor = typeof(MainViewModel).GetConstructor([typeof(IGenerationService), typeof(IRoleLibrary)]);
        Assert.NotNull(constructor);
        return (MainViewModel)constructor.Invoke([service, library]);
    }
    private static PropertyInfo Selection { get { var property = typeof(MainViewModel).GetProperty("SelectedRole"); Assert.NotNull(property); return property; } }
    private static Task Import(MainViewModel vm, string path)
    {
        var method = typeof(MainViewModel).GetMethod("ImportRoleAsync"); Assert.NotNull(method);
        return (Task)method.Invoke(vm, [path, CancellationToken.None])!;
    }
    private static Task Initialize(MainViewModel vm, string id)
    {
        var method = typeof(MainViewModel).GetMethod("InitializeRolesAsync"); Assert.NotNull(method);
        return (Task)method.Invoke(vm, [id, CancellationToken.None])!;
    }

    [Fact] public async Task Import_selects_defaults_preserves_text_and_restores_custom_inputs()
    {
        using var folder = new EngineFixture(); var service = new CaptureService();
        var vm = ViewModel(service, new RoleLibrary(folder.Root));
        vm.EngineRoot = folder.Root; vm.ReferencePath = Path.Combine(folder.Root, "custom.wav");
        vm.PromptText = "原参考文字"; vm.PromptLanguage = "en"; vm.TextLanguage = "en"; vm.Text = "保留合成文字";
        var extra = Path.Combine(folder.Root, "custom-extra.ogg"); File.WriteAllBytes(extra, [1]);
        vm.AddAuxiliaryReferences([extra]);
        await Import(vm, RoleFixture.Zip(folder.Root));
        var role = Assert.IsType<VoiceRole>(Selection.GetValue(vm));
        Assert.False(role.IsCustom); Assert.Equal(role.ReferencePath, vm.ReferencePath);
        Assert.Equal("参考文字", vm.PromptText); Assert.Equal("zh", vm.PromptLanguage);
        Assert.Single(vm.AuxiliaryReferences); Assert.Equal("保留合成文字", vm.Text);
        await vm.GenerateCommand.ExecuteAsync(null);
        Assert.Equal(role.Id, service.Input!.RoleId);
        Selection.SetValue(vm, VoiceRole.Custom);
        Assert.Equal("原参考文字", vm.PromptText); Assert.Equal("en", vm.PromptLanguage); Assert.Equal("en", vm.TextLanguage);
        Assert.Equal(extra, Assert.Single(vm.AuxiliaryReferences).Path);
        Assert.Equal(Path.Combine(folder.Root, "custom.wav"), vm.ReferencePath); Assert.Empty(vm.ResultPath);
    }

    [Fact] public async Task Cancel_busy_import_blocks_selection_and_recovers_editing()
    {
        using var folder = new EngineFixture(); var library = new BlockingLibrary();
        var vm = ViewModel(new CaptureService(), library);
        var pending = Import(vm, "unused.zip"); await library.Started.Task;
        Assert.True(vm.IsBusy); Assert.False(vm.CanEdit);
        Selection.SetValue(vm, new VoiceRole("different", "other", "1", "", "", "zh", [], null));
        Assert.True(Assert.IsType<VoiceRole>(Selection.GetValue(vm)).IsCustom);
        vm.CancelCurrentOperation(); await pending;
        Assert.False(vm.IsBusy); Assert.Contains("取消", vm.Status); Assert.Empty(vm.Error);
    }

    [Fact] public async Task Failed_import_keeps_previous_selection_and_defaults()
    {
        using var folder = new EngineFixture(); var vm = ViewModel(new CaptureService(), new RoleLibrary(folder.Root));
        await Import(vm, RoleFixture.Zip(folder.Root)); var previous = Selection.GetValue(vm);
        await Import(vm, RoleFixture.Zip(folder.Root, json => json["model_version"] = "v3"));
        Assert.Same(previous, Selection.GetValue(vm)); Assert.NotEmpty(vm.Error); Assert.Equal("参考文字", vm.PromptText);
    }

    [Fact] public async Task Saved_role_restores_and_missing_role_falls_back_with_message()
    {
        using var folder = new EngineFixture(); var library = new RoleLibrary(folder.Root);
        var role = await library.ImportAsync(RoleFixture.Zip(folder.Root));
        var vm = ViewModel(new CaptureService(), library); await Initialize(vm, role.Id);
        Assert.Equal(role.Id, Assert.IsType<VoiceRole>(Selection.GetValue(vm)).Id);
        var missing = ViewModel(new CaptureService(), library); await Initialize(missing, Guid.NewGuid().ToString("N"));
        Assert.True(Assert.IsType<VoiceRole>(Selection.GetValue(missing)).IsCustom); Assert.NotEmpty(missing.Error);
    }

    [Fact] public async Task Settings_save_selection_and_accept_pre_role_settings()
    {
        using var folder = new EngineFixture(); var store = new SettingsStore(folder.Root);
        var id = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(Path.Combine(folder.Root, "settings.json"), JsonSerializer.Serialize(new { SelectedRoleId = id }));
        await store.SaveAsync(await store.LoadAsync());
        using (var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder.Root, "settings.json"))))
            Assert.Equal(id, saved.RootElement.GetProperty("SelectedRoleId").GetString());
        await File.WriteAllTextAsync(Path.Combine(folder.Root, "settings.json"), "{}");
        Assert.Equal("", JsonSerializer.SerializeToElement(await store.LoadAsync()).GetProperty("SelectedRoleId").GetString());
    }

    private sealed class CaptureService : IGenerationService
    {
        public GenerationInput? Input;
        public Task<GeneratedAudio> GenerateAsync(GenerationInput input, IProgress<string> progress, CancellationToken cancellationToken)
        { Input = input; return Task.FromResult(new GeneratedAudio("result.wav", WaveInfo.Inspect(Fixtures.Wav()), input.IsMock)); }
    }
    private sealed class BlockingLibrary : IRoleLibrary
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<VoiceRole> ImportAsync(string archivePath, CancellationToken cancellationToken = default)
        { Started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); throw new InvalidOperationException(); }
        public Task<IReadOnlyList<VoiceRole>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<VoiceRole>>([]);
        public Task<VoiceRole> ValidateAsync(string id, CancellationToken cancellationToken = default) => throw new VoiceLiteException("missing");
    }
}
