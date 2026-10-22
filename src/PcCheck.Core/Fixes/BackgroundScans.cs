using System.Diagnostics;
using System.Text;
using PcCheck.Core.Localization;
using PcCheck.Core.Util;

namespace PcCheck.Core.Fixes;

public sealed record ProcessOutput(int ExitCode, string Output, bool Cancelled);

/// <summary>Chạy tiến trình ẩn, đọc đầu ra; hủy = dừng tiến trình.</summary>
internal static class HiddenProcess
{
    public static async Task<ProcessOutput> RunAsync(string file, string args, CancellationToken ct, Func<Task>? onCancel = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            },
        };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (output) output.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (output) output.AppendLine(e.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (onCancel is not null) await onCancel().ConfigureAwait(false);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }

            lock (output) return new ProcessOutput(-1, output.ToString(), true);
        }

        process.WaitForExit(); // chờ đọc hết đầu ra
        lock (output) return new ProcessOutput(process.ExitCode, output.ToString(), false);
    }
}

public enum DefenderScanType
{
    Quick = 1,
    Full = 2,
}

public sealed record DefenderScanResult(bool Started, bool Completed, bool Cancelled, IReadOnlyList<string> Threats, TimeSpan Duration);

/// <summary>
/// Quét bằng Microsoft Defender ngay trong ứng dụng (MpCmdRun chạy ẩn). Cửa sổ dòng lệnh của MpCmdRun không hiện tiến độ –
/// quét toàn bộ có thể mất 1–2 giờ – nên ứng dụng tự hiện thời gian đã chạy và cho phép dừng.
/// </summary>
public static class DefenderScanner
{
    public static async Task<DefenderScanResult> RunAsync(DefenderScanType type, CancellationToken ct = default)
    {
        var mp = FixService.MpCmdRun();
        if (mp is null) return new DefenderScanResult(false, false, false, [], TimeSpan.Zero);

        var start = DateTime.Now;
        var watch = Stopwatch.StartNew();
        var result = await HiddenProcess.RunAsync(mp, $"-Scan -ScanType {(int)type}", ct,
            onCancel: () => HiddenProcess.RunAsync(mp, "-Scan -Cancel", CancellationToken.None)).ConfigureAwait(false);
        Log.Info($"Defender scan {type}: exit {result.ExitCode}, cancelled={result.Cancelled}, {watch.Elapsed}\n{result.Output.Trim()}");
        if (result.Cancelled) return new DefenderScanResult(true, false, true, [], watch.Elapsed);

        return new DefenderScanResult(true, true, false, await Task.Run(() => ThreatsSince(start.AddMinutes(-1)), CancellationToken.None).ConfigureAwait(false), watch.Elapsed);
    }

    /// <summary>Tên mối đe dọa Defender phát hiện từ thời điểm cho trước.</summary>
    private static List<string> ThreatsSince(DateTime since)
    {
        try
        {
            var names = Wmi.Query("SELECT ThreatID, ThreatName FROM MSFT_MpThreat", Wmi.Defender, 15)
                .GroupBy(r => r.Str("ThreatID"))
                .ToDictionary(g => g.Key, g => g.First().Str("ThreatName"));
            return Wmi.Query("SELECT ThreatID, InitialDetectionTime FROM MSFT_MpThreatDetection", Wmi.Defender, 15)
                .Where(d => d.Date("InitialDetectionTime") >= since)
                .Select(d => names.GetValueOrDefault(d.Str("ThreatID")) is { Length: > 0 } n ? n : "Threat " + d.Str("ThreatID"))
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc kết quả quét Defender lỗi", ex);
            return [];
        }
    }
}

public enum DiskCheckStatus
{
    Healthy,
    ErrorsFound,
    Failed,
}

public sealed record DiskCheckResult(DiskCheckStatus Status, string Drive, string Detail);

/// <summary>
/// Kiểm tra lỗi hệ thống tệp ngay trong ứng dụng. Dùng Repair-Volume -Scan (qua dịch vụ lưu trữ của Windows), dự phòng
/// bằng chkdsk /scan. Cửa sổ chkdsk riêng có thể báo "Access is denied" trên một số máy – cách này tránh được.
/// </summary>
public static class DiskChecker
{
    public static string Normalize(string? drive) =>
        drive is { Length: > 0 } d && char.IsAsciiLetter(d[0]) ? char.ToUpperInvariant(d[0]).ToString() : "C";

