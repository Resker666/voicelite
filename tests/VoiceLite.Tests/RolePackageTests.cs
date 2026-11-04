using System.Collections;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class RolePackageTests
{
    internal static dynamic Library(string root)
    {
        var type = typeof(GenerationService).Assembly.GetType("VoiceLite.Infrastructure.RoleLibrary");
        Assert.NotNull(type);
        return Activator.CreateInstance(type, root)!;
    }

    [Fact] public async Task Real_zip_import_lists_and_validates_owned_copies()
    {
        using var folder = new EngineFixture();
        var zip = RoleFixture.Zip(folder.Root);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(zip));
        dynamic library = Library(folder.Root);
        dynamic role = await library.ImportAsync(zip, CancellationToken.None);
        Assert.Equal("测试角色", (string)role.Name);
        Assert.NotEmpty((string)role.Id);
        Assert.StartsWith(Path.Combine(folder.Root, "roles"), (string)role.ReferencePath);
        Assert.True(File.Exists((string)role.Weights.GptPath));
        Assert.Single(Enumerable.Cast<object>((IEnumerable)await library.ListAsync(CancellationToken.None)));
        dynamic valid = await library.ValidateAsync((string)role.Id, CancellationToken.None);
        Assert.Equal((string)role.Id, (string)valid.Id);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(zip)));
    }

    [Theory]
    [InlineData("../escape.ckpt")]
    [InlineData("C:/escape.ckpt")]
    [InlineData("weights/gpt.ckpt:extra")]
    [InlineData("weights/CON.ckpt")]
    [InlineData("weights./gpt.ckpt")]
    [InlineData("weights\\gpt.ckpt")]
    public async Task Unsafe_manifest_paths_never_leave_an_install(string path)
    {
        using var folder = new EngineFixture();
        var zip = RoleFixture.Zip(folder.Root, json => json["gpt"] = path);
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await Library(folder.Root).ImportAsync(zip, CancellationToken.None));
        Assert.Empty(Directory.GetDirectories(Path.Combine(folder.Root, "roles")));
    }

    [Theory] [InlineData("hash")] [InlineData("size")] [InlineData("fractional-size")] [InlineData("version")] [InlineData("api")] [InlineData("missing")]
    public async Task Invalid_manifest_or_payload_is_rejected_and_staging_cleaned(string reason)
    {
        using var folder = new EngineFixture();
        var zip = RoleFixture.Zip(folder.Root, json => {
            if (reason == "hash") json["files"]![0]!["sha256"] = new string('0', 64);
            if (reason == "size") json["files"]![0]!["bytes"] = 999;
            if (reason == "fractional-size") json["files"]![0]!["bytes"] = 1.5;
            if (reason == "version") json["model_version"] = "v3";
            if (reason == "api") json["api_sha256"] = new string('0', 64);
            if (reason == "missing") json.Remove("prompt_text");
        });
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await Library(folder.Root).ImportAsync(zip, CancellationToken.None));
        Assert.Empty(Directory.GetDirectories(Path.Combine(folder.Root, "roles")));
    }

    [Theory] [InlineData("duplicate")] [InlineData("link")] [InlineData("extra")] [InlineData("expanded")]
    public async Task Archive_aliases_links_unknown_files_and_oversized_expansion_are_rejected(string reason)
    {
        using var folder = new EngineFixture();
        var zip = RoleFixture.Zip(folder.Root, extra: archive => {
            if (reason == "duplicate") archive.CreateEntry("WEIGHTS/GPT.CKPT");
            if (reason == "link") archive.GetEntry("weights/gpt.ckpt")!.ExternalAttributes = unchecked((int)0xA1FF0000);
            if (reason == "extra") archive.CreateEntry("run.py");
            if (reason == "expanded") {
                using var file = archive.CreateEntry("oversized.wav").Open();
                var buffer = new byte[1024 * 1024];
                for (int i = 0; i < 65; i++) file.Write(buffer);
            }
        });
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await Library(folder.Root).ImportAsync(zip, CancellationToken.None));
        Assert.Empty(Directory.GetDirectories(Path.Combine(folder.Root, "roles")));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Changed_or_missing_installed_weights_are_rejected_before_use(bool missing)
    {
        using var folder = new EngineFixture(); dynamic library = Library(folder.Root);
        dynamic role = await library.ImportAsync(RoleFixture.Zip(folder.Root), CancellationToken.None);
        var path = (string)role.Weights.SovitsPath;
        if (missing) File.Delete(path); else await File.WriteAllBytesAsync(path, [99, 2, 3, 4]);
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await library.ValidateAsync((string)role.Id, CancellationToken.None));
    }

    [Fact] public async Task Cancelled_import_keeps_existing_roles_and_leaves_no_staging()
    {
        using var folder = new EngineFixture(); dynamic library = Library(folder.Root);
        dynamic first = await library.ImportAsync(RoleFixture.Zip(folder.Root), CancellationToken.None);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await library.ImportAsync(RoleFixture.Zip(folder.Root), cancel.Token));
        var directories = Directory.GetDirectories(Path.Combine(folder.Root, "roles"));
        Assert.Single(directories); Assert.Equal((string)first.Id, Path.GetFileName(directories[0]));
    }

    [Fact] public async Task Cancelling_after_extraction_starts_removes_partial_files()
    {
        using var folder = new EngineFixture();
        var payload = new byte[16 * 1024 * 1024];
        var zip = RoleFixture.Zip(folder.Root, json => {
            json["files"]![0]!["bytes"] = payload.Length;
            json["files"]![0]!["sha256"] = Convert.ToHexString(SHA256.HashData(payload));
        }, archive => {
            archive.GetEntry("weights/gpt.ckpt")!.Delete();
            using var stream = archive.CreateEntry("weights/gpt.ckpt").Open(); stream.Write(payload);
        });
        var root = Path.Combine(folder.Root, "roles"); Directory.CreateDirectory(root);
        using var cancel = new CancellationTokenSource();
        using var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, EnableRaisingEvents = true };
        watcher.Created += (_, item) => { if (item.Name!.EndsWith("gpt.ckpt", StringComparison.Ordinal)) cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Library(folder.Root).ImportAsync(zip, cancel.Token));
        Assert.Empty(Directory.GetFileSystemEntries(root));
    }

    [Fact] public async Task Installed_reference_junction_is_rejected_without_following_it()
    {
        using var folder = new EngineFixture(); var library = new RoleLibrary(folder.Root);
        var role = await library.ImportAsync(RoleFixture.Zip(folder.Root));
        var references = Path.GetDirectoryName(role.ReferencePath)!;
        var target = Path.Combine(folder.Root, "detached-references"); Directory.Move(references, target);
        var info = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "New-Item -ItemType Junction -Path $env:ROLE_LINK -Target $env:ROLE_TARGET -ErrorAction Stop | Out-Null" }) info.ArgumentList.Add(argument);
        info.Environment["ROLE_LINK"] = references; info.Environment["ROLE_TARGET"] = target;
        using var process = System.Diagnostics.Process.Start(info)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(0, process.ExitCode);
        try { await Assert.ThrowsAsync<VoiceLiteException>(() => library.ValidateAsync(role.Id)); Assert.True(File.Exists(Path.Combine(target, "main.wav"))); }
        finally { Directory.Delete(references); }
    }

    [Fact] public async Task Malformed_installed_manifest_does_not_block_other_roles()
    {
        using var folder = new EngineFixture(); var library = new RoleLibrary(folder.Root);
        var broken = await library.ImportAsync(RoleFixture.Zip(folder.Root));
        var healthy = await library.ImportAsync(RoleFixture.Zip(folder.Root));
        var manifestPath = Path.Combine(folder.Root, "roles", broken.Id, "character.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        json["files"]![0]!["bytes"] = 1.5;
        await File.WriteAllTextAsync(manifestPath, json.ToJsonString());
        Assert.Equal(healthy.Id, Assert.Single(await library.ListAsync()).Id);
        await Assert.ThrowsAsync<VoiceLiteException>(() => library.ValidateAsync(broken.Id));
    }
}

