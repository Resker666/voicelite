namespace VoiceLite.Core;

public sealed record TrainingClip(string Id, string WavePath, string SourceName, string SourceSha256,
    string WaveSha256, double DurationSeconds, string Text = "", string Language = "zh", bool Included = true, bool Reviewed = false);

public sealed record TrainingDataset(string Id, string Name, int Revision, IReadOnlyList<TrainingClip> Clips)
{
    public string Schema { get; init; } = "voicelite-dataset-1";
}
public sealed record DatasetImportIssue(string FileName, string Message);
public sealed record DatasetImportResult(TrainingDataset Dataset, IReadOnlyList<DatasetImportIssue> Issues);

public interface IDatasetService
{
    Task<TrainingDataset> CreateAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrainingDataset>> ListAsync(CancellationToken cancellationToken = default);
    Task<TrainingDataset> LoadAsync(string id, CancellationToken cancellationToken = default);
    Task<TrainingDataset> SaveAsync(TrainingDataset dataset, CancellationToken cancellationToken = default);
    Task<DatasetImportResult> ImportAsync(string id, IReadOnlyList<string> sources, string ffmpeg,
        IProgress<string> progress, CancellationToken cancellationToken = default);
    Task<string> GetAudioPathAsync(string id, string clipId, CancellationToken cancellationToken = default);
    Task<string> ExportAsync(string id, string destinationParent, CancellationToken cancellationToken = default);
}

public sealed record TranscriptionInput(string Id, string AudioPath, string? Sha256 = null);
public sealed record TranscriptionDraft(string Id, string Text, string Language = "zh");
public sealed record TranscriptionIssue(string Id, string Message);
public sealed record TranscriptionResult(IReadOnlyList<TranscriptionDraft> Drafts, IReadOnlyList<TranscriptionIssue> Issues);
public interface IOfflineTranscriber
{
    Task<TranscriptionResult> TranscribeAsync(string engineRoot, IReadOnlyList<TranscriptionInput> inputs,
        IProgress<string> progress, CancellationToken cancellationToken = default);
}
