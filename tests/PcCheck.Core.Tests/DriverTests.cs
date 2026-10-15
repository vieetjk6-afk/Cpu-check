using PcCheck.Core.Analysis;
using PcCheck.Core.Models;

namespace PcCheck.Core.Tests;

public sealed class DriverTests
{
    private static DriverProblem? Classify(DeviceInfo d) => DriverAnalyzer.Classify(d, TestData.Now);

    [Fact]
    public void BasicDisplayAdapterMeansMissingGpuDriver()
    {
        var p = Classify(new DeviceInfo { Name = "Microsoft Basic Display Adapter", ClassName = "Display", Service = "BasicDisplay", DriverProvider = "Microsoft" });
        Assert.NotNull(p);
        Assert.Equal(DriverProblemKind.Generic, p.Kind);
        Assert.Equal(Severity.Critical, p.Severity);
    }

    [Fact]
    public void Code28IsMissingDriver()
    {
        var p = Classify(new DeviceInfo { Name = "PCI Device", ErrorCode = 28, HardwareIds = [@"PCI\VEN_8086&DEV_A2BA&SUBSYS_86941043&REV_00"] });
        Assert.NotNull(p);
        Assert.Equal(DriverProblemKind.Missing, p.Kind);
        Assert.Equal(@"PCI\VEN_8086&DEV_A2BA", p.Device.SearchId);
    }

    [Fact]
    public void Code43OnGpuIsCriticalDeviceError()
    {
        var p = Classify(new DeviceInfo { Name = "NVIDIA GeForce GTX 1650", ClassName = "Display", ErrorCode = 43, Service = "nvlddmkm", DriverVersion = "31.0" });
        Assert.NotNull(p);
        Assert.Equal(DriverProblemKind.DeviceError, p.Kind);
        Assert.Equal(Severity.Critical, p.Severity);
    }

    [Fact]
    public void DisconnectedDevicesAreIgnoredAndDisabledAreInfo()
    {
        Assert.Null(Classify(new DeviceInfo { Name = "Old USB", ErrorCode = 45 }));
        Assert.Equal(DriverProblemKind.Disabled, Classify(new DeviceInfo { Name = "Webcam", ErrorCode = 22, Service = "usbvideo" })!.Kind);
    }

    [Fact]
    public void OldVendorDriverIsOutdatedButMicrosoftInboxIsNot()
    {
        var old = new DeviceInfo { Name = "Realtek PCIe GbE", ClassName = "Net", Service = "rt640x64", DriverProvider = "Realtek", DriverDate = new DateTime(2019, 1, 1), IsSigned = true };
        Assert.Equal(DriverProblemKind.Outdated, Classify(old)!.Kind);

        var inbox = new DeviceInfo { Name = "WAN Miniport", ClassName = "Net", Service = "x", DriverProvider = "Microsoft", DriverDate = new DateTime(2006, 6, 21), IsSigned = true };
        Assert.Null(Classify(inbox));

        var fakeDate = new DeviceInfo { Name = "Intel Chipset", ClassName = "Net", Service = "x", DriverProvider = "INTEL", DriverDate = new DateTime(1968, 7, 18), IsSigned = true };
        Assert.Null(Classify(fakeDate));
    }

    [Fact]
    public void GpuDriverOlderThanAYearIsOutdated()
    {
        var gpu = new DeviceInfo { Name = "AMD Radeon RX 580", ClassName = "Display", Service = "amdkmdag", DriverProvider = "Advanced Micro Devices, Inc.", DriverDate = TestData.Now.AddMonths(-18), IsSigned = true };
        Assert.Equal(DriverProblemKind.Outdated, Classify(gpu)!.Kind);
    }

    [Fact]
    public void UnsignedDriverIsWarning()
    {
        var p = Classify(new DeviceInfo { Name = "Some Device", ClassName = "USB", Service = "x", IsSigned = false });
        Assert.Equal(DriverProblemKind.Unsigned, p!.Kind);
        Assert.Equal(Severity.Warning, p.Severity);
    }

    [Fact]
    public void GenericHdAudioIsInfo()
    {
        var p = Classify(new DeviceInfo { Name = "High Definition Audio Device", ClassName = "MEDIA", Service = "HdAudio", DriverProvider = "Microsoft" });
        Assert.Equal(DriverProblemKind.Generic, p!.Kind);
        Assert.Equal(Severity.Info, p.Severity);
    }

    [Fact]
    public void AnalyzerProducesIssuesWithVendorLinks()
    {
        var data = TestData.HealthyPc();
        data.Devices.Add(new DeviceInfo { Name = "Microsoft Basic Display Adapter", ClassName = "Display", Service = "BasicDisplay" });
        data.Devices.Add(new DeviceInfo { Name = "Unknown device", ErrorCode = 28, HardwareIds = [@"ACPI\VEN_ELAN&DEV_0001"] });
        var result = Analyzer.Analyze(data, TestData.Now);

        var gpuIssue = result.Issues.Single(i => i.Id == "driver/generic_display");
        Assert.Contains(gpuIssue.Fixes, f => f.Arg is not null && f.Arg.Contains("nvidia.com", StringComparison.Ordinal));
        Assert.Contains(result.Issues, i => i.Id.StartsWith("driver/Missing", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Dell Inc.", "https://www.dell.com/support/home")]
    [InlineData("HP", "https://support.hp.com/drivers")]
    [InlineData("LENOVO", "https://pcsupport.lenovo.com")]
    [InlineData("ASUSTeK COMPUTER INC.", "https://www.asus.com/support/download-center/")]
    [InlineData("Micro-Star International Co., Ltd.", "https://www.msi.com/support/download")]
    [InlineData("To Be Filled By O.E.M.", null)]
    public void OemDriverPages(string manufacturer, string? expected)
    {
        Assert.Equal(expected, VendorSupport.OemDriverPage(manufacturer));
    }
}
