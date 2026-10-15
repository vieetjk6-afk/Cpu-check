using PcCheck.Core.Analysis;
using PcCheck.Core.Models;

namespace PcCheck.Core.Localization;

/// <summary>Nhãn hiển thị cho các enum, dùng chung giữa giao diện và báo cáo.</summary>
public static class Labels
{
    public static string Severity(Severity s) => Loc.T(s switch
    {
        Models.Severity.Critical => "severity.critical",
        Models.Severity.Warning => "severity.warning",
        Models.Severity.Info => "severity.info",
        _ => "severity.ok",
    });

    public static string Check(CheckStatus s) => Loc.T(s switch
    {
        CheckStatus.Pass => "check.pass",
        CheckStatus.Info => "check.info",
        CheckStatus.Warning => "check.warning",
        CheckStatus.Fail => "check.fail",
        _ => "check.unknown",
    });

    public static string Grade(HealthGrade g) => Loc.T(g switch
    {
        HealthGrade.Excellent => "grade.excellent",
        HealthGrade.Good => "grade.good",
        HealthGrade.Fair => "grade.fair",
        _ => "grade.poor",
    });

    public static string Category(IssueCategory c) => Loc.T(c switch
    {
        IssueCategory.Hardware => "category.hardware",
        IssueCategory.Temperature => "category.temperature",
        IssueCategory.Storage => "category.storage",
        IssueCategory.Compatibility => "category.compatibility",
        IssueCategory.Driver => "category.driver",
        IssueCategory.Junk => "category.junk",
        IssueCategory.Software => "category.software",
        IssueCategory.Security => "category.security",
        _ => "category.system",
    });

    public static string DiskHealth(DiskHealth h) => Loc.T(h switch
    {
        Models.DiskHealth.Healthy => "disk.health.healthy",
        Models.DiskHealth.Warning => "disk.health.warning",
        Models.DiskHealth.Unhealthy => "disk.health.unhealthy",
        _ => "disk.health.unknown",
    });

    public static string MediaType(DiskInfo d) =>
        d.IsNvme ? "NVMe SSD"
        : d.MediaType == DiskMediaType.Ssd ? "SSD"
        : d.MediaType == DiskMediaType.Hdd ? "HDD"
        : Loc.T("common.unknown");

    public static string StartupLocation(StartupLocation l) => Loc.T(l switch
    {
        Models.StartupLocation.UserRun => "startup.loc.user_run",
        Models.StartupLocation.MachineRun or Models.StartupLocation.MachineRun32 => "startup.loc.machine_run",
        Models.StartupLocation.UserStartupFolder => "startup.loc.user_folder",
        _ => "startup.loc.common_folder",
    });

    public static string DriverProblem(DriverProblemKind k) => Loc.T(k switch
    {
        DriverProblemKind.Missing => "driver.kind.missing",
        DriverProblemKind.DeviceError => "driver.kind.error",
        DriverProblemKind.Generic => "driver.kind.generic",
        DriverProblemKind.Outdated => "driver.kind.outdated",
        DriverProblemKind.Unsigned => "driver.kind.unsigned",
        _ => "driver.kind.disabled",
    });

    public static string YesNo(bool? value) => value switch
    {
        true => Loc.T("common.yes"),
        false => Loc.T("common.no"),
        null => "—",
    };
}
