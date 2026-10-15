using System.Runtime.InteropServices;

namespace PcCheck.Core.Util;

/// <summary>Số liệu nhẹ cho giám sát trực tiếp (không cần LibreHardwareMonitor): RAM, % CPU.</summary>
public sealed class SystemMetrics
{
    private long _lastIdle;
    private long _lastTotal;

    public static (long Total, long Available) Memory()
    {
        var (total, available) = NativeMethods.GetMemoryStatus();
        return ((long)total, (long)available);
    }

    /// <summary>% CPU tính theo chênh lệch GetSystemTimes giữa hai lần gọi (lần đầu trả về null).</summary>
    public double? CpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var idleTicks = idle.ToLong();
        var total = kernel.ToLong() + user.ToLong(); // kernel đã bao gồm idle
        double? result = null;
        if (_lastTotal != 0)
        {
            var dTotal = total - _lastTotal;
            var dIdle = idleTicks - _lastIdle;
            if (dTotal > 0) result = Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
        }

        _lastIdle = idleTicks;
        _lastTotal = total;
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly long ToLong() => ((long)High << 32) | Low;
    }

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);
#pragma warning restore SYSLIB1054
}
