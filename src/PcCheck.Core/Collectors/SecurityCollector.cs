using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PcCheck.Core.Analysis;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

/// <summary>
/// Thu thập dữ liệu cho phần kiểm tra bảo mật / phát hiện phần mềm theo dõi. Chỉ ĐỌC, không thay đổi hệ thống.
/// Mỗi phần chạy độc lập: một phần lỗi (thiếu quyền, WMI hỏng) không ảnh hưởng phần khác.
/// </summary>
public static partial class SecurityCollector
{
    public static SecurityData Collect()
    {
        var data = new SecurityData { Collected = true };
        var watch = Stopwatch.StartNew();

        Parallel.Invoke(
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            () => Safe("defender", () => data.Defender = ReadDefender()),
            () => Safe("processes", () => data.Processes = ReadProcesses()),
            () => Safe("services", () => data.Services = ReadServices()),
            () => Safe("autostart", () => data.Autostart = ReadAutostart()),
            () => Safe("connections", () => data.Connections = ReadConnections()),
            () => Safe("hosts", () => data.Hosts = ReadHosts()),
            () => Safe("proxy", () => data.Proxy = ReadProxy()),
            () => Safe("certificates", () => data.AddedRootCertificates = ReadAddedRootCertificates()),
            () => Safe("browser", () =>
            {
                data.BrowserPolicies = ReadBrowserPolicies();
                data.Extensions = ReadExtensions(data.BrowserPolicies);
            }),
            () => Safe("management", () => data.Management = ReadManagement()),
            () => Safe("hijacks", () => data.Hijacks = ReadHijacks()),
            () => Safe("accounts", () => data.Accounts = ReadAccounts()),
            () => Safe("device-access", () => data.DeviceAccess = ReadDeviceAccess()));

        // Cần danh sách dịch vụ (đã đọc ở trên) để biết RDP/WinRM/SSH có chạy không.
        Safe("exposure", () => data.Exposure = ReadExposure(data.Services));
        AddServiceAutostart(data);

        Log.Info($"Kiểm tra bảo mật: {watch.ElapsedMilliseconds} ms, {data.Processes.Count} tiến trình, {data.Autostart.Count} mục tự chạy, {data.Connections.Count} kết nối");
        return data;
    }

