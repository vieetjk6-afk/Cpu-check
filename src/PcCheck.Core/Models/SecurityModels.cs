using PcCheck.Core.Localization;

namespace PcCheck.Core.Models;

/// <summary>Vị trí tệp – dùng để đánh giá rủi ro (chương trình lạ thường nằm trong thư mục người dùng / Temp).</summary>
public enum PathKind
{
    Unknown,
    Windows,
    ProgramFiles,
    ProgramData,
    UserProfile,
    Temp,
    Other,
}

/// <summary>Thông tin tin cậy của một tệp thực thi: có tồn tại, có chữ ký số hợp lệ, ai ký, nằm ở đâu.</summary>
public sealed record FileTrust(string Path, bool Exists, bool? Signed, string Signer, string Company, PathKind Kind)
{
    /// <summary>Thư mục mà tiến trình thường của người dùng ghi được (AppData, Temp, ProgramData, Public...).</summary>
    public bool UserWritable => Kind is PathKind.UserProfile or PathKind.Temp or PathKind.ProgramData;

    public string Publisher => Signer.Length > 0 ? Signer : Company;
}

public enum SecurityToolKind
{
    /// <summary>Phần mềm điều khiển từ xa (TeamViewer, AnyDesk, UltraViewer...) – hợp pháp nhưng có thể bị lợi dụng.</summary>
    RemoteAccess,

    /// <summary>Phần mềm giám sát nhân viên / quản lý con cái – có mục đích hợp pháp nhưng ghi lại hoạt động.</summary>
    Monitoring,

    /// <summary>Phần mềm gián điệp / keylogger.</summary>
    Spyware,
}

[Flags]
public enum CommandFlags
{
    None = 0,

    /// <summary>Chạy qua trình thông dịch (PowerShell, cmd, wscript, mshta...).</summary>
    ScriptHost = 1,

    /// <summary>PowerShell -EncodedCommand (lệnh bị mã hóa base64 để che giấu).</summary>
    EncodedCommand = 2,

    /// <summary>Ẩn cửa sổ (-WindowStyle Hidden).</summary>
    HiddenWindow = 4,

    /// <summary>Tải nội dung từ Internet khi chạy.</summary>
    Downloads = 8,

    /// <summary>Công cụ hệ thống hay bị lợi dụng (rundll32, regsvr32, mshta, certutil...).</summary>
    LolBin = 16,
}

public enum AutostartSource
{
    RunKey,
    RunOnce,
    StartupFolder,
    ScheduledTask,
    Service,
}

/// <summary>Một mục tự khởi động (khóa Run, thư mục Startup, tác vụ hẹn giờ, dịch vụ) không thuộc Windows.</summary>
public sealed class AutostartEntry
{
    public AutostartSource Source { get; set; }
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";

    /// <summary>Đường dẫn khóa registry / tác vụ / tên dịch vụ – để người dùng tự tìm.</summary>
    public string Location { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Tác vụ hẹn giờ được đánh dấu ẩn.</summary>
    public bool Hidden { get; set; }

    /// <summary>Id StartupItem (khóa Run / thư mục Startup) để bật/tắt; tên tác vụ / dịch vụ với các nguồn khác.</summary>
    public string ActionId { get; set; } = "";

    public CommandFlags Flags { get; set; }
    public FileTrust? Target { get; set; }
}

public sealed class ProcessEntry
{
    public int Pid { get; set; }
    public string Name { get; set; } = "";
    public FileTrust? File { get; set; }
}

public sealed class ServiceEntry
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PathName { get; set; } = "";
    public string StartMode { get; set; } = "";
    public string State { get; set; } = "";
    public int ProcessId { get; set; }
    public FileTrust? File { get; set; }

    public bool Running => State.Equals("Running", StringComparison.OrdinalIgnoreCase);
    public bool AutoStart => StartMode.StartsWith("Auto", StringComparison.OrdinalIgnoreCase);
}

public sealed record ConnectionEntry(int Pid, string LocalAddress, int LocalPort, string RemoteAddress, int RemotePort, bool Listening, bool IPv6);

public sealed record HostsEntry(string Address, string Host, int Line);

public sealed class ProxyInfo
{
    public bool Enabled { get; set; }
    public string Server { get; set; } = "";
    public string AutoConfigUrl { get; set; } = "";
}

public sealed record CertificateEntry(string Subject, string Issuer, string Thumbprint, DateTime NotAfter, string Store);

public sealed class BrowserExtension
{
    public string Browser { get; set; } = "";
    public string Profile { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public bool ForceInstalled { get; set; }
}

public sealed record BrowserPolicy(string Browser, string Name, string Value, string Scope);

public sealed class ManagementInfo
{
    public bool? DomainJoined { get; set; }
    public string Domain { get; set; } = "";
    public bool MdmEnrolled { get; set; }
    public string MdmAccount { get; set; } = "";
}

public enum HijackKind
{
    /// <summary>Image File Execution Options "Debugger": chạy chương trình khác thay cho chương trình gốc.</summary>
    IfeoDebugger,
    WinlogonShell,
    WinlogonUserinit,
    AppInitDlls,
}

public sealed record HijackEntry(HijackKind Kind, string Target, string Value, string Location);

public sealed class RemoteExposure
{
    public bool? RdpEnabled { get; set; }
    public bool? WinRmRunning { get; set; }
    public bool? SshServerRunning { get; set; }
    public bool? RemoteRegistryRunning { get; set; }
    public bool? TelnetRunning { get; set; }
    public bool? Smb1Enabled { get; set; }
    public bool AutoLogonWithPassword { get; set; }
    public bool? SmartScreenOff { get; set; }