internal static class RoleFixture
{
    internal static string Zip(string root, Action<JsonObject>? change = null, Action<ZipArchive>? extra = null)
    {
        var files = new Dictionary<string, byte[]> {
            ["weights/gpt.ckpt"] = [1, 2, 3, 4], ["weights/sovits.pth"] = [4, 3, 2, 1],
            ["references/main.wav"] = Fixtures.Wav(3), ["references/extra.wav"] = Fixtures.Wav(3)
        };
        var json = JsonSerializer.SerializeToNode(new {
            schema = "voicelite-role-experiment-0", name = "测试角色", version = "test-1",
            engine_family = "GPT-SoVITS", model_version = EngineLayout.Version, api_sha256 = EngineLayout.ApiSha256,
            gpt = "weights/gpt.ckpt", sovits = "weights/sovits.pth", reference = "references/main.wav",
            prompt_text = "参考文字", language = "zh", auxiliary = new[] { "references/extra.wav" },
            files = files.Select(x => new { path = x.Key, bytes = x.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(x.Value)) }).ToArray()
        })!.AsObject();
        change?.Invoke(json);
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
            foreach (var file in files) { using var stream = archive.CreateEntry(file.Key).Open(); stream.Write(file.Value); }
            using (var writer = new StreamWriter(archive.CreateEntry("character.json").Open())) writer.Write(json.ToJsonString());
        }
        if (extra is not null) { using var archive = ZipFile.Open(path, ZipArchiveMode.Update); extra(archive); }
        return path;
    }
}
