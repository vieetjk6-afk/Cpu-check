using System.Globalization;
using PcCheck.Core.Analysis;
using PcCheck.Core.Collectors;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Reporting;
using PcCheck.Core.Util;

namespace PcCheck.Core.Tests;

public sealed class UtilityTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10L * 1024 * 1024 * 1024, "10 GB")]
    [InlineData(1_500_000_000_000, "1.36 TB")]
    public void BytesFormatting(long bytes, string expected)
    {
        Assert.Equal(expected, Format.Bytes(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BytesFormattingFollowsVietnameseCulture()
    {
        Assert.Equal("1,5 KB", Format.Bytes(1536, CultureInfo.GetCultureInfo("vi-VN")));
    }

    [Theory]
    [InlineData(512_110_190_592, "512 GB")]
    [InlineData(1_000_204_886_016, "1 TB")]
    public void MarketingSize(long bytes, string expected)
    {
        Assert.Equal(expected, Format.MarketingSize(bytes, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("0x0000009f (0xffffcd8a, 0x2, 0x0, 0x0)", "0x0000009F")]
    [InlineData("0x1000007e", "0x1000007E")]
    [InlineData("", null)]
    public void NormalizeBugCheck(string raw, string? expected)
    {
        Assert.Equal(expected, EventLogCollector.NormalizeBugCheck(raw));
    }

    [Fact]
    public void BugCheckLookupHandlesVariants()
    {
        Assert.Equal(("DRIVER_POWER_STATE_FAILURE", BugChecks.HintDriver), BugChecks.Lookup("0x0000009F"));
        Assert.Equal(("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", BugChecks.HintDriver), BugChecks.Lookup("0x1000007E"));
        Assert.Null(BugChecks.Lookup("0xDEADBEEF"));
        Assert.Null(BugChecks.Lookup(null));
    }

    [Theory]
    [InlineData(397568, true, true)]    // Defender bật, cập nhật (0x061100)
    [InlineData(393472, false, true)]   // Defender tắt (0x060100)
    [InlineData(266240, true, true)]    // AV bên thứ ba bật (0x041000)
    [InlineData(266256, true, false)]   // bật nhưng lỗi thời (0x041010)
    public void DecodeSecurityCenterProductState(long state, bool enabled, bool upToDate)
    {
        Assert.Equal((enabled, upToDate), SystemStatusCollector.DecodeProductState(state));
    }

    [Theory]
    [InlineData("9/12/2026", 2026, 9, 12)]
    [InlineData("20260105", 2026, 1, 5)]
    public void ParseQfeDate(string raw, int y, int m, int d)
    {
        Assert.Equal(new DateTime(y, m, d), SystemStatusCollector.ParseQfeDate(raw));
    }

    [Fact]
    public void Rot13DecodesUserAssistNames()
    {
        Assert.Equal(@"C:\Program Files\App\app.exe", SoftwareCollector.Rot13(@"P:\Cebtenz Svyrf\Ncc\ncc.rkr"));
    }

    [Theory]
    [InlineData(0x02, true)]
    [InlineData(0x06, true)]
    [InlineData(0x03, false)]
    [InlineData(0x07, false)]
    public void StartupApprovedFlag(byte flag, bool enabled)
    {
        Assert.Equal(enabled, StartupCollector.IsEnabledFlag(flag));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Steam\\steam.exe\" -silent", @"C:\Program Files\Steam\steam.exe")]
    [InlineData(@"C:\Tools\app.exe /background", @"C:\Tools\app.exe")]
    public void ExtractExecutable(string command, string expected)
    {
        Assert.Equal(expected, StartupCollector.ExtractExecutable(command));
    }

    [Fact]
    public void UninstallCommandMsiRepairBecomesRemove()
    {
        var (exe, args) = FixService.SplitCommand("MsiExec.exe /I{12345678-1234-1234-1234-123456789012}");
        Assert.Equal("MsiExec.exe", exe);
        Assert.Equal("/X{12345678-1234-1234-1234-123456789012}", args);

        var (exe2, args2) = FixService.SplitCommand("\"C:\\Program Files\\App\\unins000.exe\" /SILENT");
        Assert.Equal(@"C:\Program Files\App\unins000.exe", exe2);
        Assert.Equal("/SILENT", args2);
    }

    [Theory]
    [InlineData("80CE80CE80CE", "Samsung")]
    [InlineData("Kingston", "Kingston")]
    [InlineData("859B", "Crucial")]
    public void RamVendorCodes(string raw, string expected)
    {
        Assert.Equal(expected, HardwareCollector.RamVendor(raw));
    }

    [Fact]
    public void CpuNameIsCleaned()
    {
        Assert.Equal("Intel Core i7-9700K @ 3.60GHz", HardwareCollector.CleanCpuName("Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz"));
    }

    [Fact]
    public void WindowsLifecycle()
    {
        Assert.Equal(new DateTime(2025, 10, 14), Analysis.WindowsLifecycle.EndOfSupport(19045, "Professional"));
        Assert.Equal(new DateTime(2026, 11, 10), Analysis.WindowsLifecycle.EndOfSupport(22631, "Enterprise"));
        Assert.Null(Analysis.WindowsLifecycle.EndOfSupport(19044, "EnterpriseS"));
        Assert.Null(Analysis.WindowsLifecycle.EndOfSupport(99999, "Professional"));
    }

    [Fact]
    public void SoftwareCatalog()
    {
        Assert.Equal(Analysis.SoftwareCatalog.ReasonOptimizer, Analysis.SoftwareCatalog.BloatReason("Driver Booster 12", "IObit"));
        Assert.Equal(Analysis.SoftwareCatalog.ReasonCrack, Analysis.SoftwareCatalog.BloatReason("KMSpico", ""));
        Assert.True(Analysis.SoftwareCatalog.IsEssential("Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft"));
        Assert.False(Analysis.SoftwareCatalog.IsEssential("Driver Booster 12", "IObit"));
        Assert.False(Analysis.SoftwareCatalog.IsEssential("Zalo", "VNG"));
        Assert.Equal("Microsoft News", Analysis.SoftwareCatalog.StoreBloatName("Microsoft.BingNews"));
    }

    [Fact]
    public void UnusedProgramRules()
    {
        var now = TestData.Now;
        Assert.True(CleanupAnalyzer.IsUnused(new InstalledProgram { Name = "Game", LastUsed = now.AddDays(-200) }, now));
        Assert.False(CleanupAnalyzer.IsUnused(new InstalledProgram { Name = "Game", LastUsed = now.AddDays(-10) }, now));
        Assert.False(CleanupAnalyzer.IsUnused(new InstalledProgram { Name = "Game" }, now));
        Assert.False(CleanupAnalyzer.IsUnused(new InstalledProgram { Name = "Runtime", LastUsed = now.AddDays(-200), IsEssential = true }, now));
        Assert.False(CleanupAnalyzer.IsUnused(new InstalledProgram { Name = "New", LastUsed = now.AddDays(-200), InstallDate = now.AddDays(-30) }, now));
    }

    [Fact]
    public void SensorSnapshotPicksPreferredSensors()
    {
        var snapshot = new SensorSnapshot
        {
            Readings =
            [
                new SensorReading("1", "CPU", SensorHardwareKind.Cpu, "Core #1", SensorKind.Temperature, 50, null, null),
                new SensorReading("2", "CPU", SensorHardwareKind.Cpu, "CPU Package", SensorKind.Temperature, 61, null, null),
                new SensorReading("3", "SSD", SensorHardwareKind.Storage, "Temperature", SensorKind.Temperature, 44, null, null),
                new SensorReading("4", "HDD", SensorHardwareKind.Storage, "Temperature", SensorKind.Temperature, 38, null, null),
            ],
        };

        Assert.Equal(61f, snapshot.CpuTemperature);
        Assert.Equal(44f, snapshot.MaxStorageTemperature);
        Assert.Null(snapshot.GpuTemperature);
    }

    [Fact]
    public void ReportIsSelfContainedAndEncoded()
    {
        Loc.Instance.SetLanguage(Loc.English);
        var data = TestData.HealthyPc();
        data.Hardware.Os.MachineName = "<script>alert(1)</script>";
        var result = Analysis.Analyzer.Analyze(data, TestData.Now);
        var html = ReportBuilder.BuildHtml(result, "1.0.0");

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("PC check report", html, StringComparison.Ordinal);
        Assert.Contains("Samsung SSD 980 1TB", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Loc.Instance.SetLanguage(Loc.Vietnamese);
    }

    [Fact]
    public void WmiRowConversions()
    {
        var row = new WmiRow { ["A"] = (uint)5, ["B"] = "12", ["C"] = true, ["D"] = null, ["E"] = new[] { "x", "y" }, ["F"] = ulong.MaxValue };
        Assert.Equal(5, row.Int("a"));
        Assert.Equal(12L, row.Long("B"));
        Assert.True(row.Bool("C"));
        Assert.Null(row.Int("D"));
        Assert.Equal("", row.Str("missing"));
        Assert.Equal(["x", "y"], row.StrArray("E"));
        Assert.Equal(long.MaxValue, row.Long("F"));
    }
}
