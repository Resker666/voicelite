using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class DatasetServiceTests
{
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static dynamic Store(string root)
    {
        var type = typeof(GenerationService).Assembly.GetType("VoiceLite.Infrastructure.DatasetService"); Assert.NotNull(type);
        return Activator.CreateInstance(type, root)!;
    }
    internal static dynamic Edit(object dataset, Action<JsonNode> edit)
    {
        var json = JsonSerializer.SerializeToNode(dataset, dataset.GetType(), Json)!; edit(json);
        return JsonSerializer.Deserialize(json.ToJsonString(), dataset.GetType(), Json)!;
    }

    [Fact] public async Task New_dataset_round_trips_and_rejects_stale_revision()
    {
        using var folder = new EngineFixture(); dynamic store = Store(folder.Root);
        dynamic dataset = await store.CreateAsync("胡桃素材", CancellationToken.None);
        Assert.Equal("胡桃素材", (string)dataset.Name);
        dynamic saved = await store.SaveAsync(Edit((object)dataset, json => json["name"] = "已改名"), CancellationToken.None);
        Assert.Equal((int)dataset.Revision + 1, (int)saved.Revision);
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await store.SaveAsync(dataset, CancellationToken.None));
        dynamic loaded = await store.LoadAsync((string)dataset.Id, CancellationToken.None);
        Assert.Equal("已改名", (string)loaded.Name);
        Assert.Single(Enumerable.Cast<object>((IEnumerable)await store.ListAsync(CancellationToken.None)));
    }

    [FfmpegFact] public async Task Batch_import_copies_audio_deduplicates_and_preserves_originals()
    {
        using var folder = new EngineFixture(); dynamic store = Store(folder.Root);
        dynamic dataset = await store.CreateAsync("素材", CancellationToken.None);
        var source = Path.Combine(folder.Root, "原始 声音.wav"); await File.WriteAllBytesAsync(source, Fixtures.Wav(3));
        var duplicate = Path.Combine(folder.Root, "重复.wav"); File.Copy(source, duplicate);
        var invalid = Path.Combine(folder.Root, "invalid.txt"); File.WriteAllText(invalid, "not audio");
        var before = SHA256.HashData(await File.ReadAllBytesAsync(source));
        dynamic result = await store.ImportAsync((string)dataset.Id, new[] { source, duplicate, invalid }, AudioTestTools.Ffmpeg, new Progress<string>(), CancellationToken.None);
        var clips = Enumerable.Cast<object>((IEnumerable)result.Dataset.Clips).ToArray(); Assert.Single(clips);
        Assert.Equal(2, Enumerable.Cast<object>((IEnumerable)result.Issues).Count());
        dynamic clip = clips[0]; Assert.False((bool)clip.Reviewed); Assert.True((bool)clip.Included);
        string copied = await store.GetAudioPathAsync((string)dataset.Id, (string)clip.Id, CancellationToken.None);
        Assert.NotEqual(source, copied); Assert.Equal(3, WaveInfo.Inspect(await File.ReadAllBytesAsync(copied)).DurationSeconds);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        Assert.DoesNotContain(folder.Root, await File.ReadAllTextAsync(Path.Combine(folder.Root, "datasets", (string)dataset.Id, "dataset.json")));
    }

    [FfmpegTheory] [InlineData(1)] [InlineData(19)]
    public async Task Training_import_rejects_out_of_range_clips_without_truncation(int seconds)
    {
        using var folder = new EngineFixture(); dynamic store = Store(folder.Root);
        dynamic dataset = await store.CreateAsync("素材", CancellationToken.None);
        var source = Path.Combine(folder.Root, "source.wav"); await File.WriteAllBytesAsync(source, Fixtures.Wav(seconds));
        dynamic result = await store.ImportAsync((string)dataset.Id, new[] { source }, AudioTestTools.Ffmpeg, new Progress<string>(), CancellationToken.None);
        Assert.Empty(Enumerable.Cast<object>((IEnumerable)result.Dataset.Clips));
        Assert.Single(Enumerable.Cast<object>((IEnumerable)result.Issues));
        Assert.Empty(Directory.GetFiles(Path.Combine(folder.Root, "datasets", (string)dataset.Id, "wavs")));
    }

    [FfmpegFact] public async Task Export_only_includes_enabled_reviewed_safe_labels_and_relative_audio()
    {
        using var folder = new EngineFixture(); dynamic store = Store(folder.Root);
        dynamic dataset = await store.CreateAsync("素材", CancellationToken.None);
        var paths = new[] { Path.Combine(folder.Root, "one.wav"), Path.Combine(folder.Root, "two.wav") };
        await File.WriteAllBytesAsync(paths[0], Fixtures.Wav(3)); await File.WriteAllBytesAsync(paths[1], Fixtures.Wav(4));
        dynamic imported = await store.ImportAsync((string)dataset.Id, paths, AudioTestTools.Ffmpeg, new Progress<string>(), CancellationToken.None);
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await store.ExportAsync((string)dataset.Id, Path.Combine(folder.Root, "exports"), CancellationToken.None));
        dynamic saved = await store.SaveAsync(Edit((object)imported.Dataset, json => {
            json["clips"]![0]!["text"] = "测试校对文字"; json["clips"]![0]!["reviewed"] = true;
            json["clips"]![1]!["text"] = "未校对草稿";
        }), CancellationToken.None);
        string export = await store.ExportAsync((string)dataset.Id, Path.Combine(folder.Root, "exports"), CancellationToken.None);
        var line = Assert.Single(await File.ReadAllLinesAsync(Path.Combine(export, "train.list")));
        Assert.StartsWith("wavs/", line); Assert.EndsWith("|voicelite|ZH|测试校对文字", line);
        Assert.Single(Directory.GetFiles(Path.Combine(export, "wavs")));
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await store.SaveAsync(Edit((object)saved, json => json["clips"]![0]!["text"] = "unsafe|label"), CancellationToken.None));
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await store.ExportAsync((string)dataset.Id, Path.Combine(folder.Root, "datasets"), CancellationToken.None));
    }

    [FfmpegFact] public async Task Altered_wav_or_manifest_path_is_rejected_before_use()
    {
        using var folder = new EngineFixture(); dynamic store = Store(folder.Root);
        dynamic dataset = await store.CreateAsync("素材", CancellationToken.None);
        var source = Path.Combine(folder.Root, "one.wav"); await File.WriteAllBytesAsync(source, Fixtures.Wav(3));
        dynamic result = await store.ImportAsync((string)dataset.Id, new[] { source }, AudioTestTools.Ffmpeg, new Progress<string>(), CancellationToken.None);
        dynamic clip = ((IEnumerable)result.Dataset.Clips).Cast<object>().Single();
        string path = await store.GetAudioPathAsync((string)dataset.Id, (string)clip.Id, CancellationToken.None);
        await File.WriteAllBytesAsync(path, Fixtures.Wav(4));
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await store.GetAudioPathAsync((string)dataset.Id, (string)clip.Id, CancellationToken.None));
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await store.SaveAsync(Edit((object)result.Dataset, json => json["clips"]![0]!["wavePath"] = "../outside.wav"), CancellationToken.None));
    }

    [Theory] [InlineData("../outside")] [InlineData("C:\\outside")] [InlineData("")]
    public async Task Invalid_dataset_id_is_rejected(string id)
    {
        using var folder = new EngineFixture();
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await Store(folder.Root).LoadAsync(id, CancellationToken.None));
    }

    [Fact] public async Task Cancel_before_import_does_not_change_existing_dataset()
    {
        using var folder = new EngineFixture(); dynamic store = Store(folder.Root);
        dynamic dataset = await store.CreateAsync("素材", CancellationToken.None);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.ImportAsync((string)dataset.Id, new[] { "missing.wav" }, "missing.exe", new Progress<string>(), cancel.Token));
        dynamic loaded = await store.LoadAsync((string)dataset.Id, CancellationToken.None);
        Assert.Empty(Enumerable.Cast<object>((IEnumerable)loaded.Clips)); Assert.Equal((int)dataset.Revision, (int)loaded.Revision);
    }

    [Fact] public async Task Null_clip_in_one_manifest_does_not_prevent_other_datasets_from_opening()
    {
        using var folder = new EngineFixture(); dynamic store = Store(folder.Root);
        dynamic broken = await store.CreateAsync("损坏素材", CancellationToken.None);
        dynamic good = await store.CreateAsync("有效素材", CancellationToken.None);
        var manifestPath = Path.Combine(folder.Root, "datasets", (string)broken.Id, "dataset.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        json["clips"] = new JsonArray((JsonNode?)null); await File.WriteAllTextAsync(manifestPath, json.ToJsonString());
        var sets = Enumerable.Cast<object>((IEnumerable)await store.ListAsync(CancellationToken.None)).ToArray();
        Assert.Single(sets); Assert.Equal((string)good.Id, (string)((dynamic)sets[0]).Id);
        await Assert.ThrowsAsync<VoiceLiteException>(async () => await store.LoadAsync((string)broken.Id, CancellationToken.None));
    }

    [Fact] public async Task Oversized_label_manifest_does_not_replace_the_last_saved_data()
    {
        using var folder = new EngineFixture(); var store = new DatasetService(folder.Root);
        var dataset = await store.CreateAsync("文本大小边界");
        var clips = Enumerable.Range(0, 500).Select(_ => { var id = Guid.NewGuid().ToString("N"); return new TrainingClip(id, $"wavs/{id}.wav", "sample.wav", new string('0',64), new string('1',64),3); }).ToArray();
        dataset = dataset with { Clips = clips };
        var manifest = Path.Combine(folder.Root, "datasets", dataset.Id, "dataset.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(dataset, Json));
        var before = await File.ReadAllTextAsync(manifest);
        await Assert.ThrowsAsync<VoiceLiteException>(() => store.SaveAsync(dataset with { Clips = clips.Select(x => x with { Text = new string('中', 1000) }).ToArray() }));
        Assert.Equal(before, await File.ReadAllTextAsync(manifest));
    }
}
