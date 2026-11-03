using VoiceLite.Core;
namespace VoiceLite.Infrastructure;

public sealed class AudioPreviewService
{
    public async Task<PreparedAudioPreview> PrepareAsync(string source, string ffmpeg, string temporaryDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(source)) throw new VoiceLiteException("请先选择或生成音频。");
        if (!ReferenceAudioFormats.IsSupported(source)) throw new VoiceLiteException(ReferenceAudioFormats.UnsupportedMessage);
        if (!Path.GetExtension(source).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
            return new(Path.GetFullPath(source), ownsFile: false);
        // OGG support in Windows media components varies. Always play a normalized WAV.
        var converted = await new AudioImportService().ConvertForPreviewAsync(source, ffmpeg, temporaryDirectory, cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            File.Delete(converted); cancellationToken.ThrowIfCancellationRequested();
        }
        return new(converted, ownsFile: true);
    }
}
public sealed class PreparedAudioPreview : IDisposable
{
    private readonly bool ownsFile;
    public string FilePath { get; }
    internal PreparedAudioPreview(string path, bool ownsFile) { FilePath = path; this.ownsFile = ownsFile; }
    public void Dispose() { if (ownsFile && File.Exists(FilePath)) File.Delete(FilePath); }
}
