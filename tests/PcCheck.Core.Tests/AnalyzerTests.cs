using PcCheck.Core.Analysis;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;

namespace PcCheck.Core.Tests;

public sealed class AnalyzerTests
{
    private static ScanResult Analyze(ScanData data) => Analyzer.Analyze(data, TestData.Now);

    private static bool Has(ScanResult r, string id) => r.Issues.Any(i => i.Id == id);

    [Fact]
    public void HealthyPcScoresHighWithoutCriticalIssues()
    {
        var result = Analyze(TestData.HealthyPc());

        Assert.DoesNotContain(result.Issues, i => i.Severity >= Severity.Warning);
        Assert.True(result.Score >= 90, $"Điểm {result.Score}: " + string.Join("; ", result.Issues.Select(i => i.Id)));
        Assert.Equal(HealthGrade.Excellent, HealthScore.Grade(result.Score));
    }

    [Fact]
    public void AllIssueTextsResolveInBothLanguages()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Os.Build = 19045;
        data.Hardware.Os.Caption = "Windows 10 Pro";
        data.Hardware.Disks[0].Health = DiskHealth.Unhealthy;
        data.Hardware.Memory.Modules.RemoveAt(1);
        data.System.PendingReboot = true;
        data.Events.Entries.Add(new EventEntry(EventKind.BlueScreen, TestData.Now.AddDays(-1), 1001, "BugCheck", "0x0000001A"));
        var result = Analyze(data);

        foreach (var language in Loc.SupportedLanguages)
        {
            Loc.Instance.SetLanguage(language);
            foreach (var issue in result.Issues)
            {
                foreach (var text in new[] { issue.Title, issue.Detail, issue.Recommendation }.Where(t => t is not null))
                {
                    var s = text!.ToString();
                    Assert.DoesNotContain("issue.", s, StringComparison.Ordinal);
                    Assert.DoesNotContain("{0", s, StringComparison.Ordinal);
                }

                foreach (var fix in issue.Fixes) Assert.NotEqual(fix.Label.Key, fix.Label.ToString());
            }
        }

