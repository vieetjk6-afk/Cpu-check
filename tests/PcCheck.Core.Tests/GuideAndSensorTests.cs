using PcCheck.Core.Analysis;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Sensors;
using PcCheck.Core.Util;

namespace PcCheck.Core.Tests;

public sealed class GuideAndSensorTests
{
    private static readonly Dictionary<string, string> Vi = Loc.Load(Loc.Vietnamese);
    private static readonly Dictionary<string, string> En = Loc.Load(Loc.English);

    private static SensorReading Acpi(string zone, float celsius) => ThermalZoneReader.Make(zone, celsius)!;

    [Fact]
    public void EveryGuideHasCauseAndStepsInBothLanguages()
    {
        foreach (var key in IssueGuides.AllKeys)
        {
            foreach (var dict in new[] { Vi, En })
            {
                Assert.True(dict.ContainsKey($"guide.{key}.cause"), key);
                Assert.True(dict.ContainsKey($"guide.{key}.steps"), key);
            }

            var viSteps = Vi[$"guide.{key}.steps"].Split('\n');
            var enSteps = En[$"guide.{key}.steps"].Split('\n');
            Assert.True(viSteps.Length >= 2, key);
            Assert.Equal(viSteps.Length, enSteps.Length);
        }
    }

    [Fact]
    public void NoOrphanGuideStrings()
    {
        var keys = IssueGuides.AllKeys.ToHashSet();
        var orphans = Vi.Keys
            .Select(k => k.Split('.'))
            .Where(parts => parts.Length == 3 && parts[0] == "guide" && parts[2] is "cause" or "steps")
            .Select(parts => parts[1])
            .Where(k => !keys.Contains(k))
            .Distinct()
            .ToList();
        Assert.Empty(orphans);
    }

    [Theory]
    [InlineData("temp/cpu", "cpu_hot")]
    [InlineData("temp/cpu_idle", "cpu_hot")]
    [InlineData("temp/pawnio", "cpu_sensor")]
    [InlineData("driver/Missing.PCI\\VEN_8086", "driver_missing")]
    [InlineData("disk/failing/0", "disk_failing")]
    [InlineData("compat/ram/channel", "ram_channel")]
    [InlineData("compat/ram/upgrade", "ram_upgrade")]
    [InlineData("events/bsod", "bsod")]
    public void FindMapsIssueIdsToGuides(string id, string expected)
    {
        Assert.Equal(expected, IssueGuides.Find(id)?.Key);
    }

    [Fact]
    public void FindReturnsNullForUnknownIds() => Assert.Null(IssueGuides.Find("unknown/thing"));

    [Fact]
    public void AnalyzedIssuesCarryGuides()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Os.Build = 19045;
        data.Hardware.Os.Caption = "Windows 10 Pro";
        data.Hardware.Disks[0].Health = DiskHealth.Unhealthy;
        data.Hardware.Memory.Modules.RemoveAt(1);
        data.System.PendingReboot = true;
        data.System.UacEnabled = false;
        data.Events.Entries.Add(new EventEntry(EventKind.BlueScreen, TestData.Now.AddDays(-1), 1001, "BugCheck", "0x0000001A"));
        data.Sensors = new SensorSnapshot { PawnIoInstalled = false, Readings = [] };

        var result = Analyzer.Analyze(data, TestData.Now);

