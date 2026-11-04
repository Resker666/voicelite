namespace VoiceLite.Core;

public sealed record ModelWeights(string GptPath, string SovitsPath);

public sealed record VoiceRole(string Id, string Name, string Version, string ReferencePath,
    string PromptText, string Language, IReadOnlyList<string> AuxiliaryReferencePaths, ModelWeights? Weights)
{
    public static VoiceRole Custom { get; } = new("", "自定义参考声音", "", "", "", "zh", [], null);
    public bool IsCustom => Id.Length == 0;
    public string DisplayName => IsCustom ? Name : $"{Name} · {Version}";
}

public interface IRoleLibrary
{
    Task<IReadOnlyList<VoiceRole>> ListAsync(CancellationToken cancellationToken = default);
    Task<VoiceRole> ImportAsync(string archivePath, CancellationToken cancellationToken = default);
    Task<VoiceRole> ValidateAsync(string id, CancellationToken cancellationToken = default);
}
