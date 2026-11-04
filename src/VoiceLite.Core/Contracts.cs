using System.Text.Json.Serialization;

namespace VoiceLite.Core;

public enum ComputeDevice { Auto, Nvidia, Cpu }
public interface IEngineDeviceResolver
{
    Task<EngineOptions> ResolveAsync(string engineRoot, string dataRoot, ComputeDevice device, CancellationToken cancellationToken = default);
}
public sealed record EngineOptions(string EngineRoot, string DataRoot, int Port = 0,
    TimeSpan? StartupTimeout = null, TimeSpan? RequestTimeout = null, bool UseGpu = true);
public sealed record ProbeItem(string Name, bool Passed, string Message);
public sealed record ProbeReport(IReadOnlyList<ProbeItem> Items)
{
    public bool IsReady => Items.All(x => x.Passed);
}
public interface IEnvironmentProbe
{
    Task<ProbeReport> InspectAsync(EngineOptions options, CancellationToken cancellationToken = default);
}
public interface IEngineSession
{
    Uri Endpoint { get; }
    void VerifyOwnership();
}
public interface IEngineManager : IAsyncDisposable
{
    Task<IEngineSession> StartAsync(EngineOptions options, CancellationToken cancellationToken = default);
    Task StopAsync();
}
public interface ITtsApiClient
{
    Task<byte[]> GenerateAsync(IEngineSession session, TtsRequest request, TimeSpan timeout, CancellationToken cancellationToken);
}
public interface IAudioImportService
{
    Task<string> ImportAsync(string source, string ffmpeg, string destinationDirectory, CancellationToken cancellationToken);
}
public sealed record TtsRequest(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("text_lang")] string TextLanguage,
    [property: JsonPropertyName("ref_audio_path")] string ReferencePath,
    [property: JsonPropertyName("prompt_text")] string PromptText,
    [property: JsonPropertyName("prompt_lang")] string PromptLanguage)
{
    [JsonIgnore] public bool UseGpu { get; init; } = true;
    [JsonPropertyName("seed")] public int Seed { get; init; } = -1;
    [JsonPropertyName("temperature")] public double Temperature { get; init; } = 1;
    [JsonPropertyName("aux_ref_audio_paths")] public IReadOnlyList<string> AuxiliaryReferencePaths { get; init; } = [];
    [JsonPropertyName("media_type")] public string MediaType => "wav";
    [JsonPropertyName("streaming_mode")] public bool Streaming => false;
    [JsonPropertyName("text_split_method")] public string SplitMethod => "cut5";
    [JsonPropertyName("batch_size")] public int BatchSize => 1;
}
public sealed class VoiceLiteException(string message, Exception? inner = null) : Exception(message, inner);
public sealed record GenerationInput(string EngineRoot, string ReferencePath, string PromptText,
    string PromptLanguage, string Text, string TextLanguage, bool IsMock = false, ComputeDevice Device = ComputeDevice.Auto,
    IReadOnlyList<string>? AuxiliaryReferencePaths = null);
public sealed record GeneratedAudio(string FilePath, WaveInfo Info, bool IsMock, string DeviceName = "");
public interface IGenerationService
{
    Task<GeneratedAudio> GenerateAsync(GenerationInput input, IProgress<string> progress, CancellationToken cancellationToken);
}