    /// <summary>Ổ hệ thống đã bật BitLocker; null = không có BitLocker (bản Home) hoặc không đọc được.</summary>
    public bool? BitLockerOn { get; set; }
}

public sealed class LocalAccount
{
    public string Name { get; set; } = "";
    public string Sid { get; set; } = "";
    public bool Enabled { get; set; }
    public bool IsAdmin { get; set; }

    /// <summary>Bị ẩn khỏi màn hình đăng nhập (SpecialAccounts\UserList) – thủ thuật tạo "cửa hậu".</summary>
    public bool HiddenFromLogon { get; set; }

    /// <summary>RID của SID: 500 Administrator, 501 Guest, 503 DefaultAccount, 504 WDAGUtilityAccount.</summary>
    public int Rid => int.TryParse(Sid[(Sid.LastIndexOf('-') + 1)..], out var rid) ? rid : 0;
}

/// <summary>Ứng dụng đã dùng camera / micro / vị trí (theo nhật ký quyền riêng tư của Windows).</summary>
public sealed class DeviceAccessEntry
{
    /// <summary>"webcam", "microphone" hoặc "location".</summary>
    public string Capability { get; set; } = "";

    public string App { get; set; } = "";
    public bool Packaged { get; set; }
    public DateTime? LastStart { get; set; }
    public DateTime? LastStop { get; set; }
    public FileTrust? File { get; set; }

    /// <summary>Đang dùng: đã bắt đầu nhưng chưa kết thúc.</summary>
    public bool InUse => LastStart is not null && LastStop is null;
}

/// <param name="Allowed">Người dùng đã chọn "Allow on device" – mối đe dọa được để lại trên máy.</param>
public sealed record ThreatEntry(string Name, int SeverityId, DateTime? Time, IReadOnlyList<string> Resources, bool Active, bool Allowed = false);

public sealed class DefenderInfo
{
    public bool Available { get; set; }
    public bool? AntivirusEnabled { get; set; }
    public bool? RealTimeEnabled { get; set; }
    public bool? TamperProtected { get; set; }
    public int? SignatureAgeDays { get; set; }
    public int? QuickScanAgeDays { get; set; }

    /// <summary>"Normal", "Passive" (khi có phần mềm diệt virus khác)...</summary>
    public string RunningMode { get; set; } = "";

    public bool ExclusionsReadable { get; set; }
    public List<string> ExclusionPaths { get; set; } = [];
    public List<string> ExclusionProcesses { get; set; } = [];
    public List<string> ExclusionExtensions { get; set; } = [];
    public List<ThreatEntry> Threats { get; set; } = [];

    public bool IsActiveAntivirus => Available && !RunningMode.Contains("Passive", StringComparison.OrdinalIgnoreCase) && AntivirusEnabled != false;
}

/// <summary>Dữ liệu thô của phần kiểm tra bảo mật / phát hiện phần mềm theo dõi.</summary>
public sealed class SecurityData
{
    public bool Collected { get; set; }
    public DefenderInfo Defender { get; set; } = new();
    public List<ProcessEntry> Processes { get; set; } = [];
    public List<ServiceEntry> Services { get; set; } = [];
    public List<AutostartEntry> Autostart { get; set; } = [];
    public List<ConnectionEntry> Connections { get; set; } = [];
    public List<HostsEntry> Hosts { get; set; } = [];
    public ProxyInfo Proxy { get; set; } = new();
    public List<CertificateEntry> AddedRootCertificates { get; set; } = [];
    public List<BrowserExtension> Extensions { get; set; } = [];
    public List<BrowserPolicy> BrowserPolicies { get; set; } = [];
    public ManagementInfo Management { get; set; } = new();
    public List<HijackEntry> Hijacks { get; set; } = [];
    public RemoteExposure Exposure { get; set; } = new();
    public List<LocalAccount> Accounts { get; set; } = [];
    public List<DeviceAccessEntry> DeviceAccess { get; set; } = [];
}

// ===== Kết quả phân tích (hiển thị ở trang Bảo mật) =====

public enum SecuritySectionKind
{
    Tools,
    DeviceAccess,
    Autostart,
    Processes,
    Network,
    Browser,
    Certificates,
    Defender,
    Access,
    System,
}

public enum SecurityActionKind
{
    OpenLocation,
    VirusTotal,
    Uninstall,
    DisableStartup,
    DisableTask,
    OpenUri,
    OpenFile,
}

public sealed record SecurityAction(SecurityActionKind Kind, string Arg, LocText Label);

public sealed class SecurityItem
{
    public required string Id { get; init; }
    public required SecuritySectionKind Section { get; init; }
    public required Severity Severity { get; init; }
    public required LocText Title { get; init; }
    public LocText? Detail { get; init; }

    /// <summary>Đường dẫn tệp / lệnh / địa chỉ để người dùng đối chiếu.</summary>
    public string Evidence { get; init; } = "";

    public IReadOnlyList<LocText> Tags { get; init; } = [];
    public IReadOnlyList<SecurityAction> Actions { get; init; } = [];

    /// <summary>Khóa hướng dẫn khắc phục (guide.&lt;khóa&gt;.*).</summary>
    public string? GuideKey { get; init; }

    /// <summary>Không tạo vấn đề trong danh sách lỗi (đã có ở phần khác, vd diệt virus tắt).</summary>
    public bool NoIssue { get; init; }
}

public sealed class SecurityReport
{
    public static readonly SecurityReport Empty = new();

    public bool Collected { get; init; }
    public IReadOnlyList<SecurityItem> Items { get; init; } = [];

    public int Count(Severity severity) => Items.Count(i => i.Severity == severity);

    public IEnumerable<SecurityItem> Of(SecuritySectionKind section) => Items.Where(i => i.Section == section);
}
