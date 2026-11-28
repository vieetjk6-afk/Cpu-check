using System.Text.RegularExpressions;
using Microsoft.Win32;
using PcCheck.Core.Fixes;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Performance;

/// <summary>Đọc trạng thái hệ thống cho phần kiểm tra tối ưu (registry, powercfg, fsutil, schtasks). Chỉ đọc, không thay đổi gì.</summary>
public static partial class OptimizationProbe
{
    public static async Task<OptimizationData> CollectAsync(ScanResult? scan, CancellationToken ct = default)
    {
        var d = new OptimizationData();
        var hw = scan?.Data.Hardware;

        var (total, available) = SystemMetrics.Memory();
        d.MemoryTotal = total;
        d.MemoryAvailable = available;
        d.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        d.IsLaptop = hw?.Computer.IsLaptop ?? hw?.Battery is not null;
        d.PhysicalCores = hw?.Cpus.Sum(c => c.Cores) ?? 0;
        d.HasDedicatedGpu = hw?.Gpus.Any(g => !g.IsVirtual && !g.IsBasicDriver && g.VramBytes >= 2L * OptimizationAnalyzer.GiB) ?? false;
        d.PendingReboot = scan?.Data.System.PendingReboot ?? false;
        d.JunkBytes = scan?.Data.Junk is { Count: > 0 } junk ? junk.Sum(j => j.Bytes) : null;
        d.StartupEnabled = scan?.Data.Startup is { Count: > 0 } startup ? startup.Count(s => s.Enabled) : null;

        if (hw is not null)
        {
            var modules = hw.Memory.Modules.Where(m => m.CapacityBytes > 0).ToList();
            d.MemoryModules = modules.Count;
            var rated = modules.Select(m => m.SpeedMhz).DefaultIfEmpty(0).Min();
            var configured = modules.Select(m => m.ConfiguredSpeedMhz).Where(s => s > 0).DefaultIfEmpty(0).Min();
            d.MemoryRatedMhz = rated;
            d.MemoryConfiguredMhz = configured;
            d.MemoryBelowRated = rated > 0 && configured > 0 && configured < rated * 0.9;

            var systemLetter = Path.GetPathRoot(Environment.SystemDirectory)?[..1];
            var volume = hw.Volumes.FirstOrDefault(v => v.IsSystem) ?? hw.Volumes.FirstOrDefault(v => v.Letter.StartsWith(systemLetter ?? "C", StringComparison.OrdinalIgnoreCase));
            if (volume is not null)
            {
                d.SystemDriveTotal = volume.TotalBytes;
                d.SystemDriveFree = volume.FreeBytes;
            }
        }

        if (d.SystemDriveTotal == 0)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                d.SystemDriveTotal = drive.TotalSize;
                d.SystemDriveFree = drive.AvailableFreeSpace;
            }
            catch (IOException)
            {
            }
        }

        var systemLetterChar = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C")[0];
        var media = await Task.Run(() => Benchmark.DriveInfoFor(char.ToUpperInvariant(systemLetterChar)), ct).ConfigureAwait(false);
        d.SystemDriveIsHdd = media.Media switch
        {
            DriveMedia.Hdd => true,
            DriveMedia.SataSsd or DriveMedia.Nvme => false,
            _ => null,
        };

        await PowerAsync(d, ct).ConfigureAwait(false);
        d.TrimDisabled = ParseTrimDisabled((await RunAsync("fsutil.exe", "behavior query DisableDeleteNotify", ct).ConfigureAwait(false)).Output);
        var task = await RunAsync("schtasks.exe", "/Query /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /XML", ct).ConfigureAwait(false);
        d.ScheduledOptimizeEnabled = task.ExitCode == 0 ? ParseTaskEnabled(task.Output) : null;

        d.VisualEffects = ReadInt(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting") ?? 0;
        d.Transparency = ReadInt(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency") is { } t ? t != 0 : null;

        // Game Mode mặc định bật trên Windows 10 1903+ / 11 khi chưa có giá trị.
        d.GameMode = (ReadInt(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled") ?? 1) != 0;
        d.BackgroundRecording = (ReadInt(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled") ?? 0) != 0;
        d.HardwareGpuScheduling = ReadInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        d.SysMainStart = ReadInt(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SysMain", "Start");
        return d;
    }

    private static async Task PowerAsync(OptimizationData d, CancellationToken ct)
    {
        var active = await RunAsync("powercfg.exe", "/getactivescheme", ct).ConfigureAwait(false);
        d.PowerScheme = ParseGuid(active.Output);
        var list = await RunAsync("powercfg.exe", "/list", ct).ConfigureAwait(false);
        d.HasHighPerformanceScheme = list.Output.Contains(OptimizationAnalyzer.HighPerformance.ToString(), StringComparison.OrdinalIgnoreCase);
        var overlay = ReadString(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActiveOverlayAcPowerScheme");
        d.PowerModeOverlay = Guid.TryParse(overlay, out var g) && g != Guid.Empty ? g : null;
    }

    internal static Guid? ParseGuid(string output) =>
        GuidPattern().Match(output) is { Success: true } m && Guid.TryParse(m.Value, out var g) ? g : null;

    /// <summary>"NTFS DisableDeleteNotify = 0" (Windows 10+) hoặc "DisableDeleteNotify = 0" (bản cũ). 1 = TRIM tắt.</summary>
    internal static bool? ParseTrimDisabled(string output)
    {
        var m = NtfsTrimPattern().Match(output);
        if (!m.Success) m = TrimPattern().Match(output);
        return m.Success ? m.Groups[1].Value == "1" : null;
    }

    /// <summary>Đọc &lt;Enabled&gt; trong phần Settings của XML tác vụ (không phụ thuộc ngôn ngữ Windows). Thiếu = bật.</summary>
    internal static bool? ParseTaskEnabled(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || !xml.Contains("<Task", StringComparison.OrdinalIgnoreCase)) return null;
        var settings = xml.IndexOf("<Settings>", StringComparison.OrdinalIgnoreCase);
        var end = xml.IndexOf("</Settings>", StringComparison.OrdinalIgnoreCase);
        var block = settings >= 0 && end > settings ? xml[settings..end] : xml;
        var m = EnabledPattern().Match(block);
        return !m.Success || m.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ReadInt(RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name) switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var v) => v,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static string? ReadString(RegistryKey root, string path, string name)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue(name)?.ToString();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static async Task<ProcessOutput> RunAsync(string exe, string args, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            return await HiddenProcess.RunAsync(Path.Combine(Environment.SystemDirectory, exe), args, timeout.Token, oem: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"{exe} {args} lỗi", ex);
            return new ProcessOutput(-1, "", false);
        }
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"NTFS\s+DisableDeleteNotify\s*=\s*([01])", RegexOptions.IgnoreCase)]
    private static partial Regex NtfsTrimPattern();

    [GeneratedRegex(@"^\s*DisableDeleteNotify\s*=\s*([01])", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TrimPattern();

    [GeneratedRegex(@"<Enabled>\s*(true|false)\s*</Enabled>", RegexOptions.IgnoreCase)]
    private static partial Regex EnabledPattern();
}
