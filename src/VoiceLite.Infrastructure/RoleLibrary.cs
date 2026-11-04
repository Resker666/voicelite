using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using VoiceLite.Core;

namespace VoiceLite.Infrastructure;

public sealed class RoleLibrary(string dataRoot) : IRoleLibrary
{
    private const long MiB = 1024 * 1024;
    private readonly string root = Path.GetFullPath(Path.Combine(dataRoot, "roles"));
    private sealed record Payload(string Path, long Bytes, string Sha256);
    private sealed record Manifest(string Name, string Version, string Gpt, string Sovits, string Reference,
        string Prompt, string Language, string[] Auxiliary, Payload[] Files);

    public async Task<IReadOnlyList<VoiceRole>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root)) return [];
        Plain(root);
        var roles = new List<VoiceRole>();
        foreach (var directory in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try { roles.Add(await ReadAsync(directory, id, false, cancellationToken)); }
            catch (Exception exception) when (Invalid(exception)) { /* A saved selection is validated separately and reports the error. */ }
        }
        return roles;
    }

    public async Task<VoiceRole> ValidateAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(id, "N", out _)) throw new VoiceLiteException("角色标识无效，请重新导入角色包。");
        try { return await ReadAsync(Path.Combine(root, id), id, true, cancellationToken); }
        catch (Exception exception) when (Invalid(exception) && exception is not VoiceLiteException)
        { throw new VoiceLiteException("角色文件缺失或损坏，请重新导入角色包。", exception); }
    }

    public async Task<VoiceRole> ImportAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root); Plain(root);
        var id = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(root, ".import-" + id);
        try
        {
            await using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
            if (input.Length > 1024 * MiB) throw new VoiceLiteException("角色包过大，本版最多支持 1 GiB 的 ZIP。");
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.Entries.Count is < 4 or > 8) throw new VoiceLiteException("角色包文件数量不符合本版格式。");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                SafeRelative(entry.FullName);
                var kind = ((uint)entry.ExternalAttributes >> 16) & 0xF000;
                if ((kind != 0 && kind != 0x8000) || (entry.ExternalAttributes & 0x400) != 0)
                    throw new VoiceLiteException("角色包包含链接或特殊文件，无法导入。");
                if (!entries.TryAdd(entry.FullName, entry) || entry.Length > Limit(entry.FullName) || entry.Length < 1)
                    throw new VoiceLiteException("角色包包含重名、空文件或过大的文件。");
                total = checked(total + entry.Length);
            }
            if (total > 1024 * MiB || !entries.TryGetValue("character.json", out var manifestEntry))
                throw new VoiceLiteException("角色包缺少根目录 character.json，或解压后体积过大。");
            byte[] manifestBytes;
            using (var memory = new MemoryStream())
            {
                await CopyAsync(manifestEntry, memory, cancellationToken);
                manifestBytes = memory.ToArray();
            }
            var manifest = Parse(manifestBytes);
            var expected = manifest.Files.Select(x => x.Path).Append("character.json").ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (entries.ContainsKey("README.txt")) expected.Add("README.txt");
            if (!expected.SetEquals(entries.Keys)) throw new VoiceLiteException("角色包包含未声明或缺少的文件。");
            Directory.CreateDirectory(staging); Plain(staging);
            foreach (var file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = entries[file.Path];
                if (entry.Length != file.Bytes) throw new VoiceLiteException("角色文件长度校验失败，请重新获取角色包。");
                var destination = Path.Combine(staging, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); Plain(Path.GetDirectoryName(destination)!);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                var hash = await CopyAsync(entry, output, cancellationToken);
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) throw new VoiceLiteException("角色文件 SHA256 校验失败，请重新获取角色包。");
            }
            await File.WriteAllBytesAsync(Path.Combine(staging, "character.json"), manifestBytes, cancellationToken);
            if (entries.TryGetValue("README.txt", out var readme))
            {
                await using var output = new FileStream(Path.Combine(staging, "README.txt"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
                await CopyAsync(readme, output, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var installed = Path.Combine(root, id);
            Directory.Move(staging, installed);
            return await ReadAsync(installed, id, false, cancellationToken: CancellationToken.None);
        }
        catch (Exception exception) when (Invalid(exception) && exception is not VoiceLiteException)
        { throw new VoiceLiteException("角色包导入失败，请检查 ZIP 格式、文件完整性和目录权限。", exception); }
        finally
        {
            // This path is generated here, is a direct child, and never comes from the archive.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private async Task<VoiceRole> ReadAsync(string directory, string id, bool verifyHash, CancellationToken cancellationToken)
    {
        Plain(directory);
        var manifestPath = Path.Combine(directory, "character.json"); Plain(manifestPath);
        if (new FileInfo(manifestPath).Length > 65536) throw new VoiceLiteException("角色清单过大，请重新导入。");
        var manifest = Parse(await File.ReadAllBytesAsync(manifestPath, cancellationToken));
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, file.Path); Plain(path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Bytes) throw new VoiceLiteException("角色文件缺失或损坏，请重新导入角色包。");
            if (verifyHash)
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new VoiceLiteException("角色文件 SHA256 校验失败，请重新导入角色包。");
            }
        }
        return new(id, manifest.Name, manifest.Version, Path.Combine(directory, manifest.Reference), manifest.Prompt,
            manifest.Language, manifest.Auxiliary.Select(x => Path.Combine(directory, x)).ToArray(),
            new(Path.Combine(directory, manifest.Gpt), Path.Combine(directory, manifest.Sovits)));
    }

    private static Manifest Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var json = document.RootElement;
        string Text(string key, int max = 2000)
        {
            var value = json.GetProperty(key).GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(x => x == '\0'))
                throw new VoiceLiteException("角色清单缺少有效的名称、版本、参考文字或语言。");
            return value;
        }
        if (Text("schema") != "voicelite-role-experiment-0" || Text("engine_family") != "GPT-SoVITS"
            || Text("model_version") != EngineLayout.Version || !Text("api_sha256").Equals(EngineLayout.ApiSha256, StringComparison.OrdinalIgnoreCase))
            throw new VoiceLiteException("此角色包与本版 v2ProPlus 引擎不兼容。");
        var gpt = Text("gpt"); var sovits = Text("sovits"); var reference = Text("reference");
        var auxiliary = json.GetProperty("auxiliary").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        var required = new[] { gpt, sovits, reference }.Concat(auxiliary).ToArray();
        foreach (var path in required) SafeRelative(path);
        if (auxiliary.Length > 3 || required.Distinct(StringComparer.OrdinalIgnoreCase).Count() != required.Length
            || !Path.GetExtension(gpt).Equals(".ckpt", StringComparison.OrdinalIgnoreCase)
            || !Path.GetExtension(sovits).Equals(".pth", StringComparison.OrdinalIgnoreCase)
            || !auxiliary.Prepend(reference).All(ReferenceAudioFormats.IsSupported))
            throw new VoiceLiteException("角色包的权重或参考录音格式不符合要求。");
        var language = Text("language");
        if (language is not ("zh" or "en")) throw new VoiceLiteException("本版仅支持中文和英文角色参考。");
        var files = json.GetProperty("files").EnumerateArray().Select(x => {
            if (!x.GetProperty("bytes").TryGetInt64(out var size))
                throw new VoiceLiteException("角色文件大小必须是有效的整数字节数。");
            return new Payload(x.GetProperty("path").GetString() ?? "", size, x.GetProperty("sha256").GetString() ?? "");
        }).ToArray();
        foreach (var file in files)
        {
            SafeRelative(file.Path);
            if (file.Bytes < 1 || file.Bytes > Limit(file.Path) || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new VoiceLiteException("角色文件清单的大小或 SHA256 无效。");
        }
        if (files.Length != required.Length || !files.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(required))
            throw new VoiceLiteException("角色文件清单与权重、参考录音不匹配。");
        return new(Text("name", 100).Trim(), Text("version", 100).Trim(), gpt, sovits, reference, Text("prompt_text"), language, auxiliary, files);
    }

    private static long Limit(string path) => Path.GetExtension(path).ToLowerInvariant() switch {
        ".ckpt" or ".pth" => 512 * MiB, ".wav" or ".mp3" or ".ogg" => 64 * MiB, _ => 65536
    };

    private static void SafeRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 200 || path.Contains('\\') || Path.IsPathRooted(path))
            throw new VoiceLiteException("角色包包含不安全的文件路径。");
        foreach (var part in path.Split('/'))
        {
            var stem = part.Split('.')[0];
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.Any(x => char.IsControl(x) || "<>:\"|?*".Contains(x))
                || new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
                throw new VoiceLiteException("角色包包含不安全的 Windows 文件路径。");
        }
    }

    private void Plain(string path)
    {
        var full = Path.GetFullPath(path);
        if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new VoiceLiteException("角色文件路径超出角色库。");
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new VoiceLiteException("角色库包含目录或文件链接，请使用普通文件目录。");
            if (current.Equals(Path.GetFullPath(dataRoot), StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    private static async Task<string> CopyAsync(ZipArchiveEntry entry, Stream output, CancellationToken cancellationToken)
    {
        await using var source = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536]; long count = 0; int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            count += read;
            if (count > entry.Length || count > Limit(entry.FullName)) throw new VoiceLiteException("角色文件解压后过大。");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (count != entry.Length) throw new VoiceLiteException("角色包文件不完整。");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static bool Invalid(Exception exception) => exception is VoiceLiteException or JsonException or IOException
        or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or ArgumentException or OverflowException;
}
