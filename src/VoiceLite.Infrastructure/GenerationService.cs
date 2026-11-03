using VoiceLite.Core;
using System.Text;
namespace VoiceLite.Infrastructure;
public sealed class GenerationService(string dataRoot, IEngineManager manager, ITtsApiClient? client = null, IAudioImportService? importer = null) : IGenerationService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<GeneratedAudio> GenerateAsync(GenerationInput input, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) throw new VoiceLiteException("已有生成任务正在进行，请等待或取消。");
        string? reference = null;
        try
        {
            if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 2000) throw new VoiceLiteException("请填写 1–2000 字的合成文字，较长内容可以分段生成。");
            byte[] bytes;
            if (input.IsMock)
            {
                progress.Report("模拟演示：正在生成提示音，未加载真实模型。仅供界面验证。");
                await Task.Delay(300, cancellationToken);
                bytes = DemoTone();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(input.PromptText)) throw new VoiceLiteException("请填写参考录音的实际文字。");
                progress.Report("正在检查并转换参考录音…");
                reference = await (importer ?? new AudioImportService()).ImportAsync(input.ReferencePath,
                    Path.Combine(input.EngineRoot, "runtime/ffmpeg.exe"), Path.Combine(dataRoot, "references"), cancellationToken);
                progress.Report("正在启动本地引擎并加载模型，首次可能需要一分钟…");
                var options = new EngineOptions(Path.GetFullPath(input.EngineRoot), Path.GetFullPath(dataRoot));
                var session = await manager.StartAsync(options, cancellationToken);
                progress.Report("正在合成语音，请稍候…");
                bytes = await (client ?? new TtsApiClient()).GenerateAsync(session,
                    new(input.Text, input.TextLanguage, reference, input.PromptText, input.PromptLanguage),
                    options.RequestTimeout ?? TimeSpan.FromMinutes(3), cancellationToken);
            }
            var wave = WaveInfo.Inspect(bytes);
            var results = Path.Combine(dataRoot, "results"); Directory.CreateDirectory(results);
            var path = Path.Combine(results, (input.IsMock ? "demo-" : "speech-") + Guid.NewGuid().ToString("N") + ".wav");
            var temporary = path + ".partial";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            new AppLog(dataRoot).Write("generation", $"{(input.IsMock ? "mock" : "real")}; seconds={wave.DurationSeconds:F2}; rate={wave.SampleRate}");
            return new(path, wave, input.IsMock);
        }
        catch (Exception exception)
        {
            if (!input.IsMock) await manager.StopAsync();
            new AppLog(dataRoot).Write("generation-failed", exception.GetType().Name);
            throw;
        }
        finally
        {
            try { if (reference is not null && File.Exists(reference)) File.Delete(reference); }
            finally { gate.Release(); }
        }
    }
    private static byte[] DemoTone()
    {
        const int rate = 32000, frames = rate * 2;
        using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + frames * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(frames * 2);
        for (int i = 0; i < frames; i++) writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 3000 * Math.Min(1, Math.Min(i, frames - i) / 800.0)));
        return buffer.ToArray();
    }
}
