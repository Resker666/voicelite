using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace VoiceLite.Infrastructure;

internal sealed class WindowsChildProcess : IDisposable
{
    internal Process Process { get; }
    internal StreamReader StandardOutput { get; }
    internal StreamReader StandardError { get; }
    private readonly SafeFileHandle thread;
    private bool resumed;
    private WindowsChildProcess(Process process, SafeFileHandle thread, SafeFileHandle stdout, SafeFileHandle stderr)
    {
        Process = process; this.thread = thread;
        StandardOutput = new(new FileStream(stdout, FileAccess.Read, 4096, false), Encoding.UTF8);
        StandardError = new(new FileStream(stderr, FileAccess.Read, 4096, false), Encoding.UTF8);
    }
    internal static WindowsChildProcess Create(ProcessStartInfo info)
    {
        var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), InheritHandle = true };
        if (!CreatePipe(out var outputRead, out var outputWrite, ref attributes, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!CreatePipe(out var errorRead, out var errorWrite, ref attributes, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                using (outputWrite) using (errorWrite)
                using (var input = CreateFile("NUL", 0x80000000, 3, ref attributes, 3, 0x80, IntPtr.Zero))
                {
                    if (input.IsInvalid || !SetHandleInformation(outputRead, 1, 0) || !SetHandleInformation(errorRead, 1, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x100,
                        StdInput = input.DangerousGetHandle(), StdOutput = outputWrite.DangerousGetHandle(), StdError = errorWrite.DangerousGetHandle() };
                    var executable = Path.GetFullPath(info.FileName);
                    var command = new StringBuilder(string.Join(" ", new[] { Quote(executable) }.Concat(info.ArgumentList.Select(Quote))));
                    var block = string.Join('\0', info.Environment.Where(x => x.Value is not null).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Key + "=" + x.Value)) + "\0\0";
                    var environment = Marshal.StringToHGlobalUni(block);
                    try
                    {
                        // No child instruction can run until ResumeInto has assigned the job.
                        if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08000404, environment,
                            string.IsNullOrEmpty(info.WorkingDirectory) ? Environment.CurrentDirectory : Path.GetFullPath(info.WorkingDirectory), ref startup, out var created))
                            throw new Win32Exception(Marshal.GetLastWin32Error());
                        using var nativeProcess = new SafeFileHandle(created.Process, true);
                        var nativeThread = new SafeFileHandle(created.Thread, true);
                        Process? process = null;
                        try
                        {
                            process = Process.GetProcessById(created.ProcessId);
                            _ = process.Handle; // Bind identity while the root is still suspended.
                            return new(process, nativeThread, outputRead, errorRead);
                        }
                        catch
                        {
                            TerminateProcess(nativeProcess, 1); nativeThread.Dispose(); process?.Dispose(); throw;
                        }
                    }
                    finally { Marshal.FreeHGlobal(environment); }
                }
            }
            catch { errorRead.Dispose(); errorWrite.Dispose(); throw; }
        }
        catch { outputRead.Dispose(); outputWrite.Dispose(); throw; }
    }
    internal void ResumeInto(ProcessJob job)
    {
        if (resumed) throw new InvalidOperationException("Process already resumed.");
        job.Assign(Process);
        if (ResumeThread(thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        resumed = true; thread.Dispose();
    }
    public void Dispose()
    {
        try { if (!Process.HasExited) { Process.Kill(true); Process.WaitForExit(); } }
        finally { thread.Dispose(); StandardOutput.Dispose(); StandardError.Dispose(); Process.Dispose(); }
    }
    private static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"')) return value;
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes); result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public ushort ShowWindow, ReservedSize; public IntPtr ReservedBytes, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, ref SecurityAttributes attributes, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string name, uint access, uint share, ref SecurityAttributes attributes, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string executable, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(SafeFileHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(SafeFileHandle process, uint exitCode);
}
