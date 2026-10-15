using PcCheck.Core.Localization;

namespace PcCheck.Core.Models;

public sealed class JunkCategory
{
    public required string Key { get; init; }
    public required LocText Name { get; init; }
    public required LocText Description { get; init; }
    public bool DefaultSelected { get; init; }
    public long Bytes { get; set; }
    public int Files { get; set; }

    /// <summary>Đường dẫn gốc đã quét (để hiển thị/kiểm tra).</summary>
    public List<string> Roots { get; init; } = [];
}

public sealed record CleanResult(long FreedBytes, int DeletedFiles, int SkippedFiles);

public enum ProgramScope
{
    Machine,
    User,
    Store,
}

public sealed class InstalledProgram
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime? InstallDate { get; set; }
    public long? SizeBytes { get; set; }
    public string UninstallCommand { get; set; } = "";
    public string QuietUninstallCommand { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string IconPath { get; set; } = "";
    public ProgramScope Scope { get; set; }

    /// <summary>Tên gói Store (dùng để gỡ ứng dụng Store).</summary>
    public string PackageName { get; set; } = "";

    public DateTime? LastUsed { get; set; }
    public int? RunCount { get; set; }

    /// <summary>Phần mềm hệ thống / runtime / driver: không bao giờ gợi ý gỡ.</summary>
    public bool IsEssential { get; set; }

    /// <summary>Khóa lý do nếu là phần mềm rác / có thể không mong muốn (null = bình thường).</summary>
    public string? BloatReasonKey { get; set; }

    public int? DaysSinceUsed(DateTime now) => LastUsed is null ? null : (int)(now - LastUsed.Value).TotalDays;
}

public enum StartupLocation
{
    UserRun,
    MachineRun,
    MachineRun32,
    UserStartupFolder,
    CommonStartupFolder,
}

public sealed class StartupItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public StartupLocation Location { get; set; }
    public bool Enabled { get; set; } = true;
    public string Publisher { get; set; } = "";
}
