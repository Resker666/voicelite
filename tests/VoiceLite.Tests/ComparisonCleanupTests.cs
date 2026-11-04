using System.Security.Cryptography;
using VoiceLite.WorkflowProbe;
using Xunit;

namespace VoiceLite.Tests;

public sealed class ComparisonCleanupTests
{
    [Fact] public async Task Locked_import_does_not_skip_other_deletions_or_original_hashes()
    {
        using var folder = new CleanupFolder();
        var locked = folder.File("locked.wav"); var other = folder.File("other.wav"); var original = folder.File("original.wav");
        using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = await ComparisonCleanup.CompleteAsync([locked, other], [(original, Hash(original))]);
            Assert.NotEmpty(result.Errors);
            Assert.False(File.Exists(other));
            Assert.True(result.PrimaryUnchanged);
        }
    }
    [Fact] public async Task Failed_auxiliary_hash_is_reported_as_unverified()
    {
        using var folder = new CleanupFolder();
        var original = folder.File("original.wav");
        var result = await ComparisonCleanup.CompleteAsync([], [(original, Hash(original)), (Path.Combine(folder.Root, "missing.wav"), "not-read")]);
        Assert.NotEmpty(result.Errors);
        Assert.True(result.PrimaryUnchanged);
        Assert.False(result.AuxiliaryUnchanged);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private sealed class CleanupFolder : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "VoiceLite-cleanup-" + Guid.NewGuid().ToString("N"));
        public CleanupFolder() => Directory.CreateDirectory(Root);
        public string File(string name) { var path = Path.Combine(Root, name); System.IO.File.WriteAllBytes(path, [1, 2, 3]); return path; }
        public void Dispose()
        {
            var full = Path.GetFullPath(Root);
            if (!Path.GetDirectoryName(full)!.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to delete outside the test temporary root.");
            Directory.Delete(full, true);
        }
    }
}
