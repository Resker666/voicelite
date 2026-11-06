namespace VoiceLite.Core;

public enum TrainingJobState { Preparing, Completed, Cancelled, Failed, Interrupted }
public sealed record TrainingReference(string Id, string Name, double Seconds, string Text, string Language)
{
    public string DisplayName => $"{Name} · {Seconds:F2}秒";
}
public sealed record TrainingJob(string Id, string DatasetId, string Name, string EngineRoot, TrainingJobState State,
    int EpochsCompleted, IReadOnlyList<TrainingReference> References, string Error = "")
{
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string DisplayName => $"{Name} · {State switch { TrainingJobState.Completed => $"已完成{EpochsCompleted}轮", TrainingJobState.Preparing => "运行中", TrainingJobState.Cancelled => "已取消", TrainingJobState.Interrupted => "运行中断", _ => "失败" }}";
}
public interface ITrainingService
{
    Task<IReadOnlyList<TrainingJob>> ListAsync(string datasetId, CancellationToken cancellationToken = default);
    Task<TrainingJob> StartAsync(string datasetId, string name, string engineRoot, IProgress<string> progress, CancellationToken cancellationToken = default);
    Task<string> ExportRoleAsync(string jobId, string referenceId, string output, IProgress<string> progress, CancellationToken cancellationToken = default);
}
