using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using VoiceLite.Core;
namespace VoiceLite.Infrastructure;

internal sealed class ProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;
    internal ProcessJob()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf<ExtendedLimits>()))
        { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error); }
    }
    internal void Assign(Process process)
    {
        if (!AssignProcessToJobObject(handle, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose() => handle.Dispose();
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long PerProcessUserTime, PerJobUserTime;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimits info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}

internal static class WindowsPorts
{
    internal sealed record Owner(IPAddress Address, int ProcessId);
    internal static IReadOnlyList<Owner> Listeners(int port)
    {
        int size = 0;
        var status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        if (status is not (0 or 122) || size is < 4 or > 8 * 1024 * 1024) throw new VoiceLiteException("无法核实本地端口归属，已停止连接。");
        for (int retry = 0; retry < 3; retry++)
        {
            var memory = Marshal.AllocHGlobal(size);
            try
            {
                status = GetExtendedTcpTable(memory, ref size, false, 2, 3, 0);
                if (status == 122) continue;
                if (status != 0) throw new VoiceLiteException("无法核实本地端口归属，已停止连接。");
                int rows = Marshal.ReadInt32(memory);
                if (rows < 0 || 4L + rows * 24L > size) throw new VoiceLiteException("本地端口表无效，已停止连接。");
                var result = new List<Owner>();
                for (int i = 0; i < rows; i++)
                {
                    var row = IntPtr.Add(memory, 4 + i * 24);
                    int networkPort = Marshal.ReadInt32(row, 8);
                    int localPort = ((networkPort & 255) << 8) | ((networkPort >> 8) & 255);
                    if (localPort == port) result.Add(new(new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row, 4))), Marshal.ReadInt32(row, 20)));
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        throw new VoiceLiteException("端口状态不断变化，请稍后再试。");
    }
    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
}