        Assert.NotEmpty(result.Issues);
        var missing = result.Issues.Where(i => i.Guide is null).Select(i => i.Id).ToList();
        Assert.True(missing.Count == 0, "Thiếu hướng dẫn: " + string.Join(", ", missing));
        foreach (var issue in result.Issues)
        {
            Assert.NotEqual(issue.Guide!.Steps.Key, issue.Guide.Steps.ToString());
            Assert.NotEqual(issue.Guide.Cause.Key, issue.Guide.Cause.ToString());
        }
    }

    [Theory]
    [InlineData(@"\_TZ.TZ00", "TZ00")]
    [InlineData(@"ACPI\ThermalZone\TZ01_0", "TZ01")]
    [InlineData("THM", "THM")]
    [InlineData("", "Zone")]
    public void ThermalZoneNamesAreShortened(string raw, string expected)
    {
        Assert.Equal(expected, ThermalZoneReader.ZoneName(raw));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(5)]
    [InlineData(-273)]
    [InlineData(130)]
    public void ImplausibleThermalZoneValuesAreDropped(double celsius)
    {
        Assert.Null(ThermalZoneReader.Make("TZ00", celsius));
    }

    [Fact]
    public void ThermalZoneReadingIsMarkedAsAcpi()
    {
        var reading = Acpi("TZ00", 47.26f);
        Assert.StartsWith(ThermalZoneReader.IdPrefix, reading.Id, StringComparison.Ordinal);
        Assert.Equal(SensorKind.Temperature, reading.Kind);
        Assert.Equal(47.3f, reading.Value!.Value, 2);
    }

    [Fact]
    public void AcpiIsUsedAsEstimateOnlyWithoutCpuSensor()
    {
        var estimated = new SensorSnapshot { Readings = [Acpi("TZ00", 52), Acpi("TZ01", 58)] };
        Assert.Equal(58, estimated.CpuTemperature);
        Assert.True(estimated.CpuTemperatureEstimated);
        Assert.False(estimated.HasCpuSensor);

        var real = new SensorSnapshot
        {
            Readings =
            [
                Acpi("TZ00", 52),
                new SensorReading("/cpu/0/t", "CPU", SensorHardwareKind.Cpu, "CPU Package", SensorKind.Temperature, 66, null, null),
            ],
        };
        Assert.Equal(66, real.CpuTemperature);
        Assert.False(real.CpuTemperatureEstimated);
        Assert.True(real.HasCpuSensor);
    }

    [Fact]
    public void EstimatedHotCpuIsCappedAtWarningAndSuggestsAccurateSensor()
    {
        var data = TestData.HealthyPc();
        data.Sensors = new SensorSnapshot { PawnIoInstalled = false, Readings = [Acpi("TZ00", 99)] };

        var result = Analyzer.Analyze(data, TestData.Now);

        var hot = Assert.Single(result.Issues, i => i.Id == "temp/cpu");
        Assert.Equal(Severity.Warning, hot.Severity);
        Assert.Equal("issue.cpu_hot.detail_estimated", hot.Detail.Key);
        var sensor = Assert.Single(result.Issues, i => i.Id == "temp/pawnio");
        Assert.Equal("issue.pawnio.title_estimated", sensor.Title.Key);
        Assert.Contains(sensor.Fixes, f => f.Arg == Fixes.SystemTools.CpuSensor);
        Assert.DoesNotContain(result.Issues, i => i.Id == "temp/cpu_idle");
    }

    [Fact]
    public void RealCpuSensorDoesNotSuggestDriver()
    {
        var data = TestData.HealthyPc();
        data.Sensors = new SensorSnapshot
        {
            PawnIoInstalled = false,
            Readings = [new SensorReading("/cpu/0/t", "CPU", SensorHardwareKind.Cpu, "Core (Tctl/Tdie)", SensorKind.Temperature, 50, null, null)],
        };
        Assert.DoesNotContain(Analyzer.Analyze(data, TestData.Now).Issues, i => i.Id == "temp/pawnio");
    }

    [Fact]
    public void DeviceTemperaturesListEachDeviceOnce()
    {
        var snapshot = new SensorSnapshot
        {
            Readings =
            [
                Acpi("TZ00", 40),
                new SensorReading("/nvme/0/t0", "Samsung SSD 980", SensorHardwareKind.Storage, "Composite Temperature", SensorKind.Temperature, 44, null, null),
                new SensorReading("/nvme/0/t1", "Samsung SSD 980", SensorHardwareKind.Storage, "Temperature 2", SensorKind.Temperature, 51, null, null),
                new SensorReading("/gpu/0/t", "RTX 3060", SensorHardwareKind.Gpu, "GPU Core", SensorKind.Temperature, 61, null, null),
                new SensorReading("/cpu/0/t0", "Core i5", SensorHardwareKind.Cpu, "Core Max", SensorKind.Temperature, 70, null, null),
                new SensorReading("/cpu/0/t1", "Core i5", SensorHardwareKind.Cpu, "CPU Package", SensorKind.Temperature, 65, null, null),
                new SensorReading("/cpu/0/l", "Core i5", SensorHardwareKind.Cpu, "CPU Total", SensorKind.Load, 20, null, null),
            ],
        };

        var list = snapshot.DeviceTemperatures();

        Assert.Equal(["Core i5", "RTX 3060", "Samsung SSD 980"], list.Select(d => d.Name));
        Assert.Equal(65, list[0].Celsius); // Ưu tiên CPU Package
        Assert.Equal(51, list[2].Celsius); // Cảm biến nóng nhất của ổ
        Assert.All(list, d => Assert.False(d.Estimated));
    }

    [Fact]
    public void DeviceTemperaturesFallBackToAcpiForCpu()
    {
        var snapshot = new SensorSnapshot { Readings = [Acpi("TZ00", 48)] };
        var device = Assert.Single(snapshot.DeviceTemperatures());
        Assert.Equal(SensorHardwareKind.Cpu, device.Kind);
        Assert.True(device.Estimated);
        Assert.Equal(48, device.Celsius);
    }

    [Fact]
    public void IdenticalDrivesStaySeparate()
    {
        var snapshot = new SensorSnapshot
        {
            Readings =
            [
                new SensorReading("/nvme/0/temperature/0", "Samsung SSD 980", SensorHardwareKind.Storage, "Composite Temperature", SensorKind.Temperature, 40, null, null, "/nvme/0"),
                new SensorReading("/nvme/1/temperature/0", "Samsung SSD 980", SensorHardwareKind.Storage, "Composite Temperature", SensorKind.Temperature, 55, null, null, "/nvme/1"),
            ],
        };

        var list = snapshot.DeviceTemperatures();

        Assert.Equal(2, list.Count);
        Assert.Equal(["Samsung SSD 980 (1)", "Samsung SSD 980 (2)"], list.Select(d => d.Name).Order());
        Assert.Equal([40f, 55f], list.Select(d => d.Celsius).Order());
        Assert.Equal("1", snapshot.Readings[1].DriveNumber);
    }

    [Theory]
    [InlineData("/nvme/1", "1")]
    [InlineData("/hdd/12", "12")]
    [InlineData("/intelcpu/0", "0")]
    [InlineData("/lpc/nct6798d", null)]
    [InlineData(null, null)]
    public void DriveNumberComesFromHardwareId(string? id, string? expected)
    {
        Assert.Equal(expected, SensorReading.DriveNumberOf(id));
    }

    private static SensorReading Nvme(string name, float? value, float? min = null) =>
        new($"/nvme/0/temperature/{name}", "Samsung SSD 980", SensorHardwareKind.Storage, name, SensorKind.Temperature, value, min, value, "/nvme/0");

    [Theory]
    [InlineData("Warning Temperature", true)]
    [InlineData("Critical Temperature", true)]
    [InlineData("Temperature Limit", true)]
    [InlineData("TjMax", true)]
    [InlineData("CPU Core #1 Distance to TjMax", false)]
    [InlineData("Composite Temperature", false)]
    [InlineData("Core Max", false)]
    public void TemperatureLimitsAreRecognised(string name, bool isLimit)
    {
        Assert.Equal(isLimit, Nvme(name, 80).IsTemperatureLimit);
    }

    [Fact]
    public void DriveLimitsAreNotCountedAsTemperature()
    {
        var snapshot = new SensorSnapshot
        {
            Readings =
            [
                Nvme("Composite Temperature", 43),
                Nvme("Temperature #2", 46),
                Nvme("Warning Temperature", 81),
                Nvme("Critical Temperature", 84),
            ],
        };

        Assert.Equal(46, snapshot.MaxStorageTemperature);
        Assert.Equal(46, Assert.Single(snapshot.DeviceTemperatures()).Celsius);
    }

    [Fact]
    public void CpuFallbackIgnoresDistanceToTjMax()
    {
        var readings = new List<SensorReading>
        {
            new("/intelcpu/0/temperature/1", "CPU", SensorHardwareKind.Cpu, "CPU Core #1", SensorKind.Temperature, 55, null, null),
            new("/intelcpu/0/temperature/9", "CPU", SensorHardwareKind.Cpu, "CPU Core #1 Distance to TjMax", SensorKind.Temperature, 45, null, null),
            new("/intelcpu/0/temperature/10", "CPU", SensorHardwareKind.Cpu, "TjMax", SensorKind.Temperature, 100, null, null),
        };
        Assert.Equal(55, SensorSnapshot.PickCpuTemperature(readings));
    }

    [Fact]
    public void RangesIgnoreZeroTemperatureReadings()
    {
        var ranges = new Dictionary<string, (float Min, float Max)>();

        var first = new List<SensorReading> { Nvme("Composite Temperature", 0, 0) };
        Sensors.SensorService.ApplyRanges(first, ranges);
        Assert.Null(first[0].Value);
        Assert.Null(first[0].Min);

        var second = new List<SensorReading> { Nvme("Composite Temperature", 43, 0) };
        Sensors.SensorService.ApplyRanges(second, ranges);
        var third = new List<SensorReading> { Nvme("Composite Temperature", 47, 0) };
        Sensors.SensorService.ApplyRanges(third, ranges);

        Assert.Equal(43, third[0].Min);
        Assert.Equal(47, third[0].Max);
        Assert.Equal(47, third[0].Value);
    }

    [Fact]
    public void WidgetCollapsedSettingRoundTrips()
    {
        var settings = new AppSettings();
        settings.Widget.Collapsed = true;
        settings.Widget.Metrics = ["cpu_temp", "devices"];

        var loaded = AppSettings.FromJson(settings.ToJson());

        Assert.True(loaded.Widget.Collapsed);
        Assert.Equal(["cpu_temp", "devices"], loaded.Widget.Metrics);
    }
}
