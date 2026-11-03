using VoiceLite.Core;
namespace VoiceLite.Infrastructure;
public static class WaveExporter
{
    public static async Task SaveAsync(string source, string destination, CancellationToken cancellationToken = default, string? protectedReferencePath = null)
    {
        var fullDestination = Path.GetFullPath(destination);
        if (!string.IsNullOrWhiteSpace(protectedReferencePath) && string.Equals(Path.GetFullPath(protectedReferencePath), fullDestination, StringComparison.OrdinalIgnoreCase))
            throw new VoiceLiteException("保存位置不能是参考录音，请使用另一个文件名，保护原始声音素材。");
        var bytes = await File.ReadAllBytesAsync(source, cancellationToken);
        WaveInfo.Inspect(bytes);
        if (string.Equals(Path.GetFullPath(source), fullDestination, StringComparison.OrdinalIgnoreCase)) return;
        var temporary = fullDestination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullDestination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
