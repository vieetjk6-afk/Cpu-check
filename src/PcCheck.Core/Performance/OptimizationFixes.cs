using Microsoft.Win32;
using PcCheck.Core.Fixes;
using PcCheck.Core.Util;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Performance;

/// <summary>
/// Sửa nhanh các mục tối ưu – chỉ những thay đổi an toàn, đảo ngược được bằng cài đặt Windows thông thường
/// (gói điện năng, TRIM, tối ưu ổ định kỳ, Game Mode, ghi hình nền Game Bar, hiệu ứng trong suốt, SysMain).
/// Được gọi qua FixKind.RunTool với Arg = "opt:&lt;khóa&gt;".
/// </summary>
public static class OptimizationFixes
{
    public const string Prefix = "opt:";

    public static readonly IReadOnlyList<string> Keys =
        ["power-high", "trim-on", "optimize-on", "gamemode-on", "bgrecord-off", "transparency-off", "sysmain-on"];

    public static async Task<FixOutcome> ApplyAsync(string key, CancellationToken ct = default)
    {
        switch (key)
        {
            case "power-high":
                return await RunAsync("powercfg.exe", "/setactive " + OptimizationAnalyzer.HighPerformance, ct).ConfigureAwait(false);

            case "trim-on":
                return await RunAsync("fsutil.exe", "behavior set DisableDeleteNotify 0", ct).ConfigureAwait(false);

            case "optimize-on":
                return await RunAsync("schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /Enable", ct).ConfigureAwait(false);

            case "gamemode-on":
                SetUser(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
                SetUser(@"Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
                return Done();

            case "bgrecord-off":
                SetUser(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0);
                return Done();

            case "transparency-off":
                SetUser(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
                return Done();

            case "sysmain-on":
                var config = await RunAsync("sc.exe", "config SysMain start= auto", ct).ConfigureAwait(false);
                if (!config.Success) return config;
                await RunAsync("sc.exe", "start SysMain", ct).ConfigureAwait(false);
                return Done();

            default:
                return new FixOutcome(false, L("fix.result.unsupported"));
        }
    }

    private static FixOutcome Done() => new(true, L("opt.fix.done"));

    private static void SetUser(string path, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static async Task<FixOutcome> RunAsync(string exe, string args, CancellationToken ct)
    {
        var run = await HiddenProcess.RunAsync(Path.Combine(Environment.SystemDirectory, exe), args, ct, oem: true).ConfigureAwait(false);
        Log.Info($"{exe} {args}: exit {run.ExitCode}\n{run.Output.Trim()}");
        return run.ExitCode == 0 ? Done() : new FixOutcome(false, L("fix.result.failed", exe));
    }
}
