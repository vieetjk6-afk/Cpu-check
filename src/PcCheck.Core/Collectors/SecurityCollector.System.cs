using Microsoft.Win32;
using PcCheck.Core.Analysis;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

public static partial class SecurityCollector
{
    // ===== Tài khoản =====

    private static List<LocalAccount> ReadAccounts()
    {
        var accounts = Wmi.Query("SELECT Name, SID, Disabled FROM Win32_UserAccount WHERE LocalAccount=True", Wmi.Cimv2, 20)
            .Select(r => new LocalAccount { Name = r.Str("Name"), Sid = r.Str("SID"), Enabled = r.Bool("Disabled") != true })
            .Where(a => a.Name.Length > 0)
            .ToList();

        // Nhóm Administrators theo SID (tên nhóm khác nhau theo ngôn ngữ Windows).
        var group = Wmi.First("SELECT Name, Domain FROM Win32_Group WHERE LocalAccount=True AND SID='S-1-5-32-544'", Wmi.Cimv2, 15);
        if (group is not null)
        {
            var members = Wmi.Query(
                    $"ASSOCIATORS OF {{Win32_Group.Domain='{group.Str("Domain")}',Name='{group.Str("Name")}'}} WHERE AssocClass=Win32_GroupUser Role=GroupComponent",
                    Wmi.Cimv2, 20)
                .Select(r => r.Str("SID"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var account in accounts) account.IsAdmin = members.Contains(account.Sid);
        }

        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList"))
        {
            foreach (var name in key?.GetValueNames() ?? [])
            {
                if (key!.GetValue(name) is int shown && shown == 0)
                {
                    var account = accounts.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (account is not null) account.HiddenFromLogon = true;
                }
            }
        }

        return accounts;
    }

    // ===== Truy cập từ xa & cấu hình =====

    private static RemoteExposure ReadExposure(List<ServiceEntry> services)
    {
        bool? Running(string name) => services.Count == 0
            ? null
            : services.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Running);

        var exposure = new RemoteExposure
        {
            WinRmRunning = Running("WinRM"),
            SshServerRunning = Running("sshd"),
            RemoteRegistryRunning = Running("RemoteRegistry"),
            TelnetRunning = Running("TlntSvr"),
        };

        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Terminal Server"))
        {
            if (key?.GetValue("fDenyTSConnections") is int deny) exposure.RdpEnabled = deny == 0;
        }

        // SMBv1: bật nếu tham số SMB1=1 ở máy chủ, hoặc driver máy khách mrxsmb10 còn được cài và không bị tắt.
        using (var server = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters"))
        using (var client = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\mrxsmb10"))
        {
            var serverOn = server?.GetValue("SMB1") is int s1 && s1 == 1;
            var clientOn = client is not null && (client.GetValue("Start") is not int start || start != 4);
            exposure.Smb1Enabled = serverOn || clientOn;
        }

        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon"))
        {
            var auto = key?.GetValue("AutoAdminLogon") as string;
            var password = key?.GetValue("DefaultPassword") as string;
            exposure.AutoLogonWithPassword = auto == "1" && !string.IsNullOrEmpty(password);
        }

        using (var policy = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System"))
        using (var explorer = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"))
        {
            if (policy?.GetValue("EnableSmartScreen") is int p) exposure.SmartScreenOff = p == 0;
            else if (explorer?.GetValue("SmartScreenEnabled") is string value) exposure.SmartScreenOff = value.Equals("Off", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            var drive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            var volume = Wmi.First($"SELECT ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter='{drive}'", @"root\CIMV2\Security\MicrosoftVolumeEncryption", 10);
            if (volume?.Int("ProtectionStatus") is { } status and (0 or 1)) exposure.BitLockerOn = status == 1;
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc BitLocker lỗi", ex);
        }

        return exposure;
    }

    private static ManagementInfo ReadManagement()
    {
        var info = new ManagementInfo();
        var system = Wmi.First("SELECT PartOfDomain, Domain FROM Win32_ComputerSystem", Wmi.Cimv2, 10);
        if (system is not null)
        {
            info.DomainJoined = system.Bool("PartOfDomain");
            info.Domain = system.Str("Domain");
        }

        // Đăng ký MDM (Intune...) với tài khoản tổ chức: tổ chức có thể cài phần mềm / chính sách / giám sát máy.
        using var enrollments = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
        foreach (var name in enrollments?.GetSubKeyNames() ?? [])
        {
            using var sub = enrollments!.OpenSubKey(name);
            var upn = sub?.GetValue("UPN") as string;
            var provider = sub?.GetValue("ProviderID") as string;
            if (!string.IsNullOrWhiteSpace(upn) && !string.IsNullOrWhiteSpace(provider))
            {
                info.MdmEnrolled = true;
                info.MdmAccount = upn;
                break;
            }
        }

        return info;
    }

    // ===== Camera / micro / vị trí =====

    /// <summary>
    /// Nhật ký quyền riêng tư của Windows (CapabilityAccessManager): ứng dụng nào đã dùng camera / micro / vị trí và khi nào.
    /// Ứng dụng Store nằm trực tiếp dưới khóa; ứng dụng thường nằm dưới "NonPackaged" với đường dẫn dùng '#' thay '\'.
    /// </summary>
    private static List<DeviceAccessEntry> ReadDeviceAccess()
    {
        var list = new List<DeviceAccessEntry>();
        const string root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
        var cutoff = DateTime.Now.AddDays(-30);
        foreach (var capability in new[] { "webcam", "microphone", "location" })
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{root}\{capability}");
            if (key is null) continue;
            foreach (var name in key.GetSubKeyNames())
            {
                if (name == "NonPackaged")
                {
                    using var nonPackaged = key.OpenSubKey(name);
                    foreach (var app in nonPackaged?.GetSubKeyNames() ?? [])
                    {
                        using var sub = nonPackaged!.OpenSubKey(app);
                        AddAccess(list, capability, app.Replace('#', '\\'), false, sub, cutoff);
                    }
                }
                else
                {
                    using var sub = key.OpenSubKey(name);
                    AddAccess(list, capability, name, true, sub, cutoff);
                }
            }
        }

        foreach (var entry in list.Where(e => !e.Packaged))
            entry.File = TrustChecker.Check(entry.App);
        return list.OrderByDescending(e => e.InUse).ThenByDescending(e => e.LastStart).ToList();
    }

    private static void AddAccess(List<DeviceAccessEntry> list, string capability, string app, bool packaged, RegistryKey? key, DateTime cutoff)
    {
        if (key is null) return;
        var start = FileTime(key.GetValue("LastUsedTimeStart"));
        var stop = FileTime(key.GetValue("LastUsedTimeStop"));
        if (start is null || start < cutoff && stop is not null) return;
        list.Add(new DeviceAccessEntry { Capability = capability, App = app, Packaged = packaged, LastStart = start, LastStop = stop });
    }

    private static DateTime? FileTime(object? value)
    {
        var ticks = value switch
        {
            long l => l,
            byte[] { Length: 8 } b => BitConverter.ToInt64(b),
            _ => 0L,
        };
        if (ticks <= 0) return null;
        try
        {
            return DateTime.FromFileTimeUtc(ticks).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
