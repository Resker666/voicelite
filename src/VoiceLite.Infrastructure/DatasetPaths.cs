using System.Text.Json;
using VoiceLite.Core;

namespace VoiceLite.Infrastructure;

internal sealed class DatasetPaths(string dataRoot)
{
    internal string Root { get; } = Path.GetFullPath(Path.Combine(dataRoot, "datasets"));
    internal static JsonSerializerOptions Json { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 16 };
    internal string DirectoryFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new VoiceLiteException("素材集标识无效，请重新打开素材集。");
        return Path.Combine(Root, id);
    }
    internal static void Plain(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new VoiceLiteException("素材目录或文件包含链接，请选择普通文件目录。");
    }
    internal static bool IsInside(string parent, string child) => Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static bool ValidHash(string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    internal static void Validate(TrainingDataset dataset)
    {
        if (dataset.Schema != "voicelite-dataset-1" || !Guid.TryParseExact(dataset.Id, "N", out _) || dataset.Revision < 0
            || string.IsNullOrWhiteSpace(dataset.Name) || dataset.Name.Length > 80 || dataset.Name.Any(char.IsControl)
            || dataset.Clips is null || dataset.Clips.Count > 500 || dataset.Clips.Any(x => x is null)
            || dataset.Clips.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != dataset.Clips.Count)
            throw new VoiceLiteException("素材集清单无效，请检查格式、名称和条目数量。");
        foreach (var clip in dataset.Clips)
        {
            if (!Guid.TryParseExact(clip.Id, "N", out _) || clip.WavePath != $"wavs/{clip.Id}.wav"
                || string.IsNullOrWhiteSpace(clip.SourceName) || clip.SourceName.Length > 260
                || !ValidHash(clip.SourceSha256) || !ValidHash(clip.WaveSha256)
                || !double.IsFinite(clip.DurationSeconds) || clip.DurationSeconds is < 2 or > 18
                || clip.Language is not ("zh" or "en") || clip.Text is null || clip.Text.Length > 2000
                || clip.Text.IndexOfAny(['|', '\r', '\n', '\0']) >= 0 || (clip.Reviewed && string.IsNullOrWhiteSpace(clip.Text)))
                throw new VoiceLiteException("素材信息无效：请使用2–18秒片段、单行文字与中文/英文语言；已校对条目必须有文字。");
        }
    }
    internal static async Task WriteAsync(string path, TrainingDataset dataset, CancellationToken cancellationToken)
    {
        Validate(dataset);
        if (File.Exists(path)) Plain(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await JsonSerializer.SerializeAsync(stream, dataset, Json, cancellationToken);
            if (new FileInfo(temporary).Length > 2 * 1024 * 1024)
                throw new VoiceLiteException("素材清单超过2 MB，请减少片段数量或文字长度；原保存记录未替换。");
            cancellationToken.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
