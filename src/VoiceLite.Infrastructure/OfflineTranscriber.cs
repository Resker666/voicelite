using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoiceLite.Core;

namespace VoiceLite.Infrastructure;

public sealed class OfflineTranscriber(string dataRoot) : IOfflineTranscriber
{
    internal static readonly string[] ModelNames = ["speech_paraformer-large_asr_nat-zh-cn-16k-common-vocab8404-pytorch",
        "speech_fsmn_vad_zh-cn-16k-common-pytorch", "punc_ct-transformer_zh-cn-common-vocab272727-pytorch"];

    public async Task<TranscriptionResult> TranscribeAsync(string engineRoot, IReadOnlyList<TranscriptionInput> inputs,
        IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var engine = Path.GetFullPath(engineRoot);
        var required = ModelNames.SelectMany(name => new[] { $"tools/asr/models/{name}/model.pt", $"tools/asr/models/{name}/config.yaml" })
            .Concat(["runtime/python.exe", "runtime/Lib/site-packages/funasr/__init__.py", "tools/portable_runtime.py", "api_v2.py"]);
        if (required.Any(relative => !File.Exists(Path.Combine(engine, relative)) || new FileInfo(Path.Combine(engine, relative)).Length == 0))
            throw new VoiceLiteException("离线中文转写资源不完整。请选择包含 tools/asr/models 的完整整合包目录；程序不会自动下载模型。");
        await using (var api = File.OpenRead(Path.Combine(engine, "api_v2.py")))
            if (Convert.ToHexString(await SHA256.HashDataAsync(api, cancellationToken)) != EngineLayout.ApiSha256)
                throw new VoiceLiteException("转写资源目录与已验证的整合包版本不匹配。");
        if (inputs.Count is < 1 or > 500 || inputs.Select(x => x.Id).Distinct().Count() != inputs.Count)
            throw new VoiceLiteException("请选取1–500条尚未填写文字的素材。");
        var audioRoot = Path.GetFullPath(Path.Combine(dataRoot, "datasets"));
        var items = new List<object>();
        foreach (var input in inputs)
        {
            var audio = Path.GetFullPath(input.AudioPath);
            if (!Guid.TryParseExact(input.Id, "N", out _) || !DatasetPaths.IsInside(audioRoot, audio) || Path.GetFileName(audio) != input.Id + ".wav")
                throw new VoiceLiteException("转写只接受本机素材库中的录音。");
            DatasetPaths.Plain(audio);
            if (new FileInfo(audio).Length > 2 * 1024 * 1024) throw new VoiceLiteException("素材音频过大。");
            var bytes = await File.ReadAllBytesAsync(audio, cancellationToken);
            var wave = WaveInfo.Inspect(bytes);
            var sha = Convert.ToHexString(SHA256.HashData(bytes));
            if (wave.SampleRate != 32000 || wave.Channels != 1 || wave.DurationSeconds is < 2 or > 18
                || (input.Sha256 is not null && !sha.Equals(input.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new VoiceLiteException("素材音频格式或SHA校验失败，请重新导入。");
            items.Add(new { id = input.Id, audio_path = audio, sha256 = sha });
        }
        var jobRoot = Path.Combine(Path.GetFullPath(dataRoot), "asr-jobs");
        Directory.CreateDirectory(jobRoot); DatasetPaths.Plain(jobRoot);
        var job = Path.Combine(jobRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(job);
        try
        {
            var plan = Path.Combine(job, "input.json"); var output = Path.Combine(job, "result.json");
            await File.WriteAllTextAsync(plan, JsonSerializer.Serialize(new { schema = "voicelite-asr-input-1", audio_root = audioRoot, inputs = items }), cancellationToken);
            var info = new ProcessStartInfo(Path.Combine(engine, "runtime/python.exe")) { WorkingDirectory = job, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            foreach (var argument in new[] { "-B", "-u", Path.Combine(AppContext.BaseDirectory, "dataset_asr.py"), "--engine", engine, "--plan", plan, "--output", output }) info.ArgumentList.Add(argument);
            info.Environment["PYTHONDONTWRITEBYTECODE"] = "1"; info.Environment["PYTHONUNBUFFERED"] = "1";
            progress.Report("正在本机加载中文转写模型（CPU），结果仍需逐字校对…");
            var result = await ProcessRunner.RunAsync(info, TimeSpan.FromMinutes(10), cancellationToken, line => {
                const string prefix = "VOICELITE_ASR_PROGRESS ";
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) return;
                try {
                    using var json = JsonDocument.Parse(line[prefix.Length..]);
                    if (json.RootElement.GetProperty("completed").TryGetInt32(out var completed) && json.RootElement.GetProperty("total").TryGetInt32(out var total)
                        && total == inputs.Count && completed >= 0 && completed <= total) progress.Report($"离线转写 {completed}/{total}：结果为待校对草稿。");
                } catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException) { }
            });
            if (result.ExitCode != 0 || !File.Exists(output))
            { new AppLog(dataRoot).Write("asr-failed", $"exit={result.ExitCode}"); throw new VoiceLiteException("离线转写未完成，请检查所选整合包的模型与依赖，或减少素材数量后重试。"); }
            DatasetPaths.Plain(output);
            if (new FileInfo(output).Length > 2 * 1024 * 1024) throw new VoiceLiteException("转写结果超过大小限制。");
            return ParseResult(await File.ReadAllTextAsync(output, cancellationToken), inputs);
        }
        finally { if (Directory.Exists(job)) Directory.Delete(job, true); }
    }

    internal static TranscriptionResult ParseResult(string text, IReadOnlyList<TranscriptionInput> inputs)
    {
        try
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            if (root.GetProperty("schema").GetString() != "voicelite-asr-drafts-1" || root.GetProperty("human_verified").ValueKind != JsonValueKind.False)
                throw new VoiceLiteException("转写结果格式不匹配。");
            var drafts = root.GetProperty("drafts").EnumerateArray().Select(x => new TranscriptionDraft(x.GetProperty("id").GetString()!, x.GetProperty("text").GetString()!, x.GetProperty("language").GetString()!)).ToArray();
            var issues = root.GetProperty("issues").EnumerateArray().Select(x => new TranscriptionIssue(x.GetProperty("id").GetString()!, x.GetProperty("message").GetString()!)).ToArray();
            var ids = drafts.Select(x => x.Id).Concat(issues.Select(x => x.Id)).ToArray();
            if (ids.Length != inputs.Count || ids.Distinct().Count() != ids.Length || !ids.ToHashSet().SetEquals(inputs.Select(x => x.Id))
                || drafts.Any(x => string.IsNullOrWhiteSpace(x.Text) || x.Text.Length > 2000 || x.Text.IndexOfAny(['|', '\r', '\n', '\0']) >= 0 || x.Language != "zh")
                || issues.Any(x => string.IsNullOrWhiteSpace(x.Message) || x.Message.Length > 300))
                throw new VoiceLiteException("转写结果包含无效素材或文字，没有写入素材集。");
            return new(drafts, issues);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new VoiceLiteException("转写结果无法读取，没有写入素材集。", exception); }
    }
}
