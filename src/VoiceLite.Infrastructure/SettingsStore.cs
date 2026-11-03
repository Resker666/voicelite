using System.Text.Json;
using VoiceLite.Core;
namespace VoiceLite.Infrastructure;
public sealed record UserSettings(string EngineRoot = "", string ReferencePath = "", string PromptText = "", string PromptLanguage = "zh", string TextLanguage = "zh", ComputeDevice Device = ComputeDevice.Auto);
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
            return Enum.IsDefined(settings.Device) ? settings : settings with { Device = ComputeDevice.Auto };
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
