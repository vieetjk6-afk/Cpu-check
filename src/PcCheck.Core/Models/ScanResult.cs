namespace PcCheck.Core.Models;

/// <summary>Toàn bộ dữ liệu thô thu thập được trong một lần quét.</summary>
public sealed class ScanData
{
    public HardwareInfo Hardware { get; set; } = new();
    public SensorSnapshot Sensors { get; set; } = SensorSnapshot.Empty;
    public List<DeviceInfo> Devices { get; set; } = [];
    public EventSummary Events { get; set; } = new();
    public SystemStatus System { get; set; } = new();
    public List<JunkCategory> Junk { get; set; } = [];
    public List<InstalledProgram> Programs { get; set; } = [];
    public List<StartupItem> Startup { get; set; } = [];
    public SecurityData Security { get; set; } = new();
}

public sealed class ScanResult
{
    public required ScanData Data { get; init; }
    public required IReadOnlyList<Issue> Issues { get; init; }
    public required IReadOnlyList<CompatibilityCheck> Checks { get; init; }
    public required IReadOnlyList<DriverProblem> DriverProblems { get; init; }
    public required int Score { get; init; }
    public SecurityReport Security { get; init; } = SecurityReport.Empty;
    public DateTime Time { get; init; } = DateTime.Now;
    public TimeSpan Duration { get; init; }

    public int Count(Severity severity) => Issues.Count(i => i.Severity == severity);
}

public sealed record ScanProgress(int Completed, int Total, string StepKey);
