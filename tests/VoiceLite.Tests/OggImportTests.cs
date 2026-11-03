using System.Diagnostics;
using System.Security.Cryptography;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;

namespace VoiceLite.Tests;

public sealed class OggImportTests
{
    [Theory]
    [InlineData("声音.ogg", true)]
    [InlineData("声音.OGG", true)]
    [InlineData("声音.wav", true)]
    [InlineData("声音.mp3", true)]
    [InlineData("声音.mp4", false)]
    [InlineData("声音.txt", false)]
    public void Reference_format_policy_accepts_ogg_and_preserves_existing_formats(string path, bool expected) =>
        Assert.Equal(expected, ReferenceAudioFormats.IsSupported(path));

    [FfmpegTheory] [InlineData("libvorbis")] [InlineData("libopus")]
    public async Task Ogg_container_is_normalized_without_changing_original(string encoder)
    {
        using var fixture = new EngineFixture();
        var source = await CreateOggAsync(fixture.Root, encoder);
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var normalized = await new AudioImportService().ImportAsync(source, AudioTestTools.Ffmpeg, Path.Combine(fixture.Root, "导入"), default);
        var wave = WaveInfo.Inspect(await File.ReadAllBytesAsync(normalized));
        Assert.Equal(32000, wave.SampleRate); Assert.Equal(1, wave.Channels); Assert.Equal(16, wave.BitsPerSample);
        Assert.InRange(wave.DurationSeconds, 3.9, 4.1);
        Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
    }

    [FfmpegFact] public async Task Ogg_preview_uses_temporary_wave_and_cleans_it_after_playback()
    {
        using var fixture = new EngineFixture();
        var source = await CreateOggAsync(fixture.Root, "libvorbis");
        string converted;
        using (var preview = await new AudioPreviewService().PrepareAsync(source, AudioTestTools.Ffmpeg, Path.Combine(fixture.Root, "试听"), default))
        {
            converted = preview.FilePath;
            Assert.NotEqual(source, converted); Assert.Equal(".wav", Path.GetExtension(converted));
            Assert.InRange(WaveInfo.Inspect(await File.ReadAllBytesAsync(converted)).DurationSeconds, 3.9, 4.1);
        }
        Assert.False(File.Exists(converted)); Assert.True(File.Exists(source));
    }

    [Fact] public async Task Existing_long_wave_preview_does_not_require_ffmpeg_or_delete_source()
    {
        using var fixture = new EngineFixture();
        var source = Path.Combine(fixture.Root, "long.wav"); await File.WriteAllBytesAsync(source, Fixtures.Wav(41));
        using (var preview = await new AudioPreviewService().PrepareAsync(source, "missing-ffmpeg.exe", Path.Combine(fixture.Root, "unused"), default))
            Assert.Equal(source, preview.FilePath);
        Assert.True(File.Exists(source)); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "unused")));
    }

    [Fact] public async Task Cancel_during_ogg_preview_conversion_reaps_worker_and_leaves_no_preview()
    {
        using var fixture = new EngineFixture();
        var source = Path.Combine(fixture.Root, "source.ogg"); await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var runtime = Path.Combine(fixture.Root, "runtime");
        await File.WriteAllTextAsync(Path.Combine(runtime, "hang-preview.flag"), "yes");
        var destination = Path.Combine(fixture.Root, "preview");
        using var cancel = new CancellationTokenSource();
        var pending = new AudioPreviewService().PrepareAsync(source, Path.Combine(runtime, "python.exe"), destination, cancel.Token);
        var pidFile = Path.Combine(runtime, "preview.pid");
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(pidFile) && !pending.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
            Assert.True(File.Exists(pidFile), "Conversion worker did not start.");
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
            try { using var worker = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(pidFile))); Assert.True(worker.HasExited); }
            catch (ArgumentException) { }
            Assert.Empty(Directory.Exists(destination) ? Directory.GetFiles(destination) : []);
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source));
        }
        finally
        {
            cancel.Cancel();
            try { await pending; } catch (Exception) { }
        }
    }

    [FfmpegTheory] [InlineData(2)] [InlineData(19)]
    public async Task Ogg_can_be_previewed_outside_inference_duration_but_cannot_be_used_for_inference(int seconds)
    {
        using var fixture = new EngineFixture();
        var source = await CreateOggAsync(fixture.Root, "libvorbis", seconds);
        using (var preview = await new AudioPreviewService().PrepareAsync(source, AudioTestTools.Ffmpeg, Path.Combine(fixture.Root, "preview"), default))
            Assert.InRange(WaveInfo.Inspect(await File.ReadAllBytesAsync(preview.FilePath)).DurationSeconds, seconds - .1, seconds + .1);
        var imports = Path.Combine(fixture.Root, "imports");
        var exception = await Assert.ThrowsAsync<VoiceLiteException>(() => new AudioImportService().ImportAsync(source, AudioTestTools.Ffmpeg, imports, default));
        Assert.Contains("3–10", exception.Message); Assert.Empty(Directory.GetFiles(imports)); Assert.True(File.Exists(source));
    }

    internal static async Task<string> CreateOggAsync(string directory, string encoder, int seconds = 4)
    {
        var input = Path.Combine(directory, "原始.wav"); await File.WriteAllBytesAsync(input, Fixtures.Wav(seconds));
        var output = Path.Combine(directory, "参考 声音.OGG");
        var info = new ProcessStartInfo(AudioTestTools.Ffmpeg);
        foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-i", input, "-c:a", encoder, "-f", "ogg", output }) info.ArgumentList.Add(argument);
        var encoded = await ProcessRunner.RunAsync(info, TimeSpan.FromSeconds(10), default);
        Assert.Equal(0, encoded.ExitCode); return output;
    }
}
