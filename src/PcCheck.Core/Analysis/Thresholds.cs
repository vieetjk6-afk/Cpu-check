namespace PcCheck.Core.Analysis;

/// <summary>Ngưỡng cảnh báo dùng chung cho analyzer và giao diện (tô màu).</summary>
public static class Thresholds
{
    public const double CpuTempWarn = 85;
    public const double CpuTempCritical = 95;
    public const double CpuIdleTempWarn = 70;
    public const double GpuTempWarn = 83;
    public const double GpuTempCritical = 92;
    public const double SsdTempWarn = 65;
    public const double SsdTempCritical = 75;
    public const double HddTempWarn = 48;
    public const double HddTempCritical = 55;

    public const double BatteryWearInfo = 25;
    public const double BatteryWearWarn = 40;
    public const int SsdWearWarn = 70;
    public const int SsdWearCritical = 90;

    public const double RamUsageInfo = 80;
    public const double RamUsageWarn = 90;

    public const double SystemFreePercentWarn = 15;
    public const double SystemFreePercentCritical = 8;
    public const long SystemFreeBytesWarn = 20L * 1024 * 1024 * 1024;
    public const long SystemFreeBytesCritical = 8L * 1024 * 1024 * 1024;
    public const double DataFreePercentWarn = 5;

    public const long JunkInfoBytes = 500L * 1024 * 1024;
    public const long JunkWarnBytes = 2L * 1024 * 1024 * 1024;

    public const int UnusedDays = 90;
    public const int StartupInfo = 7;
    public const int StartupWarn = 12;

    public const int UptimeDaysInfo = 7;
    public const int UpdateDaysWarn = 45;
    public const int DefenderSignatureDaysWarn = 7;

    public const int DriverOldYears = 4;
    public const int GpuDriverOldYears = 1;
    public const int BiosOldYears = 5;

    public static Models.Severity TemperatureSeverity(double? value, double warn, double critical) =>
        value is null ? Models.Severity.Ok
        : value >= critical ? Models.Severity.Critical
        : value >= warn ? Models.Severity.Warning
        : Models.Severity.Ok;
}
