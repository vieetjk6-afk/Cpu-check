using System.Globalization;
using System.Xml.Linq;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Tests;

public sealed class WidgetTests
{
    [Fact]
    public void SettingsRoundTripKeepsNaNPositionAndMetrics()
    {
        var settings = new AppSettings();
        settings.Widget.Visible = true;
        settings.Widget.Metrics = ["ram", "net"];
        var json = settings.ToJson();

        var loaded = AppSettings.FromJson(json);
        Assert.True(loaded.Widget.Visible);
        Assert.True(double.IsNaN(loaded.Widget.Left));
        Assert.Equal(["ram", "net"], loaded.Widget.Metrics);
    }

    [Fact]
    public void OldSettingsFileWithoutWidgetGetsDefaults()
    {
        var loaded = AppSettings.FromJson("""{"Language":"vi","Theme":"light"}""");
        Assert.Equal("light", loaded.Theme);
        Assert.Equal(WidgetSettings.DefaultMetrics, loaded.Widget.Metrics);
        Assert.Equal(2, loaded.LiveIntervalSeconds);
    }

    [Fact]
    public void UnknownMetricsAreDropped()
    {
        var loaded = AppSettings.FromJson("""{"Widget":{"Metrics":["cpu","bogus","cpu"]}}""");
        Assert.Equal(["cpu"], loaded.Widget.Metrics);
    }

    [Fact]
    public void SnapshotExposesWidgetReadings()
    {
        var s = new SensorSnapshot
        {
            Readings =
            [
                new("1", "CPU", SensorHardwareKind.Cpu, "CPU Core #1", SensorKind.Clock, 4200, null, null),
                new("2", "CPU", SensorHardwareKind.Cpu, "CPU Core #2", SensorKind.Clock, 4700, null, null),
                new("3", "CPU", SensorHardwareKind.Cpu, "Bus Speed", SensorKind.Clock, 100, null, null),
                new("4", "CPU", SensorHardwareKind.Cpu, "CPU Package", SensorKind.Power, 35.5f, null, null),
                new("5", "GPU", SensorHardwareKind.Gpu, "GPU Memory Used", SensorKind.SmallData, 2048, null, null),
                new("6", "GPU", SensorHardwareKind.Gpu, "GPU Memory Total", SensorKind.SmallData, 8192, null, null),
                new("7", "SSD", SensorHardwareKind.Storage, "Total Activity", SensorKind.Load, 12, null, null),
                new("8", "SSD", SensorHardwareKind.Storage, "Read Rate", SensorKind.Throughput, 1000, null, null),
                new("9", "HDD", SensorHardwareKind.Storage, "Read Rate", SensorKind.Throughput, 500, null, null),
                new("10", "HDD", SensorHardwareKind.Storage, "Total Activity", SensorKind.Load, 40, null, null),
                new("11", "Board", SensorHardwareKind.Motherboard, "Fan #1", SensorKind.Fan, 900, null, null),
                new("12", "Board", SensorHardwareKind.Motherboard, "Fan #2", SensorKind.Fan, 0, null, null),
                new("13", "Battery", SensorHardwareKind.Battery, "Charge Level", SensorKind.Level, 77, null, null),
            ],
        };

        Assert.Equal(4700f, s.CpuClock);
        Assert.Equal(35.5f, s.CpuPower);
        Assert.Equal(2048f, s.GpuMemoryUsed);
        Assert.Equal(8192f, s.GpuMemoryTotal);
        Assert.Equal(40f, s.DiskActivity);
        Assert.Equal(1500f, s.DiskReadRate);
        Assert.Null(s.DiskWriteRate);
        Assert.Equal(900f, s.MaxFanRpm);
        Assert.Equal(1, s.FanCount);
        Assert.Equal(77f, s.BatteryLevel);
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0d, "0 B/s")]
    [InlineData(1536d, "1.5 KB/s")]
    [InlineData(12_582_912d, "12 MB/s")]
    public void SpeedFormatting(double? bps, string expected)
    {
        Assert.Equal(expected, Format.Speed(bps, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AutoStartTaskXmlIsValidAndEscaped()
    {
        var xml = AutoStart.BuildTaskXml(@"C:\Apps & Tools\PcCheck.exe", @"PC\Khoa");
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        Assert.Equal(@"C:\Apps & Tools\PcCheck.exe", doc.Descendants(ns + "Command").Single().Value);
        Assert.Equal(AutoStart.WidgetArgument, doc.Descendants(ns + "Arguments").Single().Value);
        Assert.Equal("HighestAvailable", doc.Descendants(ns + "RunLevel").Single().Value);
        Assert.Equal("false", doc.Descendants(ns + "DisallowStartIfOnBatteries").Single().Value);
        Assert.All(doc.Descendants(ns + "UserId"), u => Assert.Equal(@"PC\Khoa", u.Value));
    }
}
