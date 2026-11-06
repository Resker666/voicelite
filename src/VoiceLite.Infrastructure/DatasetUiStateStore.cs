using System.Text.Json;

namespace VoiceLite.Infrastructure;

public sealed record DatasetWorkspaceState(string SelectedDatasetId = "", string AsrEngineRoot = "");
public sealed class DatasetUiStateStore(string dataRoot)
{
    private string FilePath => Path.Combine(dataRoot, "dataset-ui.json");
    public async Task<DatasetWorkspaceState> LoadAsync(CancellationToken cancellationToken = default)
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<DatasetWorkspaceState>(await File.ReadAllTextAsync(FilePath, cancellationToken)) ?? new() : new(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public async Task SaveAsync(DatasetWorkspaceState state)
    {
        Directory.CreateDirectory(dataRoot); DatasetPaths.Plain(dataRoot);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".partial";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state)); File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
