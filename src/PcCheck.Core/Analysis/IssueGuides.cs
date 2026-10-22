using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>
/// Gắn hướng dẫn khắc phục chi tiết cho từng loại vấn đề theo tiền tố Id.
/// Nội dung nằm trong bộ chuỗi: guide.&lt;khóa&gt;.cause và guide.&lt;khóa&gt;.steps (các bước cách nhau bằng xuống dòng).
/// </summary>
public static class IssueGuides
{
    /// <summary>Tiền tố Id vấn đề -> khóa hướng dẫn. Tiền tố dài hơn được ưu tiên.</summary>
    public static readonly IReadOnlyList<(string Prefix, string Key)> Map =
    [
        ("temp/cpu", "cpu_hot"),
        ("temp/gpu", "gpu_hot"),
        ("temp/pawnio", "cpu_sensor"),
        ("disk/failing/", "disk_failing"),
        ("disk/warning/", "disk_failing"),
        ("disk/wear/", "ssd_wear"),
        ("disk/errors/", "disk_errors"),
        ("events/disk", "disk_errors"),
        ("disk/temp/", "disk_hot"),
        ("volume/space/", "low_space"),
        ("battery/wear", "battery"),
        ("ram/usage", "ram_full"),
        ("events/low_memory", "ram_full"),
        ("gpu/error/", "gpu_error"),
        ("driver/Missing", "driver_missing"),
        ("driver/DeviceError", "driver_error"),
        ("driver/generic_display", "driver_display"),
        ("driver/generic_audio", "driver_audio"),
        ("driver/unsigned", "driver_unsigned"),
        ("driver/outdated", "driver_outdated"),
        ("driver/disabled", "driver_disabled"),
        ("events/bsod", "bsod"),
        ("events/power_loss", "power_loss"),
        ("events/whea", "whea"),
        ("events/storage_reset", "storage_reset"),
        ("events/ntfs", "ntfs"),
        ("events/tdr", "tdr"),
        ("events/app_crash", "app_crash"),
        ("system/not_admin", "not_admin"),
        ("system/eos", "windows_eos"),
        ("system/pending_reboot", "pending_reboot"),
        ("system/uptime", "uptime"),
        ("security/no_av", "no_av"),
        ("security/av_outdated", "av_outdated"),
        ("security/firewall", "firewall"),
        ("security/uac", "uac"),
        ("system/activation", "activation"),
        ("system/updates", "updates"),
        ("system/pagefile", "pagefile"),
        ("system/windows_old", "windows_old"),
        ("junk/total", "junk"),
        ("software/bloat", "bloat"),
        ("software/unused", "unused"),
        ("startup/count", "startup"),
        ("compat/ram/channel", "ram_channel"),
        ("compat/ram/matched", "ram_matched"),
        ("compat/ram/speed", "ram_xmp"),
        ("compat/ram/capacity", "ram_capacity"),
        ("compat/ram/upgrade", "ram_upgrade"),
        ("compat/bios/age", "bios_age"),
        ("compat/cpu/virtualization", "virtualization"),
        ("compat/storage/system_ssd", "system_hdd"),
        ("compat/storage/system_usb", "system_usb"),
        ("compat/os/arch", "os_32bit"),
        ("compat/security/multi_av", "multi_av"),
        ("compat/display/refresh", "refresh_rate"),
        ("compat/display/resolution", "resolution"),
        ("compat/win11/summary", "win11"),
    ];

    /// <summary>Hướng dẫn của trang Bảo mật: Id dạng "secure/&lt;khóa&gt;/..." (khóa bắt đầu bằng "sec_").</summary>
    public static readonly IReadOnlyList<string> SecurityKeys =
    [
        "sec_spyware", "sec_monitoring", "sec_remote", "sec_device_access", "sec_autostart", "sec_process", "sec_masquerade",
        "sec_network", "sec_listen", "sec_proxy", "sec_browser_policy", "sec_browser_forced", "sec_extension", "sec_root_cert",
        "sec_hosts", "sec_threat", "sec_exclusion", "sec_tamper", "sec_rdp", "sec_remote_service", "sec_smb1", "sec_autologon",
        "sec_hidden_account", "sec_builtin_admin", "sec_guest", "sec_admins", "sec_managed", "sec_ifeo", "sec_appinit",
        "sec_winlogon", "sec_smartscreen", "sec_secureboot", "sec_bitlocker",
    ];

    public static IEnumerable<string> AllKeys => Map.Select(m => m.Key).Concat(SecurityKeys).Distinct();

    public static IssueGuide? Find(string issueId)
    {
        if (issueId.StartsWith("secure/", StringComparison.Ordinal))
        {
            var rest = issueId[7..];
            var slash = rest.IndexOf('/');
            var key = slash > 0 ? rest[..slash] : rest;
            return SecurityKeys.Contains(key) ? ForKey(key) : null;
        }

        var hit = Map.Where(m => issueId.StartsWith(m.Prefix, StringComparison.Ordinal))
            .OrderByDescending(m => m.Prefix.Length)
            .Select(m => m.Key)
            .FirstOrDefault();
        return hit is null ? null : ForKey(hit);
    }

    public static IssueGuide ForKey(string key) => new(key, L($"guide.{key}.cause"), L($"guide.{key}.steps"));

    public static void Attach(IEnumerable<Issue> issues)
    {
        foreach (var issue in issues) issue.Guide ??= Find(issue.Id);
    }
}