    public static async Task<DiskCheckResult> ScanAsync(string? drive, CancellationToken ct = default)
    {
        var letter = Normalize(drive);
        var ps = await HiddenProcess.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='Stop'; (Repair-Volume -DriveLetter {letter} -Scan).ToString()\"", ct).ConfigureAwait(false);
        Log.Info($"Repair-Volume -Scan {letter}: exit {ps.ExitCode}\n{ps.Output.Trim()}");
        if (ps.Cancelled) return new DiskCheckResult(DiskCheckStatus.Failed, letter, "cancelled");

        var parsed = ParseRepairVolume(ps.Output);
        if (ps.ExitCode == 0 && parsed is not null) return new DiskCheckResult(parsed.Value, letter, ps.Output.Trim());

        // Dự phòng: chkdsk /scan (quét trực tuyến, không cần khởi động lại).
        var chk = await HiddenProcess.RunAsync("chkdsk.exe", $"{letter}: /scan", ct).ConfigureAwait(false);
        Log.Info($"chkdsk {letter}: /scan: exit {chk.ExitCode}\n{chk.Output.Trim()}");
        if (chk.Cancelled) return new DiskCheckResult(DiskCheckStatus.Failed, letter, "cancelled");
        return new DiskCheckResult(ParseChkdsk(chk.ExitCode, chk.Output), letter, chk.Output.Trim());
    }

    /// <summary>Kết quả Repair-Volume: NoErrorsFound / ScanNeeded / SpotFixNeeded / FullRepairNeeded.</summary>
    internal static DiskCheckStatus? ParseRepairVolume(string output)
    {
        var last = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
        return last switch
        {
            "NoErrorsFound" => DiskCheckStatus.Healthy,
            "ScanNeeded" or "SpotFixNeeded" or "FullRepairNeeded" => DiskCheckStatus.ErrorsFound,
            _ => null,
        };
    }

    /// <summary>Mã thoát chkdsk: 0 không lỗi; 1/2/3 có lỗi hoặc không kiểm tra được (bị từ chối truy cập...).</summary>
    internal static DiskCheckStatus ParseChkdsk(int exitCode, string output)
    {
        if (exitCode == 0) return DiskCheckStatus.Healthy;
        var denied = output.Contains("Access", StringComparison.OrdinalIgnoreCase) && output.Contains("denied", StringComparison.OrdinalIgnoreCase)
                     || output.Contains("từ chối", StringComparison.OrdinalIgnoreCase)
                     || output.Contains("Cannot open volume", StringComparison.OrdinalIgnoreCase);
        return denied || exitCode < 0 ? DiskCheckStatus.Failed : DiskCheckStatus.ErrorsFound;
    }

    /// <summary>
    /// Sửa lỗi: ổ hệ thống được đánh dấu để Windows tự sửa (chkdsk) ở lần khởi động sau; ổ khác sửa ngay bằng Repair-Volume -SpotFix.
    /// </summary>
    public static async Task<FixOutcome> RepairAsync(string? drive, CancellationToken ct = default)
    {
        var letter = Normalize(drive);
        var system = Normalize(Path.GetPathRoot(Environment.SystemDirectory)) == letter;
        if (system)
        {
            var dirty = await HiddenProcess.RunAsync("fsutil.exe", $"dirty set {letter}:", ct).ConfigureAwait(false);
            Log.Info($"fsutil dirty set {letter}: exit {dirty.ExitCode}\n{dirty.Output.Trim()}");
            if (dirty.ExitCode == 0) return new FixOutcome(true, LocFactory.L("disk_check.repair_scheduled", letter));
            var chk = await HiddenProcess.RunAsync("cmd.exe", $"/c echo Y| chkdsk {letter}: /f", ct).ConfigureAwait(false);
            return chk.Output.Contains(letter + ":", StringComparison.OrdinalIgnoreCase) && chk.ExitCode is 0 or 3
                ? new FixOutcome(true, LocFactory.L("disk_check.repair_scheduled", letter))
                : new FixOutcome(false, LocFactory.L("fix.result.failed", "chkdsk /f"));
        }

        var spot = await HiddenProcess.RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"$ErrorActionPreference='Stop'; (Repair-Volume -DriveLetter {letter} -SpotFix).ToString()\"", ct).ConfigureAwait(false);
        Log.Info($"Repair-Volume -SpotFix {letter}: exit {spot.ExitCode}\n{spot.Output.Trim()}");
        return spot.ExitCode == 0
            ? new FixOutcome(true, LocFactory.L("disk_check.repaired", letter))
            : new FixOutcome(false, LocFactory.L("fix.result.failed", "Repair-Volume"));
    }
}
