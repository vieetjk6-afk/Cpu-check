namespace PcCheck.Core.Models;

public sealed class HardwareInfo
{
    public OsInfo Os { get; set; } = new();
    public ComputerInfo Computer { get; set; } = new();
    public List<CpuInfo> Cpus { get; set; } = [];
    public MotherboardInfo Board { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public List<GpuInfo> Gpus { get; set; } = [];
    public List<DiskInfo> Disks { get; set; } = [];
    public List<VolumeInfo> Volumes { get; set; } = [];
    public List<MonitorInfo> Monitors { get; set; } = [];
    public List<NetworkAdapterInfo> Networks { get; set; } = [];
    public List<AudioDeviceInfo> AudioDevices { get; set; } = [];
    public BatteryInfo? Battery { get; set; }
    public FirmwareInfo Firmware { get; set; } = new();
}

public sealed class OsInfo
{
    public string Caption { get; set; } = "";
    public string Version { get; set; } = "";
    public int Build { get; set; }
    public int Revision { get; set; }
    public string DisplayVersion { get; set; } = "";
    public string Edition { get; set; } = "";
    public bool Is64Bit { get; set; } = true;
    public DateTime? InstallDate { get; set; }
    public DateTime? LastBoot { get; set; }
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";

    public bool IsWindows11 => Build >= 22000;
}

public sealed class ComputerInfo
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public bool IsLaptop { get; set; }
    public bool HypervisorPresent { get; set; }
}

public sealed class CpuInfo
{
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public int MaxClockMhz { get; set; }
    public int L2CacheKb { get; set; }
    public int L3CacheKb { get; set; }
    public string Socket { get; set; } = "";
    public bool? VirtualizationEnabled { get; set; }

    /// <summary>0 = x86, 5 = ARM, 9 = x64, 12 = ARM64 (theo Win32_Processor.Architecture).</summary>
    public int Architecture { get; set; } = 9;

    public bool Is64BitCapable => Architecture is 9 or 12;
}

public sealed class MotherboardInfo
{
    public string Manufacturer { get; set; } = "";
    public string Product { get; set; } = "";
    public string Version { get; set; } = "";
    public string BiosVendor { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public DateTime? BiosDate { get; set; }
}

public sealed class MemoryInfo
{
    public long TotalBytes { get; set; }
    public long AvailableBytes { get; set; }
    public int SlotCount { get; set; }
    public long MaxCapacityBytes { get; set; }
    public List<MemoryModule> Modules { get; set; } = [];

    public double UsedPercent => TotalBytes <= 0 ? 0 : 100d * (TotalBytes - AvailableBytes) / TotalBytes;
    public long InstalledBytes => Modules.Count > 0 ? Modules.Sum(m => m.CapacityBytes) : TotalBytes;
}

public sealed class MemoryModule
{
    public string Slot { get; set; } = "";
    public string Bank { get; set; } = "";
    public long CapacityBytes { get; set; }
    public int SpeedMhz { get; set; }
    public int ConfiguredSpeedMhz { get; set; }
    public string Manufacturer { get; set; } = "";
    public string PartNumber { get; set; } = "";
    public string Type { get; set; } = "";
    public string FormFactor { get; set; } = "";
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public DateTime? DriverDate { get; set; }
    public long VramBytes { get; set; }
    public string PnpDeviceId { get; set; } = "";
    public int ResolutionX { get; set; }
    public int ResolutionY { get; set; }
    public int RefreshRate { get; set; }
    public int ErrorCode { get; set; }

    public bool IsBasicDriver =>
        Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Standard VGA", StringComparison.OrdinalIgnoreCase);

    public bool IsVirtual =>
        Name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Parsec", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase);
}

public enum DiskMediaType
{
    Unknown,
    Hdd,
    Ssd,
}

public sealed class DiskInfo
{
    public string Number { get; set; } = "";
    public string Model { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Firmware { get; set; } = "";
    public DiskMediaType MediaType { get; set; }
    public string BusType { get; set; } = "";
    public long SizeBytes { get; set; }
    public DiskHealth Health { get; set; } = DiskHealth.Unknown;
    public bool? PredictFailure { get; set; }
    public int? TemperatureC { get; set; }
    public int? TemperatureMaxC { get; set; }

    /// <summary>Phần trăm tuổi thọ SSD đã dùng (0 = mới, 100 = hết).</summary>
    public int? WearPercent { get; set; }

    public long? PowerOnHours { get; set; }
    public long? ReadErrorsUncorrected { get; set; }
    public long? WriteErrorsUncorrected { get; set; }
    public bool IsSystemDisk { get; set; }
    public string PartitionStyle { get; set; } = "";
    public List<string> DriveLetters { get; set; } = [];

    public bool IsNvme => BusType.Equals("NVMe", StringComparison.OrdinalIgnoreCase);
    public bool IsUsb => BusType.Equals("USB", StringComparison.OrdinalIgnoreCase);
    public bool IsSolidState => MediaType == DiskMediaType.Ssd || IsNvme;
}

public enum DiskHealth
{
    Unknown,
    Healthy,
    Warning,
    Unhealthy,
}

public sealed class VolumeInfo
{
    public string Letter { get; set; } = "";
    public string Label { get; set; } = "";
    public string FileSystem { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public bool IsSystem { get; set; }

    public double FreePercent => TotalBytes <= 0 ? 0 : 100d * FreeBytes / TotalBytes;
    public double UsedPercent => 100 - FreePercent;
}

public sealed class MonitorInfo
{
    public string Name { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public bool IsPrimary { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public int MaxRefreshAtCurrent { get; set; }
    public int MaxWidth { get; set; }
    public int MaxHeight { get; set; }
}

public sealed class NetworkAdapterInfo
{
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public long SpeedBps { get; set; }
    public bool Connected { get; set; }
    public bool IsWireless { get; set; }
}

public sealed class AudioDeviceInfo
{
    public string Name { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class BatteryInfo
{
    public string Name { get; set; } = "";
    public long DesignCapacity { get; set; }
    public long FullChargeCapacity { get; set; }
    public int? CycleCount { get; set; }
    public int? ChargePercent { get; set; }
    public bool? IsCharging { get; set; }

    /// <summary>Độ chai pin (%), null nếu không đọc được dung lượng.</summary>
    public double? WearPercent =>
        DesignCapacity > 0 && FullChargeCapacity > 0
            ? Math.Clamp(100d * (DesignCapacity - FullChargeCapacity) / DesignCapacity, 0, 100)
            : null;
}

public sealed class FirmwareInfo
{
    public bool? IsUefi { get; set; }
    public bool? SecureBootEnabled { get; set; }
    public bool? TpmPresent { get; set; }
    public bool? TpmEnabled { get; set; }
    public string TpmVersion { get; set; } = "";
}
