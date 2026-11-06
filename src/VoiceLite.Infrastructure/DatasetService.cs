using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using VoiceLite.Core;

namespace VoiceLite.Infrastructure;

public sealed class DatasetService(string dataRoot) : IDatasetService
{
    private readonly DatasetPaths paths = new(dataRoot);

    public async Task<TrainingDataset> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dataset = new TrainingDataset(Guid.NewGuid().ToString("N"), name?.Trim() ?? "", 0, []);
        DatasetPaths.Validate(dataset);
        Directory.CreateDirectory(paths.Root); DatasetPaths.Plain(paths.Root);
        var folder = paths.DirectoryFor(dataset.Id);
        Directory.CreateDirectory(Path.Combine(folder, "wavs")); DatasetPaths.Plain(folder);
        try { await DatasetPaths.WriteAsync(Path.Combine(folder, "dataset.json"), dataset, cancellationToken); return dataset; }
        catch { Directory.Delete(folder, true); throw; }
    }

    public async Task<IReadOnlyList<TrainingDataset>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(paths.Root)) return [];
        DatasetPaths.Plain(paths.Root);
        var result = new List<TrainingDataset>();
        foreach (var folder in Directory.GetDirectories(paths.Root).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(folder);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try { result.Add(await LoadAsync(id, cancellationToken)); }
            catch (VoiceLiteException) { /* Broken sets do not prevent other voices from opening. */ }
        }
        return result;
    }

    public async Task<TrainingDataset> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var folder = paths.DirectoryFor(id);
        try
        {
            DatasetPaths.Plain(folder);
            var path = Path.Combine(folder, "dataset.json"); DatasetPaths.Plain(path);
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new VoiceLiteException("素材集清单超过本版大小限制。");
            var dataset = JsonSerializer.Deserialize<TrainingDataset>(await File.ReadAllTextAsync(path, cancellationToken), DatasetPaths.Json)
                ?? throw new VoiceLiteException("素材集清单为空。");
            DatasetPaths.Validate(dataset);
            if (dataset.Id != id) throw new VoiceLiteException("素材集标识与目录不匹配。");
            return dataset;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw new VoiceLiteException("素材集无法读取，请检查清单是否完整、目录权限是否可用。", exception); }
    }

    private FileStream Lock(string folder)
    {
        DatasetPaths.Plain(folder);
        var path = Path.Combine(folder, ".write.lock");
        if (File.Exists(path)) DatasetPaths.Plain(path);
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new VoiceLiteException("素材集正被其他任务使用，请稍后重试。", exception); }
    }

    public async Task<TrainingDataset> SaveAsync(TrainingDataset dataset, CancellationToken cancellationToken = default)
    {
        DatasetPaths.Validate(dataset);
        var folder = paths.DirectoryFor(dataset.Id);
        using var lease = Lock(folder);
        var previous = await LoadAsync(dataset.Id, cancellationToken);
        if (previous.Revision != dataset.Revision) throw new VoiceLiteException("素材集已被其他窗口修改，请重新打开后再编辑。");
        if (previous.Clips.Count != dataset.Clips.Count || previous.Clips.Zip(dataset.Clips).Any(pair =>
            pair.Second with { Text = pair.First.Text, Language = pair.First.Language, Included = pair.First.Included, Reviewed = pair.First.Reviewed } != pair.First))
            throw new VoiceLiteException("只能修改文字、语言、启用与校对状态；请通过导入添加录音。");
        var updated = dataset with { Revision = checked(previous.Revision + 1) };
        await DatasetPaths.WriteAsync(Path.Combine(folder, "dataset.json"), updated, cancellationToken);
        return updated;
    }

    public async Task<DatasetImportResult> ImportAsync(string id, IReadOnlyList<string> sources, string ffmpeg,
        IProgress<string> progress, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = await LoadAsync(id, cancellationToken);
        if (sources.Count is < 1 or > 500 || previous.Clips.Count + sources.Count > 500)
            throw new VoiceLiteException("每个素材集最多500条，请分批选择有效短句。");
        var folder = paths.DirectoryFor(id);
        var staging = Path.Combine(folder, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging); DatasetPaths.Plain(staging);
        var added = new List<TrainingClip>(); var moved = new List<string>(); var issues = new List<DatasetImportIssue>();
        var hashes = previous.Clips.Select(x => x.SourceSha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool committed = false;
        try
        {
            for (int index = 0; index < sources.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = sources[index];
                progress.Report($"正在导入 {index + 1}/{sources.Count}：{Path.GetFileName(source)}");
                try
                {
                    if (!ReferenceAudioFormats.IsSupported(source) || !File.Exists(source)) throw new VoiceLiteException("请选择有效的WAV、MP3或OGG录音。");
                    await using var original = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                    if (original.Length > 200L * 1024 * 1024) throw new VoiceLiteException("录音超过200 MB，请先裁成2–18秒的短句。");
                    var originalHash = Convert.ToHexString(await SHA256.HashDataAsync(original, cancellationToken));
                    if (hashes.Contains(originalHash)) throw new VoiceLiteException("素材集中已有这段录音，已跳过重复文件。");
                    var clipId = Guid.NewGuid().ToString("N");
                    var target = Path.Combine(staging, clipId + ".wav");
                    var info = new ProcessStartInfo(ffmpeg);
                    foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-n", "-protocol_whitelist", "file,pipe", "-i", Path.GetFullPath(source),
                        "-vn", "-ac", "1", "-ar", "32000", "-c:a", "pcm_s16le", "-t", "19", "-f", "wav", target }) info.ArgumentList.Add(arg);
                    var converted = await ProcessRunner.RunAsync(info, TimeSpan.FromSeconds(30), cancellationToken);
                    if (converted.ExitCode != 0 || !File.Exists(target)) throw new VoiceLiteException("无法读取录音，请确认文件没有损坏。");
                    var bytes = await File.ReadAllBytesAsync(target, cancellationToken);
                    var wave = WaveInfo.Inspect(bytes);
                    if (wave.DurationSeconds is < 2 or > 18) throw new VoiceLiteException("训练片段需要2–18秒，未截短导入；请先裁成独立短句。");
                    added.Add(new(clipId, $"wavs/{clipId}.wav", Path.GetFileName(source), originalHash,
                        Convert.ToHexString(SHA256.HashData(bytes)), wave.DurationSeconds));
                    hashes.Add(originalHash);
                }
                catch (Exception exception) when (exception is VoiceLiteException or IOException or UnauthorizedAccessException or ArgumentException)
                { issues.Add(new(Path.GetFileName(source), exception is VoiceLiteException ? exception.Message : "文件无法读取，请检查权限或完整性。")); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (added.Count == 0) return new(previous, issues);
            using var lease = Lock(folder);
            var current = await LoadAsync(id, cancellationToken);
            if (current.Revision != previous.Revision) throw new VoiceLiteException("素材集在导入期间已被修改，请重试本批导入。");
            var wavs = Path.Combine(folder, "wavs"); DatasetPaths.Plain(wavs);
            foreach (var clip in added)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(folder, clip.WavePath);
                File.Move(Path.Combine(staging, clip.Id + ".wav"), destination); moved.Add(destination);
            }
            var result = previous with { Revision = checked(previous.Revision + 1), Clips = previous.Clips.Concat(added).ToArray() };
            await DatasetPaths.WriteAsync(Path.Combine(folder, "dataset.json"), result, cancellationToken);
            committed = true;
            return new(result, issues);
        }
        finally
        {
            if (!committed) foreach (var file in moved) if (File.Exists(file)) File.Delete(file);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public async Task<string> GetAudioPathAsync(string id, string clipId, CancellationToken cancellationToken = default)
    {
        var dataset = await LoadAsync(id, cancellationToken);
        var clip = dataset.Clips.FirstOrDefault(x => x.Id == clipId) ?? throw new VoiceLiteException("素材条目不存在。");
        return await VerifiedAudioAsync(dataset.Id, clip, cancellationToken);
    }

    private async Task<string> VerifiedAudioAsync(string id, TrainingClip clip, CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(paths.DirectoryFor(id), clip.WavePath); DatasetPaths.Plain(path);
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new VoiceLiteException("素材音频过大或已被修改。");
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var wave = WaveInfo.Inspect(bytes);
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(clip.WaveSha256, StringComparison.OrdinalIgnoreCase)
                || wave.SampleRate != 32000 || wave.Channels != 1 || Math.Abs(wave.DurationSeconds - clip.DurationSeconds) > 1.0 / 32000)
                throw new VoiceLiteException("素材音频校验失败，文件可能已被修改，请从原录音重新导入。");
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { throw new VoiceLiteException("素材音频缺失或无法读取，请从原录音重新导入。", exception); }
    }

    public async Task<string> ExportAsync(string id, string destinationParent, CancellationToken cancellationToken = default)
    {
        var dataset = await LoadAsync(id, cancellationToken);
        var clips = dataset.Clips.Where(x => x.Included && x.Reviewed).ToArray();
        if (clips.Length == 0) throw new VoiceLiteException("请先启用并逐字校对至少一条素材，再导出训练集。");
        var parent = Path.GetFullPath(destinationParent);
        if (DatasetPaths.IsInside(paths.Root, parent)) throw new VoiceLiteException("请选择素材库以外的目录导出，避免混入原素材。");
        Directory.CreateDirectory(parent); DatasetPaths.Plain(parent);
        var staging = Path.Combine(parent, ".voicelite-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(staging, "wavs"));
        try
        {
            var lines = new List<string>();
            foreach (var clip in clips)
            {
                var source = await VerifiedAudioAsync(id, clip, cancellationToken);
                var target = Path.Combine(staging, clip.WavePath);
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
                await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                    await input.CopyToAsync(output, cancellationToken);
                await using (var copied = File.OpenRead(target))
                    if (!Convert.ToHexString(await SHA256.HashDataAsync(copied, cancellationToken)).Equals(clip.WaveSha256, StringComparison.OrdinalIgnoreCase))
                        throw new VoiceLiteException("导出副本校验失败，请检查素材文件是否被其他程序修改。");
                lines.Add($"{clip.WavePath}|voicelite|{clip.Language.ToUpperInvariant()}|{clip.Text.Trim()}");
            }
            await DatasetPaths.WriteAsync(Path.Combine(staging, "dataset.json"), dataset with { Clips = clips }, cancellationToken);
            await File.WriteAllLinesAsync(Path.Combine(staging, "train.list"), lines, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(parent, "VoiceLite-dataset-" + id[..8] + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.Move(staging, destination); return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
