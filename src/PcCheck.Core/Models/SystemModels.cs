namespace PcCheck.Core.Models;

public enum EventKind
{
    UnexpectedShutdown,
    BlueScreen,
    HardwareError,
    HardwareErrorFatal,
    DiskError,
    StorageReset,
    FileSystemError,
    GpuDriverCrash,
    LowMemory,
    AppCrash,
    AppHang,
}

public sealed record EventEntry(EventKind Kind, DateTime Time, int EventId, string Provider, string? Detail);

public sealed class EventSummary
{
    public int Days { get; set; } = 30;
    public List<EventEntry> Entries { get; set; } = [];
    public int MinidumpCount { get; set; }
    public DateTime? LastMinidump { get; set; }
    public bool Available { get; set; } = true;

    public int Count(EventKind kind) => Entries.Count(e => e.Kind == kind);

    public IEnumerable<EventEntry> Of(EventKind kind) => Entries.Where(e => e.Kind == kind).OrderByDescending(e => e.Time);
}

public sealed record AntivirusProduct(string Name, bool Enabled, bool UpToDate)
{
    public bool IsDefender => Name.Contains("Defender", StringComparison.OrdinalIgnoreCase);
}

public sealed class SystemStatus
{
    public bool IsAdmin { get; set; }
    public bool PendingReboot { get; set; }
    public List<string> PendingRebootReasons { get; set; } = [];

    public List<AntivirusProduct> Antivirus { get; set; } = [];
    public bool? DefenderRealtime { get; set; }
    public int? DefenderSignatureAgeDays { get; set; }

    public List<string> FirewallDisabledProfiles { get; set; } = [];
    public bool FirewallKnown { get; set; }
    public bool ThirdPartyFirewall { get; set; }

    /// <summary>1 = đã kích hoạt; null = không đọc được.</summary>
    public int? LicenseStatus { get; set; }

    public DateTime? LastUpdateInstalled { get; set; }
    public TimeSpan Uptime { get; set; }
    public bool? FastStartupEnabled { get; set; }
    public string PowerPlan { get; set; } = "";
    public bool? UacEnabled { get; set; }
    public bool? PageFileEnabled { get; set; }
    public bool WindowsOldPresent { get; set; }
}
