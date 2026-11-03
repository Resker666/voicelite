using System.Diagnostics;
using System.Security.Cryptography;
using VoiceLite.Core;
using VoiceLite.Infrastructure;
using Xunit;
namespace VoiceLite.Tests;

public sealed class ReviewRegressionTests
{
    [Fact] public async Task Playback_open_timeout_does_not_limit_audio_duration()
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var playback = PlaybackAwaiter.WaitAsync(Task.CompletedTask, ended.Task, TimeSpan.FromMilliseconds(20), default);
        await Task.Delay(80);
        Assert.False(playback.IsCompleted);
        ended.SetResult(); await playback;
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Export_cannot_overwrite_reference_even_with_different_path_case(bool upperCase)
    {
        using var fixture = new EngineFixture();
        var reference = Path.Combine(fixture.Root, "参考.wav"); var generated = Path.Combine(fixture.Root, "generated.wav");
        await File.WriteAllBytesAsync(reference, Fixtures.Wav(3)); await File.WriteAllBytesAsync(generated, Fixtures.Wav());
        var original = SHA256.HashData(await File.ReadAllBytesAsync(reference));
        await Assert.ThrowsAsync<VoiceLiteException>(() => WaveExporter.SaveAsync(generated, upperCase ? reference.ToUpperInvariant() : reference, protectedReferencePath: reference));
        Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(reference)));
    }
    [Fact] public async Task Process_cannot_execute_before_job_assignment_and_immediate_child_is_reaped()
    {
        using var fixture = new EngineFixture();
        var info = new ProcessStartInfo(Path.Combine(fixture.Root, "runtime/python.exe")) {
            WorkingDirectory = fixture.Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "--spawn-immediately" }
        };
        var childFile = Path.Combine(fixture.Root, "child.pid");
        try
        {
            using var job = new ProcessJob();
            using var process = WindowsChildProcess.Create(info);
            await Task.Delay(300); // Deliberately widen the pre-assignment window.
            Assert.False(File.Exists(childFile));
            process.ResumeInto(job);
            await process.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(File.Exists(childFile));
            var childId = int.Parse(await File.ReadAllTextAsync(childFile));
            using var child = Process.GetProcessById(childId);
            Assert.False(child.HasExited);
            job.Dispose();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (File.Exists(childFile))
            {
                try { using var child = Process.GetProcessById(int.Parse(File.ReadAllText(childFile))); if (!child.HasExited) { child.Kill(true); child.WaitForExit(); } }
                catch (ArgumentException) { }
            }
        }
    }
    [Fact] public async Task Invalid_ffmpeg_executable_has_actionable_error_and_no_partial()
    {
        using var fixture = new EngineFixture();
        var source = Path.Combine(fixture.Root, "valid.wav"); await File.WriteAllBytesAsync(source, Fixtures.Wav(3));
        var executable = Path.Combine(fixture.Root, "bad.exe"); await File.WriteAllTextAsync(executable, "invalid executable");
        var output = Path.Combine(fixture.Root, "imports");
        await Assert.ThrowsAsync<VoiceLiteException>(() => new AudioImportService().ImportAsync(source, executable, output, default));
        Assert.Empty(Directory.Exists(output) ? Directory.GetFiles(output) : []);
    }
}
