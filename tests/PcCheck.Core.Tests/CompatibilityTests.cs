using PcCheck.Core.Analysis;
using PcCheck.Core.Models;

namespace PcCheck.Core.Tests;

public sealed class CompatibilityTests
{
    private static CompatibilityCheck Check(ScanData data, string id) =>
        CompatibilityAnalyzer.Analyze(data, TestData.Now).Single(c => c.Id == id);

    [Fact]
    public void HealthyPcPassesMemoryChecks()
    {
        var data = TestData.HealthyPc();
        Assert.Equal(CheckStatus.Pass, Check(data, "ram/channel").Status);
        Assert.Equal(CheckStatus.Pass, Check(data, "ram/matched").Status);
        Assert.Equal(CheckStatus.Pass, Check(data, "ram/speed").Status);
        Assert.Equal(CheckStatus.Pass, Check(data, "ram/capacity").Status);
    }

    [Fact]
    public void SingleStickIsSingleChannel()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Memory.Modules.RemoveAt(1);
        Assert.Equal(CheckStatus.Warning, Check(data, "ram/channel").Status);
    }

    [Fact]
    public void MismatchedSticksAreFlagged()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Memory.Modules[1].CapacityBytes = 16 * TestData.Gb;
        data.Hardware.Memory.Modules[1].SpeedMhz = 2666;
        Assert.Equal(CheckStatus.Warning, Check(data, "ram/matched").Status);
    }

    [Fact]
    public void XmpOffOnDesktopIsWarningButInfoOnLaptop()
    {
        var data = TestData.HealthyPc();
        foreach (var m in data.Hardware.Memory.Modules) m.ConfiguredSpeedMhz = 2133;
        Assert.Equal(CheckStatus.Warning, Check(data, "ram/speed").Status);

        data.Hardware.Computer.IsLaptop = true;
        Assert.Equal(CheckStatus.Info, Check(data, "ram/speed").Status);
    }

    [Fact]
    public void HddSystemDiskIsWarning()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Disks[0].MediaType = DiskMediaType.Hdd;
        data.Hardware.Disks[0].BusType = "SATA";
        Assert.Equal(CheckStatus.Warning, Check(data, "storage/system_ssd").Status);
    }

    [Fact]
    public void ThirtyTwoBitWindowsWithLotsOfRamFails()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Os.Is64Bit = false;
        Assert.Equal(CheckStatus.Fail, Check(data, "os/arch").Status);
    }

    [Fact]
    public void LowRefreshRateIsDetected()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Monitors[0].RefreshRate = 60;
        Assert.Equal(CheckStatus.Warning, Check(data, @"display/refresh/\\.\DISPLAY1").Status);
    }

    [Fact]
    public void Windows10PcWithoutTpmIsBlocked()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Os.Build = 19045;
        data.Hardware.Firmware.TpmPresent = false;
        Assert.Equal(CheckStatus.Fail, Check(data, "win11/tpm").Status);
        Assert.Equal(CheckStatus.Fail, Check(data, "win11/summary").Status);
    }

    [Fact]
    public void Windows10PcMeetingRequirementsIsReady()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Os.Build = 19045;
        Assert.Equal(CheckStatus.Pass, Check(data, "win11/summary").Status);
    }

    [Fact]
    public void MultipleThirdPartyAntivirusConflict()
    {
        var data = TestData.HealthyPc();
        data.System.Antivirus = [new("Kaspersky", true, true), new("Avast Antivirus", true, true), new("Windows Defender", false, true)];
        Assert.Equal(CheckStatus.Warning, Check(data, "security/multi_av").Status);
    }

    [Fact]
    public void VirtualizationOnWhenHypervisorPresent()
    {
        var data = TestData.HealthyPc();
        data.Hardware.Cpus[0].VirtualizationEnabled = false;
        Assert.Equal(CheckStatus.Info, Check(data, "cpu/virtualization").Status);
        data.Hardware.Computer.HypervisorPresent = true;
        Assert.Equal(CheckStatus.Pass, Check(data, "cpu/virtualization").Status);
    }

    [Theory]
    [InlineData("Intel Core i5-8250U @ 1.60GHz", CheckStatus.Pass)]
    [InlineData("Intel Core i7-7700HQ @ 2.80GHz", CheckStatus.Fail)]
    [InlineData("Intel Core i7-10700K @ 3.80GHz", CheckStatus.Pass)]
    [InlineData("Intel Core i5-1135G7 @ 2.40GHz", CheckStatus.Pass)]
    [InlineData("Intel Core i5-2500K @ 3.30GHz", CheckStatus.Fail)]
    [InlineData("Intel Core i5 650 @ 3.20GHz", CheckStatus.Fail)]
    [InlineData("Intel Core Ultra 7 155H", CheckStatus.Pass)]
    [InlineData("Intel Core 5 120U", CheckStatus.Pass)]
    [InlineData("Intel Processor N100", CheckStatus.Pass)]
    [InlineData("AMD Ryzen 5 3600 6-Core Processor", CheckStatus.Pass)]
    [InlineData("AMD Ryzen 7 1700 Eight-Core Processor", CheckStatus.Fail)]
    [InlineData("AMD Ryzen 7 PRO 4750U with Radeon Graphics", CheckStatus.Pass)]
    [InlineData("AMD Ryzen Threadripper 1950X 16-Core Processor", CheckStatus.Fail)]
    [InlineData("AMD Ryzen AI 9 HX 370", CheckStatus.Pass)]
    [InlineData("AMD FX-8350 Eight-Core Processor", CheckStatus.Fail)]
    [InlineData("AMD A10-9600P RADEON R5", CheckStatus.Fail)]
    [InlineData("Intel Core2 Duo CPU E8400", CheckStatus.Fail)]
    [InlineData("Intel Pentium Gold 7505", CheckStatus.Unknown)]
    [InlineData("", CheckStatus.Unknown)]
    public void Windows11CpuHeuristic(string name, CheckStatus expected)
    {
        Assert.Equal(expected, Windows11Cpu.Evaluate(name));
    }
}
