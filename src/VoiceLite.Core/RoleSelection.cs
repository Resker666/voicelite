using System.Collections.ObjectModel;

namespace VoiceLite.Core;

public sealed record ReferenceDefaults(string ReferencePath, string PromptText, string PromptLanguage,
    string TextLanguage, IReadOnlyList<string> AuxiliaryReferencePaths);

public sealed partial class MainViewModel
{
    private readonly IRoleLibrary? roleLibrary;
    private VoiceRole selectedRole = VoiceRole.Custom;
    private ReferenceDefaults? customReferences;
    public ObservableCollection<VoiceRole> Roles { get; } = [VoiceRole.Custom];
    public Task RoleOperation { get; private set; } = Task.CompletedTask;
    public bool CanEditReferences => CanEdit && SelectedRole.IsCustom;
    public bool IsRoleSelected => !SelectedRole.IsCustom;
    public string RoleDescription => SelectedRole.IsCustom ? "选择参考录音，或导入训练好的角色包。" : "已填好角色的参考声音与文字，可直接输入文字生成。";
    private ReferenceDefaults CurrentReferences => new(ReferencePath, PromptText, PromptLanguage, TextLanguage, AuxiliaryReferences.Select(x => x.Path).ToArray());
    public ReferenceDefaults CustomReferenceDefaults => !SelectedRole.IsCustom && customReferences is not null ? customReferences : CurrentReferences;
    public VoiceRole SelectedRole
    {
        get => selectedRole;
        set
        {
            if (value is null || IsBusy || value.Id == selectedRole.Id) return;
            if (selectedRole.IsCustom && !value.IsCustom) customReferences = CurrentReferences;
            SetProperty(ref selectedRole, value);
            var defaults = value.IsCustom ? customReferences : new ReferenceDefaults(value.ReferencePath, value.PromptText, value.Language, value.Language, value.AuxiliaryReferencePaths);
            if (defaults is not null)
            {
                ReferencePath = defaults.ReferencePath; PromptText = defaults.PromptText;
                PromptLanguage = defaults.PromptLanguage; TextLanguage = defaults.TextLanguage;
                AuxiliaryReferences.Clear();
                foreach (var path in defaults.AuxiliaryReferencePaths) AuxiliaryReferences.Add(new(path));
            }
            ResultPath = "";
            OnPropertyChanged(nameof(CanEditReferences)); OnPropertyChanged(nameof(IsRoleSelected)); OnPropertyChanged(nameof(RoleDescription));
        }
    }

    public async Task InitializeRolesAsync(string? selectedId = null, CancellationToken cancellationToken = default)
    {
        if (roleLibrary is null) return;
        try
        {
            var roles = await roleLibrary.ListAsync(cancellationToken);
            Roles.Clear(); Roles.Add(VoiceRole.Custom);
            foreach (var role in roles) Roles.Add(role);
            if (!string.IsNullOrEmpty(selectedId))
            {
                await roleLibrary.ValidateAsync(selectedId, cancellationToken);
                SelectedRole = Roles.FirstOrDefault(x => x.Id == selectedId) ?? throw new VoiceLiteException("上次选择的角色不存在，请重新导入角色包。");
            }
        }
        catch (Exception exception) when (exception is VoiceLiteException or IOException or UnauthorizedAccessException)
        { Error = "上次的角色无法恢复，已使用自定义参考声音。请重新导入角色包。"; }
    }

    public Task ImportRoleAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        if (IsBusy || roleLibrary is null) return Task.CompletedTask;
        return RoleOperation = ImportRoleCoreAsync(archivePath, cancellationToken);
    }

    private async Task ImportRoleCoreAsync(string archivePath, CancellationToken cancellationToken)
    {
        Error = ""; IsBusy = true; Status = "正在导入并校验角色包，请稍候…";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        environmentCancellation = cancellation;
        VoiceRole? imported = null;
        try { imported = await roleLibrary!.ImportAsync(archivePath, cancellation.Token); }
        catch (OperationCanceledException) { Status = "角色包导入已取消，原有角色保持可用。"; }
        catch (VoiceLiteException exception) { Error = exception.Message; Status = "角色包未导入。"; }
        catch (Exception) { Error = "角色包导入失败，请检查文件与目录权限。"; Status = "角色包未导入。"; }
        finally { environmentCancellation = null; IsBusy = false; }
        if (imported is not null)
        {
            Roles.Add(imported); SelectedRole = imported;
            Status = "角色导入完成，输入文字即可生成。";
        }
    }
}
