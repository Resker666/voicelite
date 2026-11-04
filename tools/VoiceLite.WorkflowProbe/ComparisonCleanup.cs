using System.IO;
using System.Security.Cryptography;

namespace VoiceLite.WorkflowProbe;

internal sealed record ComparisonCleanupResult(bool PrimaryUnchanged, bool AuxiliaryUnchanged, IReadOnlyList<Exception> Errors);

internal static class ComparisonCleanup
{
    public static async Task<ComparisonCleanupResult> CompleteAsync(IEnumerable<string> imports,
        IReadOnlyList<(string Path, string Hash)> originals)
    {
        ArgumentOutOfRangeException.ThrowIfZero(originals.Count);
        var errors = new List<Exception>();
        foreach (var path in imports)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception) { errors.Add(exception); }
        }
        var verified = new bool[originals.Count];
        for (int index = 0; index < originals.Count; index++)
        {
            try
            {
                verified[index] = originals[index].Hash == await HashAsync(originals[index].Path);
                if (!verified[index]) errors.Add(new IOException("An original reference was changed."));
            }
            catch (Exception exception) { errors.Add(exception); }
        }
        return new(verified[0], verified.Skip(1).All(x => x), errors);
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
