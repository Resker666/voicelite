using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VoiceLite.WorkflowProbe;

internal static class PhysicalDirectories
{
    public static (string Engine, string Data) ValidateExperiment(string engine, string data)
    {
        engine = Path.GetFullPath(engine); data = Path.GetFullPath(data);
        WorkflowChecks.Require(Directory.Exists(engine), "Engine directory is missing.");
        WorkflowChecks.Require(!Directory.Exists(data) && !File.Exists(data), "Use a fresh data directory.");
        WorkflowChecks.Require(!HasLinkedAncestor(data) && !HasLinkedAncestor(engine),
            "Test data must be outside the engine; use physical paths without directory links.");
        engine = Resolve(engine); data = Resolve(data);
        var relative = Path.GetRelativePath(engine, data);
        WorkflowChecks.Require(Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "Test data must be outside the engine; use physical paths without directory links.");
        return (engine, data);
    }
    private static bool HasLinkedAncestor(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    // Compare directory identities after Windows resolves extended, short and mapped paths.
    // https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew
    public static string Resolve(string path)
    {
        var missing = new Stack<string>();
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        while (!Directory.Exists(path))
        {
            if (File.Exists(path)) throw new IOException("Expected a directory.");
            missing.Push(Path.GetFileName(path));
            path = Path.GetDirectoryName(path) ?? throw new IOException("No existing directory ancestor.");
        }
        var extended = path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path
            : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        using var handle = CreateFileW(extended, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resolve the physical directory.");
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resolve the physical directory.");
        if (length >= buffer.Capacity) throw new PathTooLongException();
        var resolved = buffer.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = @"\\" + resolved[8..];
        else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        WorkflowChecks.Require(resolved.Length >= 3 && resolved[1] == ':', "The probe requires a local drive path.");
        while (missing.Count > 0) resolved = Path.Combine(resolved, missing.Pop());
        return Path.GetFullPath(resolved);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
