using System.Diagnostics;
using System.ComponentModel;
using VoiceLite.Core;
namespace VoiceLite.Infrastructure;

internal static class ProcessRunner
{
    internal sealed record Result(int ExitCode, string Output, string Error);
    internal static async Task<Result> RunAsync(ProcessStartInfo info, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        info.UseShellExecute = false; info.CreateNoWindow = true;
        info.RedirectStandardOutput = true; info.RedirectStandardError = true;
        WindowsChildProcess? child = null;
        using var job = new ProcessJob();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            child = WindowsChildProcess.Create(info); child.ResumeInto(job);
            var output = child.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = child.StandardError.ReadToEndAsync(deadline.Token);
            await child.Process.WaitForExitAsync(deadline.Token);
            return new(child.Process.ExitCode, await output, await error);
        }
        catch (Win32Exception exception) { throw new VoiceLiteException("音频工具无法启动，请检查离线包完整性。", exception); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new VoiceLiteException("音频处理超时，请选择较短的录音。"); }
        finally
        {
            if (child is not null)
            {
                if (!child.Process.HasExited) { child.Process.Kill(entireProcessTree: true); await child.Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                child.Dispose();
            }
        }
    }
}
