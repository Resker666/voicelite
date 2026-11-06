using System.Diagnostics;
using System.ComponentModel;
using System.Text;
using VoiceLite.Core;
namespace VoiceLite.Infrastructure;

internal static class ProcessRunner
{
    internal sealed record Result(int ExitCode, string Output, string Error);
    internal static async Task<Result> RunAsync(ProcessStartInfo info, TimeSpan timeout, CancellationToken cancellationToken, Action<string>? onOutputLine = null)
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
            var output = onOutputLine is null ? child.StandardOutput.ReadToEndAsync(deadline.Token) : PumpAsync(child.StandardOutput, onOutputLine, deadline.Token);
            var error = onOutputLine is null ? child.StandardError.ReadToEndAsync(deadline.Token) : PumpAsync(child.StandardError, null, deadline.Token);
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
    private static async Task<string> PumpAsync(StreamReader reader, Action<string>? onLine, CancellationToken cancellationToken)
    {
        var captured = new StringBuilder(); var pending = new StringBuilder(); var buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            captured.Append(buffer, 0, read);
            if (captured.Length > 65536) captured.Remove(0, captured.Length - 65536);
            if (onLine is null) continue;
            foreach (char character in buffer.AsSpan(0, read).ToArray())
            {
                if (character == '\n') { onLine(pending.ToString()); pending.Clear(); }
                else if (pending.Length < 1024) pending.Append(character);
            }
        }
        return captured.ToString();
    }
}