        Loc.Instance.SetLanguage(Loc.Vietnamese);
    }

    [Fact]
    public void HotCpuIsCritical()
    {
        var data = TestData.HealthyPc();
        data.Sensors = new SensorSnapshot
        {
            Readings = [new SensorReading("a", "CPU", SensorHardwareKind.Cpu, "CPU Package", SensorKind.Temperature, 98, 40, 99)],
        };

        var issue = Assert.Single(Analyze(data).Issues, i => i.Id == "temp/cpu");
        Assert.Equal(Severity.Critical, issue.Severity);
    }

    [Fact]
    public void HotCpuAtIdleIsWarning()
    {
        var data = TestData.HealthyPc();
        data.Sensors = new SensorSnapshot
        {
            Readings =
            [
                new SensorReading("a", "CPU", SensorHardwareKind.Cpu, "Core (Tctl/Tdie)", SensorKind.Temperature, 76, 40, 80),
                new SensorReading("b", "CPU", SensorHardwareKind.Cpu, "CPU Total", SensorKind.Load, 5, 0, 100),
            ],
        };

        Assert.True(Has(Analyze(data), "temp/cpu_idle"));
    }

    [Fact]
    public void MissingPawnIoSuggestsInstall()
    {
        var data = TestData.HealthyPc();
        data.Sensors = new SensorSnapshot { PawnIoInstalled = false, Readings = [] };
        Assert.True(Has(Analyze(data), "temp/pawnio"));
    }

    [Fact]
    public void FailingDiskIsCritical()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Disks[0].Health = DiskHealth.Unhealthy;
        var issue = Assert.Single(Analyze(data).Issues, i => i.Id == "disk/failing/0");
        Assert.Equal(Severity.Critical, issue.Severity);
    }

    [Fact]
    public void WornSsdAndLowSpaceAreReported()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Disks[0].WearPercent = 92;
        data.Hardware.Volumes[0].FreeBytes = 5 * TestData.Gb;
        var result = Analyze(data);

        Assert.Equal(Severity.Critical, result.Issues.Single(i => i.Id == "disk/wear/0").Severity);
        Assert.Equal(Severity.Critical, result.Issues.Single(i => i.Id == "volume/space/C:").Severity);
    }

    [Fact]
    public void BatteryWearUsesThresholds()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Battery = new BatteryInfo { DesignCapacity = 50000, FullChargeCapacity = 27000, CycleCount = 600 };
        var issue = Assert.Single(Analyze(data).Issues, i => i.Id == "battery/wear");
        Assert.Equal(Severity.Warning, issue.Severity);
        Assert.Equal(46, Math.Round(data.Hardware.Battery.WearPercent!.Value));
    }

    [Fact]
    public void BlueScreensAreDetectedWithHint()
    {
        var data = TestData.HealthyPc();
        for (var i = 0; i < 3; i++)
        {
            data.Events.Entries.Add(new EventEntry(EventKind.BlueScreen, TestData.Now.AddDays(-i), 1001, "BugCheck", "0x0000001A"));
        }

        var issue = Assert.Single(Analyze(data).Issues, i => i.Id == "events/bsod");
        Assert.Equal(Severity.Critical, issue.Severity);
        Assert.Equal("bugcheck.hint.ram", issue.Recommendation!.Key);
    }

    [Fact]
    public void PowerLossIgnoresBugcheckCausedShutdowns()
    {
        var data = TestData.HealthyPc();
        data.Events.Entries.Add(new EventEntry(EventKind.UnexpectedShutdown, TestData.Now, 41, "Kernel-Power", "0"));
        data.Events.Entries.Add(new EventEntry(EventKind.UnexpectedShutdown, TestData.Now, 41, "Kernel-Power", "159"));
        Assert.False(Has(Analyze(data), "events/power_loss"));

        data.Events.Entries.Add(new EventEntry(EventKind.UnexpectedShutdown, TestData.Now, 41, "Kernel-Power", "0"));
        Assert.True(Has(Analyze(data), "events/power_loss"));
    }

    [Fact]
    public void Windows10IsOutOfSupport()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Os.Build = 19045;
        data.Hardware.Os.Caption = "Windows 10 Pro";
        var issue = Assert.Single(Analyze(data).Issues, i => i.Id == "system/eos");
        Assert.Equal("issue.win10_eos.title", issue.Title.Key);
    }

    [Fact]
    public void Windows11_24H2HomeIsOutOfSupportAfterOctober2026()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Os.Build = 26100;
        data.Hardware.Os.DisplayVersion = "24H2";
        var issue = Assert.Single(Analyze(data).Issues, i => i.Id == "system/eos");
        Assert.Equal("issue.win_version_eos.title", issue.Title.Key);
    }

    [Fact]
    public void SecurityProblemsAreReported()
    {
        var data = TestData.HealthyPc();
        data.System.Antivirus = [new AntivirusProduct("Windows Defender", false, false)];
        data.System.DefenderRealtime = false;
        data.System.FirewallDisabledProfiles = ["Public"];
        data.System.UacEnabled = false;
        data.System.LicenseStatus = 5;
        data.System.LastUpdateInstalled = TestData.Now.AddDays(-120);
        var result = Analyze(data);

        Assert.Equal(Severity.Critical, result.Issues.Single(i => i.Id == "security/no_av").Severity);
        Assert.True(Has(result, "security/firewall"));
        Assert.True(Has(result, "security/uac"));
        Assert.True(Has(result, "system/activation"));
        Assert.True(Has(result, "system/updates"));
    }

    [Fact]
    public void JunkBloatAndStartupAreReported()
    {
        var data = TestData.HealthyPc();
        data.Junk[0].Bytes = 3 * TestData.Gb;
        data.Programs =
        [
            new InstalledProgram { Name = "Driver Booster 11", BloatReasonKey = SoftwareCatalog.BloatReason("Driver Booster 11", "IObit") },
            new InstalledProgram { Name = "Old Game", LastUsed = TestData.Now.AddDays(-400), InstallDate = TestData.Now.AddDays(-800), SizeBytes = 20 * TestData.Gb },
        ];
        data.Startup = Enumerable.Range(0, 13).Select(i => new StartupItem { Name = "App" + i, Enabled = true }).ToList();
        var result = Analyze(data);

        Assert.Equal(Severity.Warning, result.Issues.Single(i => i.Id == "junk/total").Severity);
        Assert.True(Has(result, "software/bloat"));
        Assert.True(Has(result, "software/unused"));
        Assert.Equal(Severity.Warning, result.Issues.Single(i => i.Id == "startup/count").Severity);
    }

    [Fact]
    public void ScoreIsCappedPerCategory()
    {
        var issues = Enumerable.Range(0, 10).Select(i => new Issue
        {
            Id = "x" + i,
            Category = IssueCategory.Driver,
            Severity = Severity.Critical,
            Title = new LocText("a"),
            Detail = new LocText("b"),
        }).ToList();

        Assert.Equal(70, HealthScore.Compute(issues));
        Assert.Equal(100, HealthScore.Compute([]));
    }

    [Fact]
    public void IssuesAreSortedBySeverity()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Disks[0].Health = DiskHealth.Unhealthy;
        data.System.Uptime = TimeSpan.FromDays(10);
        var result = Analyze(data);
        var severities = result.Issues.Select(i => (int)i.Severity).ToList();
        Assert.Equal(severities.OrderByDescending(s => s), severities);
    }
}
