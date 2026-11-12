using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Arkadia.App.Services;

public sealed record SystemMetricsSnapshot(double? CpuUsagePercent, double MemoryUsagePercent);

public sealed class WindowsSystemMetricsService
{
    private ulong? _previousIdle;
    private ulong? _previousKernel;
    private ulong? _previousUser;

    public SystemMetricsSnapshot Read()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);
        double? cpuUsage = null;
        if (_previousIdle is { } previousIdle
            && _previousKernel is { } previousKernel
            && _previousUser is { } previousUser)
        {
            var totalDelta = (kernel - previousKernel) + (user - previousUser);
            var idleDelta = idle - previousIdle;
            if (totalDelta > 0 && idleDelta <= totalDelta)
            {
                cpuUsage = Math.Clamp((double)(totalDelta - idleDelta) / totalDelta * 100d, 0d, 100d);
            }
        }

        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        var memory = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
        if (!GlobalMemoryStatusEx(ref memory))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var memoryUsage = memory.TotalPhysical == 0
            ? 0
            : Math.Clamp((double)(memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical * 100d, 0d, 100d);

        return new SystemMetricsSnapshot(cpuUsage, memoryUsage);
    }

    private static ulong ToUInt64(FileTime value) =>
        ((ulong)value.HighDateTime << 32) | value.LowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        public readonly uint LowDateTime;
        public readonly uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
