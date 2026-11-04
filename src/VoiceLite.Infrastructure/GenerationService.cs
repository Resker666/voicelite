using VoiceLite.Core;
using System.Text;
using System.Diagnostics;
namespace VoiceLite.Infrastructure;
public sealed class GenerationService(string dataRoot, IEngineManager manager, ITtsApiClient? client = null, IAudioImportService? importer = null, IEngineDeviceResolver? deviceResolver = null) : IGenerationService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<GeneratedAudio> GenerateAsync(GenerationInput input, IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(0, cancellationToken)) throw new VoiceLiteException("已有生成任务正在进行，请等待或取消。");
        string? reference = null;
        var auxiliary = new List<string>();
        string deviceName = "";
        double loadSeconds = 0, inferenceSeconds = 0;
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
                ModelWeights? weights = null;
                if (!string.IsNullOrEmpty(input.RoleId))
                {
                    progress.Report("正在校验角色模型与参考录音…");
                    var role = await new RoleLibrary(dataRoot).ValidateAsync(input.RoleId, cancellationToken);
                    weights = role.Weights;
                    input = input with { ReferencePath = role.ReferencePath, PromptText = role.PromptText,
                        PromptLanguage = role.Language, AuxiliaryReferencePaths = role.AuxiliaryReferencePaths };
                }
                if (string.IsNullOrWhiteSpace(input.PromptText)) throw new VoiceLiteException("请填写参考录音的实际文字。");
                var requested = input.AuxiliaryReferencePaths ?? [];
                if (requested.Count > 3 || requested.Any(string.IsNullOrWhiteSpace)
                    || requested.Select(Path.GetFullPath).Append(Path.GetFullPath(input.ReferencePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != requested.Count + 1)
                    throw new VoiceLiteException("最多选择3条辅助录音，且不要与主参考或其他录音重复。");
                progress.Report("正在检查计算设备…");
                var options = (await (deviceResolver ?? new EngineDeviceResolver()).ResolveAsync(input.EngineRoot, dataRoot, input.Device, cancellationToken)) with { Weights = weights };
                deviceName = options.UseGpu ? "NVIDIA CUDA" : "CPU（FP32）";
                progress.Report($"使用 {deviceName}，正在检查并转换参考录音…");
                reference = await (importer ?? new AudioImportService()).ImportAsync(input.ReferencePath,
                    Path.Combine(input.EngineRoot, "runtime/ffmpeg.exe"), Path.Combine(dataRoot, "references"), cancellationToken);
                foreach (var sourceReference in requested)
                    auxiliary.Add(await (importer ?? new AudioImportService()).ImportAsync(sourceReference,
                        Path.Combine(input.EngineRoot, "runtime/ffmpeg.exe"), Path.Combine(dataRoot, "references"), cancellationToken));
                progress.Report($"使用 {deviceName} 加载模型，请稍候…");
                var clock = Stopwatch.StartNew();
                var session = await manager.StartAsync(options, cancellationToken);
                loadSeconds = clock.Elapsed.TotalSeconds; clock.Restart();
                progress.Report($"使用 {deviceName} 合成语音，请稍候…");
                bytes = await (client ?? new TtsApiClient()).GenerateAsync(session,
                    new(input.Text, input.TextLanguage, reference, input.PromptText, input.PromptLanguage) {
                        UseGpu = options.UseGpu, AuxiliaryReferencePaths = auxiliary
                    },
                    options.RequestTimeout ?? TimeSpan.FromMinutes(3), cancellationToken);
                inferenceSeconds = clock.Elapsed.TotalSeconds;
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
            new AppLog(dataRoot).Write("generation", $"{(input.IsMock ? "mock" : "real")}; device={deviceName}; role={input.RoleId ?? "custom"}; aux_refs={auxiliary.Count}; load_seconds={loadSeconds:F3}; inference_seconds={inferenceSeconds:F3}; seconds={wave.DurationSeconds:F2}; rate={wave.SampleRate}");
            return new(path, wave, input.IsMock, deviceName);
        }
        catch (Exception exception)
        {
            if (!input.IsMock) await manager.StopAsync();
            new AppLog(dataRoot).Write("generation-failed", exception.GetType().Name);
            throw;
        }
        finally
        {
            try
            {
                foreach (var path in (reference is null ? Enumerable.Empty<string>() : [reference]).Concat(auxiliary))
                {
                    try { if (File.Exists(path)) File.Delete(path); }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    { new AppLog(dataRoot).Write("reference-cleanup", exception.GetType().Name); }
                }
            }
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
