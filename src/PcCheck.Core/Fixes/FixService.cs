using System.Diagnostics;
using System.Globalization;
using PcCheck.Core.Collectors;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Fixes;

public sealed record FixOutcome(bool Success, LocText Message);

/// <summary>Thực thi hành động khắc phục. Các lệnh nặng (SFC, DISM, CHKDSK) mở cửa sổ console để người dùng theo dõi tiến trình.</summary>
public static class FixService
{
    public static async Task<FixOutcome> ExecuteAsync(FixAction action, CancellationToken ct = default)
    {
        try
        {
            return action.Kind switch
            {
                FixKind.OpenUri when action.Arg is not null => OpenUri(action.Arg),
                FixKind.RunTool when action.Arg is not null => await RunToolAsync(action.Arg, ct).ConfigureAwait(false),
                FixKind.Restart => Restart(),
                _ => new FixOutcome(false, L("fix.result.unsupported")),
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Thực thi {action.Kind} {action.Arg} lỗi", ex);
            return new FixOutcome(false, L("fix.result.failed", ex.Message));
        }
    }

    public static FixOutcome OpenUri(string uri)
    {
        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) || File.Exists(uri))
        {
            // Mở qua explorer để trình duyệt chạy với quyền người dùng thường (không kế thừa quyền admin).
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{uri}\"") { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }

