using VoiceLite.Core;
using System.Diagnostics;
namespace VoiceLite.Infrastructure;

public sealed class AudioImportService : IAudioImportService
{
    public Task<string> ImportAsync(string source, string ffmpeg, string destinationDirectory, CancellationToken cancellationToken) =>
        ConvertAsync(source, ffmpeg, destinationDirectory, true, cancellationToken);
    public Task<string> ConvertForPreviewAsync(string source, string ffmpeg, string destinationDirectory, CancellationToken cancellationToken) =>
        ConvertAsync(source, ffmpeg, destinationDirectory, false, cancellationToken);
    private async Task<string> ConvertAsync(string source, string ffmpeg, string destinationDirectory, bool forInference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(source) || new FileInfo(source).Length > 200L * 1024 * 1024) throw new VoiceLiteException("请选择存在且小于 200 MB 的录音文件。");
        if (!ReferenceAudioFormats.IsSupported(source)) throw new VoiceLiteException(ReferenceAudioFormats.UnsupportedMessage);
        if (!File.Exists(ffmpeg)) throw new VoiceLiteException("离线包缺少 FFmpeg，请重新获取完整包。");
        Directory.CreateDirectory(destinationDirectory);
        var output = Path.Combine(destinationDirectory, Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var info = new ProcessStartInfo(ffmpeg);
            foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-i", Path.GetFullPath(source), "-vn", "-ac", "1", "-ar", "32000", "-c:a", "pcm_s16le", "-t", forInference ? "11" : "301", "-f", "wav", output }) info.ArgumentList.Add(argument);
            var result = await ProcessRunner.RunAsync(info, TimeSpan.FromSeconds(30), cancellationToken);
            if (result.ExitCode != 0 || !File.Exists(output)) throw new VoiceLiteException("无法读取这段录音，请确认文件没有损坏。");
            var wave = WaveInfo.Inspect(await File.ReadAllBytesAsync(output, cancellationToken));
            if (forInference && wave.DurationSeconds is < 3 or > 10) throw new VoiceLiteException("参考录音需要 3–10 秒，请选择长度合适的片段。");
            if (!forInference && wave.DurationSeconds > 300) throw new VoiceLiteException("录音超过 5 分钟，请选择较短的片段试听；生成时需要 3–10 秒参考录音。");
            return output;
        }
        catch
        {
            if (File.Exists(output)) File.Delete(output);
            throw;
        }
    }
}
