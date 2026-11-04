using System.Text.Json;
using VoiceLite.Core;
namespace VoiceLite.Infrastructure;
public sealed record UserSettings(string EngineRoot = "", string ReferencePath = "", string PromptText = "", string PromptLanguage = "zh", string TextLanguage = "zh", ComputeDevice Device = ComputeDevice.Auto)
{
    public IReadOnlyList<string> AuxiliaryReferencePaths { get; init; } = [];
    public string SelectedRoleId { get; init; } = "";
}
public sealed class SettingsStore(string dataRoot)
{
    private readonly string root = dataRoot;
    private string FilePath => Path.Combine(root, "settings.json");
    public async Task<UserSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var settings = JsonSerializer.Deserialize<UserSettings>(await File.ReadAllTextAsync(FilePath)) ?? new();
            return settings with {
                Device = Enum.IsDefined(settings.Device) ? settings.Device : ComputeDevice.Auto,
                SelectedRoleId = settings.SelectedRoleId ?? "",
                AuxiliaryReferencePaths = (settings.AuxiliaryReferencePaths ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray()
            };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        { new AppLog(root).Write("settings", "设置无法读取，已恢复默认值。"); return new(); }
    }
    public async Task SaveAsync(UserSettings settings)
    {
        Directory.CreateDirectory(root);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