        return new FixOutcome(true, L("fix.result.opened"));
    }

    private static FixOutcome Restart()
    {
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 10 /c \"PC Check\"") { UseShellExecute = false, CreateNoWindow = true });
        return new FixOutcome(true, L("fix.result.restarting"));
    }

    public static async Task<FixOutcome> RunToolAsync(string tool, CancellationToken ct = default)
    {
        var parts = tool.Split(':', 2);
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        switch (parts[0])
        {
            case SystemTools.SfcDism:
                OpenConsole("DISM /Online /Cleanup-Image /RestoreHealth & sfc /scannow");
                return new FixOutcome(true, L("fix.result.console"));

            case SystemTools.Chkdsk:
                var drive = parts.Length > 1 && parts[1].Length > 0 ? parts[1][..1] : "C";
                OpenConsole($"chkdsk {drive}: /scan");
                return new FixOutcome(true, L("fix.result.console"));

            case SystemTools.MemoryDiagnostic:
                Start("mdsched.exe");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.RestorePoint:
                var code = await RunHiddenAsync("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Checkpoint-Computer -Description 'PC Check' -RestorePointType 'MODIFY_SETTINGS'\"", ct).ConfigureAwait(false);
                if (code == 0) return new FixOutcome(true, L("fix.result.restore_point_ok"));
                Start("SystemPropertiesProtection.exe");
                return new FixOutcome(false, L("fix.result.restore_point_fail"));

            case SystemTools.FlushDns:
                return await RunHiddenAsync("ipconfig.exe", "/flushdns", ct).ConfigureAwait(false) == 0
                    ? new FixOutcome(true, L("fix.result.dns_ok"))
                    : new FixOutcome(false, L("fix.result.failed", "ipconfig"));

            case SystemTools.NetworkReset:
                OpenConsole("netsh winsock reset & netsh int ip reset & ipconfig /flushdns & echo. & echo " + Loc.T("fix.result.restart_needed"));
                return new FixOutcome(true, L("fix.result.restart_needed"));

            case SystemTools.WindowsUpdateReset:
                OpenConsole(
                    "net stop wuauserv & net stop bits & net stop cryptsvc & " +
                    $"ren \"%SystemRoot%\\SoftwareDistribution\" SoftwareDistribution.bak{stamp} & " +
                    $"ren \"%SystemRoot%\\System32\\catroot2\" catroot2.bak{stamp} & " +
                    "net start cryptsvc & net start bits & net start wuauserv");
                return new FixOutcome(true, L("fix.result.console"));

            case SystemTools.ScanDevices:
                var scan = await RunHiddenAsync("pnputil.exe", "/scan-devices", ct).ConfigureAwait(false);
                return scan == 0
                    ? new FixOutcome(true, L("fix.result.scan_devices_ok"))
                    : new FixOutcome(false, L("fix.result.failed", "pnputil"));

            case SystemTools.DeviceManager:
                Start("devmgmt.msc");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.EventViewer:
                Start("eventvwr.msc");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.ReliabilityMonitor:
                Start("perfmon.exe", "/rel");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.DiskCleanup:
                Start("cleanmgr.exe");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.PowerOptions:
                Start("control.exe", "powercfg.cpl");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.Uac:
                Start("UserAccountControlSettings.exe");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.PerformanceOptions:
                Start("SystemPropertiesPerformance.exe");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.CpuSensor:
                return await Sensors.PawnIoInstaller.InstallAsync(null, ct).ConfigureAwait(false);

            case SystemTools.StoreReset:
                Start("wsreset.exe");
                return new FixOutcome(true, L("fix.result.opened"));

            case SystemTools.BatteryReport:
                var report = Path.Combine(Path.GetTempPath(), $"battery-report-{stamp}.html");
                await RunHiddenAsync("powercfg.exe", $"/batteryreport /output \"{report}\"", ct).ConfigureAwait(false);
                if (!File.Exists(report)) return new FixOutcome(false, L("fix.result.no_battery"));
                return OpenUri(report);

            default:
                return new FixOutcome(false, L("fix.result.unsupported"));
        }
    }

    /// <summary>Gỡ cài đặt phần mềm (mở trình gỡ của chính phần mềm đó, hoặc Remove-AppxPackage cho app Store).</summary>
    public static async Task<FixOutcome> UninstallAsync(InstalledProgram program, CancellationToken ct = default)
    {
        try
        {
            if (program.Scope == ProgramScope.Store)
            {
                var name = program.PackageName.Replace("'", "", StringComparison.Ordinal);
                var code = await RunHiddenAsync("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-AppxPackage -Name '{name}' | Remove-AppxPackage\"", ct).ConfigureAwait(false);
                return code == 0
                    ? new FixOutcome(true, L("fix.result.uninstalled", program.Name))
                    : new FixOutcome(false, L("fix.result.failed", "Remove-AppxPackage"));
            }

            var (exe, args) = SplitCommand(program.UninstallCommand);
            if (exe.Length == 0) return new FixOutcome(false, L("fix.result.failed", program.Name));
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            return new FixOutcome(true, L("fix.result.uninstaller_started", program.Name));
        }
        catch (Exception ex)
        {
            Log.Error($"Gỡ {program.Name} lỗi", ex);
            return new FixOutcome(false, L("fix.result.failed", ex.Message));
        }
    }

    /// <summary>Tách lệnh gỡ cài đặt thành (exe, tham số); đổi MsiExec /I thành /X để gỡ thay vì sửa.</summary>
    internal static (string Exe, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return ("", "");
        var exe = StartupCollector.ExtractExecutable(command);
        var expanded = Environment.ExpandEnvironmentVariables(command);
        var rest = expanded.StartsWith('"')
            ? expanded[(Math.Min(expanded.Length, exe.Length + 2))..]
            : expanded.Length > exe.Length ? expanded[exe.Length..] : "";
        rest = rest.Trim();

        if (Path.GetFileName(exe).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) || exe.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest.Replace("/I{", "/X{", StringComparison.OrdinalIgnoreCase).Replace("/I {", "/X {", StringComparison.OrdinalIgnoreCase);
        }

        return (exe, rest);
    }

    private static void Start(string file, string args = "") =>
        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });

    private static void OpenConsole(string commands) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"title PC Check & {commands}\"") { UseShellExecute = true });

    private static async Task<int> RunHiddenAsync(string file, string args, CancellationToken ct)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (process is null) return -1;
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0) Log.Warn($"{file} {args} -> {process.ExitCode}: {output.Trim()}");
        return process.ExitCode;
    }
}
