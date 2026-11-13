using System.ComponentModel;
using System.Runtime.InteropServices;
using Serilog;

namespace Arkadia.App.Services;

public sealed record SystemMetricsSnapshot(
    double? CpuUsagePercent,
    IReadOnlyList<double> CpuCoreUsages,
    double MemoryUsagePercent,
    ulong MemoryUsedBytes,
    ulong MemoryAvailableBytes,
    ulong MemoryTotalBytes,
    double? GpuUsagePercent,
    ulong? GpuDedicatedMemoryBytes);

public sealed class WindowsSystemMetricsService : IDisposable
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhStatusValidData = 0;
    private const uint PdhStatusNewData = 1;
    private ulong? _previousIdle;
    private ulong? _previousKernel;
    private ulong? _previousUser;
    private readonly IntPtr _query;
    private readonly IReadOnlyList<PdhCounter> _processorCounters;
    private readonly IReadOnlyList<PdhCounter> _gpuEngineCounters;
    private readonly IReadOnlyList<PdhCounter> _gpuMemoryCounters;

    public WindowsSystemMetricsService()
    {
        if (PdhOpenQueryW(null, UIntPtr.Zero, out var query) != 0)
        {
            _query = IntPtr.Zero;
            _processorCounters = [];
            _gpuEngineCounters = [];
            _gpuMemoryCounters = [];
            return;
        }

        _query = query;
        _processorCounters = AddCounters(@"\Processor Information(*)\% Processor Utility");
        _gpuEngineCounters = AddCounters(@"\GPU Engine(*)\Utilization Percentage")
            .Where(counter => counter.Path.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _gpuMemoryCounters = AddCounters(@"\GPU Adapter Memory(*)\Dedicated Usage");
    }

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

        var memoryUsed = memory.TotalPhysical - memory.AvailablePhysical;
        var memoryUsage = memory.TotalPhysical == 0
            ? 0
            : Math.Clamp((double)memoryUsed / memory.TotalPhysical * 100d, 0d, 100d);
        var counters = ReadPdhCounters();
        var coreUsages = counters
            .Where(value => value.Counter.Path.Contains(@"\Processor Information(", StringComparison.OrdinalIgnoreCase)
                && !value.Counter.Path.Contains("_Total", StringComparison.OrdinalIgnoreCase))
            .Select(value => Math.Clamp(value.Value, 0d, 100d))
            .ToArray();
        var gpuUsages = counters
            .Where(value => _gpuEngineCounters.Contains(value.Counter))
            .Select(value => Math.Max(0d, value.Value))
            .ToArray();
        var gpuMemoryValues = counters
            .Where(value => _gpuMemoryCounters.Contains(value.Counter))
            .Select(value => Math.Max(0d, value.Value))
            .ToArray();

        return new SystemMetricsSnapshot(
            cpuUsage,
            coreUsages,
            memoryUsage,
            memoryUsed,
            memory.AvailablePhysical,
            memory.TotalPhysical,
            gpuUsages.Length == 0 ? null : Math.Clamp(gpuUsages.Max(), 0d, 100d),
            gpuMemoryValues.Length == 0
                ? null
                : (ulong)Math.Clamp(gpuMemoryValues.Sum(), 0d, long.MaxValue));
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            var status = PdhCloseQuery(_query);
            if (status != 0)
            {
                Log.Warning("Could not close the Windows performance counter query (status {Status}).", status);
            }
        }
    }

    private List<PdhCounter> AddCounters(string wildcardPath)
    {
        if (_query == IntPtr.Zero)
        {
            return [];
        }

        var paths = ExpandCounterPath(wildcardPath);
        var counters = new List<PdhCounter>(paths.Length);
        foreach (var path in paths)
        {
            if (PdhAddEnglishCounterW(_query, path, UIntPtr.Zero, out var counter) == 0)
            {
                counters.Add(new PdhCounter(path, counter));
            }
        }

        return counters;
    }

    private static string[] ExpandCounterPath(string wildcardPath)
    {
        uint bufferLength = 0;
        var status = PdhExpandWildCardPathW(
            null,
            wildcardPath,
            IntPtr.Zero,
            ref bufferLength,
            0);
        if (status != PdhMoreData || bufferLength == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal(checked((int)bufferLength * sizeof(char)));
        try
        {
            status = PdhExpandWildCardPathW(
                null,
                wildcardPath,
                buffer,
                ref bufferLength,
                0);
            return status == 0
                ? (Marshal.PtrToStringUni(buffer, checked((int)bufferLength)) ?? string.Empty)
                    .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                : [];
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private List<CounterValue> ReadPdhCounters()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
        {
            return [];
        }

        var values = new List<CounterValue>();
        foreach (var counter in _processorCounters
                     .Concat(_gpuEngineCounters)
                     .Concat(_gpuMemoryCounters))
        {
            if (PdhGetFormattedCounterValue(
                    counter.Handle,
                    PdhFormatDouble,
                    out _,
                    out var formattedValue) == 0
                && formattedValue.Status is PdhStatusValidData or PdhStatusNewData
                && double.IsFinite(formattedValue.DoubleValue))
            {
                values.Add(new CounterValue(counter, formattedValue.DoubleValue));
            }
        }

        return values;
    }

    private static ulong ToUInt64(FileTime value) =>
        ((ulong)value.HighDateTime << 32) | value.LowDateTime;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(
        [MarshalAs(UnmanagedType.LPWStr)] string? dataSource,
        UIntPtr userData,
        out IntPtr query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);

    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhExpandWildCardPathW(
        [MarshalAs(UnmanagedType.LPWStr)] string? dataSource,
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr expandedPathList,
        ref uint expandedPathListLength,
        uint flags);

    [DllImport("pdh.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(
        IntPtr query,
        [MarshalAs(UnmanagedType.LPWStr)] string fullCounterPath,
        UIntPtr userData,
        out IntPtr counter);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterValue(
        IntPtr counter,
        uint format,
        out uint counterType,
        out PdhFormattedCounterValue value);

    private sealed record PdhCounter(string Path, IntPtr Handle);
    private sealed record CounterValue(PdhCounter Counter, double Value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFormattedCounterValue
    {
        public uint Status;
        public double DoubleValue;
    }

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