    private static void Safe(string part, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn($"Bảo mật/{part} lỗi", ex);
        }
    }

    // ===== Microsoft Defender =====

    private static DefenderInfo ReadDefender()
    {
        var info = new DefenderInfo();
        var status = Wmi.First("SELECT AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, IsTamperProtected, AntivirusSignatureAge, QuickScanAge, AMRunningMode FROM MSFT_MpComputerStatus", Wmi.Defender, 15);
        if (status is null) return info;

        info.Available = true;
        info.AntivirusEnabled = status.Bool("AntivirusEnabled");
        info.RealTimeEnabled = status.Bool("RealTimeProtectionEnabled");
        info.TamperProtected = status.Bool("IsTamperProtected");
        info.SignatureAgeDays = Days(status.Long("AntivirusSignatureAge"));
        info.QuickScanAgeDays = Days(status.Long("QuickScanAge"));
        info.RunningMode = status.Str("AMRunningMode");

        var pref = Wmi.First("SELECT ExclusionPath, ExclusionProcess, ExclusionExtension FROM MSFT_MpPreference", Wmi.Defender, 15);
        if (pref is not null)
        {
            // Khi không đủ quyền, Defender trả về chuỗi "N/A: Must be an administrator to view exclusions".
            static List<string> Clean(string[] values) => values.Where(v => !string.IsNullOrWhiteSpace(v) && !v.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)).ToList();
            var paths = pref.StrArray("ExclusionPath");
            info.ExclusionsReadable = !paths.Any(v => v.StartsWith("N/A", StringComparison.OrdinalIgnoreCase));
            info.ExclusionPaths = Clean(paths);
            info.ExclusionProcesses = Clean(pref.StrArray("ExclusionProcess"));
            info.ExclusionExtensions = Clean(pref.StrArray("ExclusionExtension"));
        }

        var names = Wmi.Query("SELECT ThreatID, ThreatName, SeverityID, IsActive FROM MSFT_MpThreat", Wmi.Defender, 15)
            .GroupBy(r => r.Str("ThreatID"))
            .ToDictionary(g => g.Key, g => g.First());
        var cutoff = DateTime.Now.AddDays(-90);
        foreach (var d in Wmi.Query("SELECT ThreatID, InitialDetectionTime, Resources, ThreatStatusID FROM MSFT_MpThreatDetection", Wmi.Defender, 15))
        {
            var time = d.Date("InitialDetectionTime");
            if (time < cutoff) continue;
            names.TryGetValue(d.Str("ThreatID"), out var threat);

            // ThreatStatusID: 0 không rõ, 1 phát hiện, 2 đã làm sạch, 3 cách ly, 4 đã xóa, 5 được cho phép, 6 đã chặn,
            // 102 cách ly lỗi, 103 xóa lỗi, 104 cho phép lỗi, 105 bỏ dở, 107 chặn lỗi.
            var statusId = d.Int("ThreatStatusID") ?? 0;
            var active = threat?.Bool("IsActive") == true || statusId is 1 or 102 or 103 or 104 or 105 or 107;
            var allowed = statusId == 5;
            info.Threats.Add(new ThreatEntry(
                threat?.Str("ThreatName") is { Length: > 0 } n ? n : "Threat " + d.Str("ThreatID"),
                threat?.Int("SeverityID") ?? 0,
                time,
                d.StrArray("Resources").Select(r => r.Contains('_') ? r[(r.IndexOf('_') + 1)..] : r).Take(5).ToList(),
                active,
                allowed));
        }

        info.Threats = info.Threats.OrderByDescending(t => t.Time).Take(30).ToList();
        return info;
    }

    /// <summary>Defender trả 4294967295 (uint.MaxValue) khi chưa quét lần nào.</summary>
    private static int? Days(long? value) => value is null or < 0 or >= 100_000 ? null : (int)value.Value;

    // ===== Tiến trình =====

    private static List<ProcessEntry> ReadProcesses()
    {
        var self = Environment.ProcessId;
        var list = new List<ProcessEntry>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id is 0 or 4 || process.Id == self) continue;
                var path = ImagePath(process.Id);
                list.Add(new ProcessEntry
                {
                    Pid = process.Id,
                    Name = path.Length > 0 ? Path.GetFileName(path) : process.ProcessName + ".exe",
                    File = path.Length > 0 ? new FileTrust(path, true, null, "", "", SecurityCatalog.Classify(path)) : null,
                });
            }
        }

        // Chỉ kiểm tra chữ ký tiến trình ngoài thư mục Windows (ít, đáng chú ý nhất).
        Parallel.ForEach(list.Where(p => p.File is { Kind: not PathKind.Windows }), new ParallelOptions { MaxDegreeOfParallelism = 4 },
            p => p.File = TrustChecker.Check(p.File!.Path));
        return list;
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
#pragma warning restore SYSLIB1054

    /// <summary>Đường dẫn tệp của tiến trình (nhanh, chạy được với cả tiến trình hệ thống được bảo vệ).</summary>
    private static string ImagePath(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return "";
        try
        {
            var size = 1024;
            var sb = new StringBuilder(size);
            return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ===== Dịch vụ =====

    private static List<ServiceEntry> ReadServices()
    {
        var list = new List<ServiceEntry>();
        foreach (var row in Wmi.Query("SELECT Name, DisplayName, PathName, StartMode, State, ProcessId FROM Win32_Service", Wmi.Cimv2, 30))
        {
            var pathName = row.Str("PathName");
            var exe = SecurityCatalog.ExecutableOf(pathName);
            var entry = new ServiceEntry
            {
                Name = row.Str("Name"),
                DisplayName = row.Str("DisplayName"),
                PathName = pathName,
                StartMode = row.Str("StartMode"),
                State = row.Str("State"),
                ProcessId = row.Int("ProcessId") ?? 0,
                File = exe.Length > 0 ? new FileTrust(exe, true, null, "", "", SecurityCatalog.Classify(exe)) : null,
            };
            list.Add(entry);
        }

        Parallel.ForEach(list.Where(s => s.File is { Kind: not PathKind.Windows }), new ParallelOptions { MaxDegreeOfParallelism = 4 },
            s => s.File = TrustChecker.Check(s.File!.Path));
        return list;
    }

    /// <summary>Dịch vụ tự khởi động không thuộc Windows cũng là một dạng tự chạy.</summary>
    private static void AddServiceAutostart(SecurityData data)
    {
        const CommandFlags suspicious = CommandFlags.ScriptHost | CommandFlags.EncodedCommand | CommandFlags.Downloads | CommandFlags.HiddenWindow;
        foreach (var service in data.Services)
        {
            var flags = SecurityCatalog.AnalyzeCommand(service.PathName);

            // Dịch vụ chạy bằng cmd/PowerShell/lệnh mã hóa (kiểu PsExec, Cobalt Strike) – kể cả khi tệp nằm trong thư mục Windows.
            var viaWindowsTool = service.File is { Kind: PathKind.Windows } && (flags & suspicious) != 0
                                 && !service.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            var thirdParty = service.AutoStart && service.File is { Kind: not PathKind.Windows };
            if (!viaWindowsTool && !thirdParty) continue;

            data.Autostart.Add(new AutostartEntry
            {
                Source = AutostartSource.Service,
                Name = service.DisplayName.Length > 0 ? service.DisplayName : service.Name,
                Command = service.PathName,
                Location = service.Name,
                ActionId = service.Name,
                Flags = flags,
                Target = service.File,
            });
        }
    }
}
