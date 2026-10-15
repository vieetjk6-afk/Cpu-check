using System.Globalization;
using Microsoft.Win32;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

/// <summary>Trạng thái hệ thống: bảo mật, cập nhật, kích hoạt, khởi động lại, UAC...</summary>
public static class SystemStatusCollector
{
    public static SystemStatus Collect()
    {
        var status = new SystemStatus
        {
            IsAdmin = NativeMethods.IsAdministrator(),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
        };

        Parallel.Invoke(
            () => Run(() => ReadPendingReboot(status)),
            () => Run(() => ReadAntivirus(status)),
            () => Run(() => ReadDefender(status)),
            () => Run(() => ReadFirewall(status)),
            () => Run(() => ReadLicense(status)),
            () => Run(() => ReadLastUpdate(status)),
            () => Run(() => ReadMisc(status)),
            () => Run(() => ReadPowerPlan(status)));

        return status;
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn($"System status {action.Method.Name} failed", ex);
        }
    }

    private static void ReadPendingReboot(SystemStatus status)
    {
        if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
            status.PendingRebootReasons.Add("CBS");
        if (KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
            status.PendingRebootReasons.Add("WindowsUpdate");
        using (var sm = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
        {
            if (sm?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 })
                status.PendingRebootReasons.Add("FileRename");
        }

        status.PendingReboot = status.PendingRebootReasons.Count > 0;
    }

    private static bool KeyExists(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key is not null;
    }

    private static void ReadAntivirus(SystemStatus status)
    {
        foreach (var row in Wmi.Query("SELECT displayName, productState FROM AntiVirusProduct", Wmi.SecurityCenter, 10))
        {
            var state = row.Long("productState") ?? 0;
            var (enabled, upToDate) = DecodeProductState(state);
            var name = row.Str("displayName");
            if (name.Length == 0 || status.Antivirus.Any(a => a.Name == name)) continue;
            status.Antivirus.Add(new AntivirusProduct(name, enabled, upToDate));
        }

        foreach (var row in Wmi.Query("SELECT displayName, productState FROM FirewallProduct", Wmi.SecurityCenter, 10))
        {
            var (enabled, _) = DecodeProductState(row.Long("productState") ?? 0);
            if (enabled) status.ThirdPartyFirewall = true;
        }
    }

    /// <summary>
    /// productState của Security Center: byte giữa 0x10/0x11 = đang bật; byte thấp 0x00 = cập nhật, 0x10 = lỗi thời.
    /// </summary>
    internal static (bool Enabled, bool UpToDate) DecodeProductState(long state)
    {
        var scanner = (state >> 8) & 0xFF;
        var definitions = state & 0xFF;
        return ((scanner & 0x10) != 0, definitions == 0x00);
    }

    private static void ReadDefender(SystemStatus status)
    {
        var row = Wmi.First("SELECT RealTimeProtectionEnabled, AntivirusSignatureAge, AntivirusEnabled FROM MSFT_MpComputerStatus", Wmi.Defender, 10);
        if (row is null) return;
        status.DefenderRealtime = row.Bool("RealTimeProtectionEnabled");
        var age = row.Int("AntivirusSignatureAge");
        status.DefenderSignatureAgeDays = age is >= 0 and < 65535 ? age : null;
    }

    private static void ReadFirewall(SystemStatus status)
    {
        const string root = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
        foreach (var (profile, name) in new[] { ("DomainProfile", "Domain"), ("StandardProfile", "Private"), ("PublicProfile", "Public") })
        {
            using var key = Registry.LocalMachine.OpenSubKey(root + profile);
            if (key?.GetValue("EnableFirewall") is int enabled)
            {
                status.FirewallKnown = true;
                if (enabled == 0) status.FirewallDisabledProfiles.Add(name);
            }
        }
    }

    private static void ReadLicense(SystemStatus status)
    {
        // ApplicationID của Windows; truy vấn này mất 1-3 giây nên chạy song song.
        var rows = Wmi.Query(
            "SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL",
            timeoutSeconds: 30);
        if (rows.Count == 0) return;
        status.LicenseStatus = rows.Any(r => r.Int("LicenseStatus") == 1) ? 1 : rows[0].Int("LicenseStatus");
    }

    private static void ReadLastUpdate(SystemStatus status)
    {
        DateTime? latest = null;
        foreach (var row in Wmi.Query("SELECT InstalledOn FROM Win32_QuickFixEngineering", timeoutSeconds: 30))
        {
            var date = ParseQfeDate(row.Str("InstalledOn"));
            if (date is not null && date <= DateTime.Now.AddDays(1) && (latest is null || date > latest)) latest = date;
        }

        status.LastUpdateInstalled = latest;
    }

    internal static DateTime? ParseQfeDate(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string[] formats = ["M/d/yyyy", "MM/dd/yyyy", "yyyyMMdd", "d/M/yyyy", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;

        // Một số bản ghi lưu FILETIME dạng hex.
        if (raw.Length >= 15 && long.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var ft))
        {
            try
            {
                return DateTime.FromFileTime(ft);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    private static void ReadMisc(SystemStatus status)
    {
        using (var power = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power"))
        {
            if (power?.GetValue("HiberbootEnabled") is int fast) status.FastStartupEnabled = fast == 1;
        }

        using (var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
        {
            if (policy?.GetValue("EnableLUA") is int lua) status.UacEnabled = lua != 0;
        }

        using (var mm = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
        {
            if (mm?.GetValue("PagingFiles") is string[] files)
            {
                status.PageFileEnabled = files.Any(f => !string.IsNullOrWhiteSpace(f));
            }
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        status.WindowsOldPresent = Directory.Exists(Path.Combine(systemRoot, "Windows.old"));
    }

    private static void ReadPowerPlan(SystemStatus status)
    {
        var row = Wmi.First("SELECT ElementName FROM Win32_PowerPlan WHERE IsActive = TRUE", Wmi.Power, 10);
        if (row is not null) status.PowerPlan = row.Str("ElementName");
    }
}
