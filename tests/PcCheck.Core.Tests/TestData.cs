using PcCheck.Core.Models;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PcCheck.Core.Tests;

/// <summary>Dữ liệu quét giả lập để kiểm thử analyzer mà không cần Windows.</summary>
internal static class TestData
{
    public const long Gb = 1024L * 1024 * 1024;

    public static readonly DateTime Now = new(2026, 10, 15, 10, 0, 0);

    /// <summary>Một máy khỏe mạnh: Win11 25H2, 2x8GB dual channel, SSD NVMe, mọi thứ bình thường.</summary>
    public static ScanData HealthyPc()
    {
        var data = new ScanData
        {
            Hardware = new HardwareInfo
            {
                Os = new OsInfo { Caption = "Windows 11 Pro", Build = 26200, Revision = 1000, DisplayVersion = "25H2", Edition = "Professional", Is64Bit = true, MachineName = "PC-TEST" },
                Computer = new ComputerInfo { Manufacturer = "ASUS", Model = "Desktop" },
                Cpus = [new CpuInfo { Name = "Intel Core i5-12400F", Cores = 6, Threads = 12, VirtualizationEnabled = true }],
                Board = new MotherboardInfo { Manufacturer = "ASUSTeK COMPUTER INC.", Product = "PRIME B660M-A", BiosVersion = "3010", BiosDate = new DateTime(2025, 3, 1) },
                Memory = new MemoryInfo
                {
                    TotalBytes = 16 * Gb,
                    AvailableBytes = 10 * Gb,
                    SlotCount = 4,
                    MaxCapacityBytes = 128 * Gb,
                    Modules =
                    [
                        new MemoryModule { Slot = "DIMM_A2", CapacityBytes = 8 * Gb, SpeedMhz = 3200, ConfiguredSpeedMhz = 3200, PartNumber = "KF432C16BB/8", Type = "DDR4", FormFactor = "DIMM" },
                        new MemoryModule { Slot = "DIMM_B2", CapacityBytes = 8 * Gb, SpeedMhz = 3200, ConfiguredSpeedMhz = 3200, PartNumber = "KF432C16BB/8", Type = "DDR4", FormFactor = "DIMM" },
                    ],
                },
                Gpus = [new GpuInfo { Name = "NVIDIA GeForce RTX 3060", Vendor = "NVIDIA", DriverVersion = "32.0.15.6094", VramBytes = 12 * Gb }],
                Disks =
                [
                    new DiskInfo { Number = "0", Model = "Samsung SSD 980 1TB", MediaType = DiskMediaType.Ssd, BusType = "NVMe", SizeBytes = 1_000_000_000_000, Health = DiskHealth.Healthy, TemperatureC = 42, WearPercent = 3, IsSystemDisk = true, PartitionStyle = "GPT", DriveLetters = ["C:"] },
                ],
                Volumes = [new VolumeInfo { Letter = "C:", FileSystem = "NTFS", TotalBytes = 930 * Gb, FreeBytes = 500 * Gb, IsSystem = true }],
                Monitors = [new MonitorInfo { Name = "DELL S2721DGF", DeviceName = @"\\.\DISPLAY1", IsPrimary = true, Width = 2560, Height = 1440, RefreshRate = 165, MaxRefreshAtCurrent = 165, MaxWidth = 2560, MaxHeight = 1440 }],
                Firmware = new FirmwareInfo { IsUefi = true, SecureBootEnabled = true, TpmPresent = true, TpmEnabled = true, TpmVersion = "2.0" },
            },
            Sensors = new SensorSnapshot
            {
                PawnIoInstalled = true,
                Readings =
                [
                    new SensorReading("/cpu/0/temperature/0", "Intel Core i5-12400F", SensorHardwareKind.Cpu, "CPU Package", SensorKind.Temperature, 45, 30, 70),
                    new SensorReading("/cpu/0/load/0", "Intel Core i5-12400F", SensorHardwareKind.Cpu, "CPU Total", SensorKind.Load, 8, 0, 100),
                    new SensorReading("/gpu/0/temperature/0", "RTX 3060", SensorHardwareKind.Gpu, "GPU Core", SensorKind.Temperature, 40, 35, 70),
                ],
            },
            System = new SystemStatus
            {
                IsAdmin = true,
                Uptime = TimeSpan.FromHours(5),
                Antivirus = [new AntivirusProduct("Windows Defender", true, true)],
                DefenderRealtime = true,
                DefenderSignatureAgeDays = 0,
                FirewallKnown = true,
                LicenseStatus = 1,
                LastUpdateInstalled = Now.AddDays(-5),
                UacEnabled = true,
                PageFileEnabled = true,
            },
            Junk = [new JunkCategory { Key = "user-temp", Name = new Localization.LocText("junk.user_temp.name"), Description = new Localization.LocText("junk.user_temp.desc"), DefaultSelected = true, Bytes = 50L * 1024 * 1024, Files = 100 }],
            Devices =
            [
                new DeviceInfo { Name = "NVIDIA GeForce RTX 3060", ClassName = "Display", Service = "nvlddmkm", DriverVersion = "32.0.15.6094", DriverProvider = "NVIDIA", DriverDate = Now.AddMonths(-2), IsSigned = true },
                new DeviceInfo { Name = "Realtek Audio", ClassName = "MEDIA", Service = "IntcAzAudAddService", DriverVersion = "6.0.9", DriverProvider = "Realtek", DriverDate = Now.AddYears(-1), IsSigned = true },
            ],
        };
        return data;
    }
}
