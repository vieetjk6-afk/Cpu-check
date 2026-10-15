using System.Globalization;
using Microsoft.Win32;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

/// <summary>Thu thập thông tin phần cứng qua WMI, Registry và Win32 API.</summary>
public static class HardwareCollector
{
    private static readonly HashSet<int> LaptopChassis = [8, 9, 10, 11, 14, 30, 31, 32];

    public static HardwareInfo Collect()
    {
        var info = new HardwareInfo();
        Parallel.Invoke(
            () => info.Os = Safe(CollectOs, new OsInfo()),
            () => info.Computer = Safe(CollectComputer, new ComputerInfo()),
            () => info.Cpus = Safe(CollectCpus, []),
            () => info.Board = Safe(CollectBoard, new MotherboardInfo()),
            () => info.Memory = Safe(CollectMemory, new MemoryInfo()),
            () => info.Gpus = Safe(CollectGpus, []),
            () => info.Disks = Safe(CollectDisks, []),
            () => info.Monitors = Safe(CollectMonitors, []),
            () => info.Networks = Safe(CollectNetworks, []),
            () => info.AudioDevices = Safe(CollectAudio, []),
            () => info.Battery = Safe(CollectBattery, null),
            () => info.Firmware = Safe(CollectFirmware, new FirmwareInfo()));

        info.Volumes = Safe(CollectVolumes, []);
        if (info.Battery is not null) info.Computer.IsLaptop = true;
        return info;
    }

    private static T Safe<T>(Func<T> func, T fallback)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            Log.Warn($"Hardware collector {func.Method.Name} failed", ex);
            return fallback;
        }
    }

    private static OsInfo CollectOs()
    {
        var os = new OsInfo
        {
            Is64Bit = Environment.Is64BitOperatingSystem,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
        };

        var row = Wmi.First("SELECT Caption, Version, BuildNumber, InstallDate, LastBootUpTime FROM Win32_OperatingSystem");
        if (row is not null)
        {
            os.Caption = row.Str("Caption").Replace("Microsoft ", "", StringComparison.OrdinalIgnoreCase);
            os.Version = row.Str("Version");
            os.Build = int.TryParse(row.Str("BuildNumber"), out var build) ? build : Environment.OSVersion.Version.Build;
            os.InstallDate = row.Date("InstallDate");
            os.LastBoot = row.Date("LastBootUpTime");
        }

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key is not null)
        {
            os.DisplayVersion = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string ?? "";
            os.Edition = key.GetValue("EditionID") as string ?? "";
            if (key.GetValue("UBR") is int ubr) os.Revision = ubr;
            if (string.IsNullOrEmpty(os.Caption)) os.Caption = key.GetValue("ProductName") as string ?? "Windows";
        }

        if (os.Build == 0) os.Build = Environment.OSVersion.Version.Build;

        // ProductName trong registry vẫn ghi "Windows 10" trên Win11 -> chuẩn hóa theo build.
        if (os.IsWindows11 && os.Caption.Contains("Windows 10", StringComparison.OrdinalIgnoreCase))
        {
            os.Caption = os.Caption.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        return os;
    }

    private static ComputerInfo CollectComputer()
    {
        var computer = new ComputerInfo();
        var row = Wmi.First("SELECT Manufacturer, Model, PCSystemType, HypervisorPresent FROM Win32_ComputerSystem");
        if (row is not null)
        {
            computer.Manufacturer = row.Str("Manufacturer");
            computer.Model = row.Str("Model");
            computer.IsLaptop = row.Int("PCSystemType") == 2;
            computer.HypervisorPresent = row.Bool("HypervisorPresent") ?? false;
        }

        var enclosure = Wmi.First("SELECT ChassisTypes FROM Win32_SystemEnclosure");
        if (enclosure is not null && enclosure.TryGetValue("ChassisTypes", out var raw) && raw is Array chassis)
        {
            foreach (var c in chassis)
            {
                if (LaptopChassis.Contains(Convert.ToInt32(c, CultureInfo.InvariantCulture))) computer.IsLaptop = true;
            }
        }

        return computer;
    }

    private static List<CpuInfo> CollectCpus() =>
        Wmi.Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation, VirtualizationFirmwareEnabled, Architecture FROM Win32_Processor")
            .Select(r => new CpuInfo
            {
                Name = CleanCpuName(r.Str("Name")),
                Manufacturer = r.Str("Manufacturer"),
                Cores = r.Int("NumberOfCores") ?? 0,
                Threads = r.Int("NumberOfLogicalProcessors") ?? 0,
                MaxClockMhz = r.Int("MaxClockSpeed") ?? 0,
                L2CacheKb = r.Int("L2CacheSize") ?? 0,
                L3CacheKb = r.Int("L3CacheSize") ?? 0,
                Socket = r.Str("SocketDesignation"),
                VirtualizationEnabled = r.Bool("VirtualizationFirmwareEnabled"),
                Architecture = r.Int("Architecture") ?? 9,
            })
            .ToList();

    internal static string CleanCpuName(string name)
    {
        name = name.Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" CPU ", " ", StringComparison.OrdinalIgnoreCase);
        while (name.Contains("  ", StringComparison.Ordinal)) name = name.Replace("  ", " ", StringComparison.Ordinal);
        return name.Trim();
    }

    private static MotherboardInfo CollectBoard()
    {
        var board = new MotherboardInfo();
        var bb = Wmi.First("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard");
        if (bb is not null)
        {
            board.Manufacturer = bb.Str("Manufacturer");
            board.Product = bb.Str("Product");
            board.Version = bb.Str("Version");
        }

        var bios = Wmi.First("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
        if (bios is not null)
        {
            board.BiosVendor = bios.Str("Manufacturer");
            board.BiosVersion = bios.Str("SMBIOSBIOSVersion");
            board.BiosDate = bios.Date("ReleaseDate");
        }

        return board;
    }

    private static MemoryInfo CollectMemory()
    {
        var (total, available) = NativeMethods.GetMemoryStatus();
        var memory = new MemoryInfo { TotalBytes = (long)total, AvailableBytes = (long)available };

        memory.Modules = Wmi.Query("SELECT BankLabel, DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SMBIOSMemoryType, FormFactor FROM Win32_PhysicalMemory")
            .Select(r => new MemoryModule
            {
                Slot = r.Str("DeviceLocator"),
                Bank = r.Str("BankLabel"),
                CapacityBytes = r.Long("Capacity") ?? 0,
                SpeedMhz = r.Int("Speed") ?? 0,
                ConfiguredSpeedMhz = r.Int("ConfiguredClockSpeed") ?? 0,
                Manufacturer = RamVendor(r.Str("Manufacturer")),
                PartNumber = r.Str("PartNumber"),
                Type = MemoryType(r.Int("SMBIOSMemoryType") ?? 0),
                FormFactor = (r.Int("FormFactor") ?? 0) switch { 8 => "DIMM", 12 => "SO-DIMM", _ => "" },
            })
            .Where(m => m.CapacityBytes > 0)
            .ToList();

        foreach (var array in Wmi.Query("SELECT MaxCapacity, MaxCapacityEx, MemoryDevices, Use FROM Win32_PhysicalMemoryArray"))
        {
            var use = array.Int("Use");
            if (use is not null and not 3) continue; // 3 = bộ nhớ hệ thống
            memory.SlotCount += array.Int("MemoryDevices") ?? 0;
            var maxKb = array.Long("MaxCapacityEx") is long ex && ex > 0 ? ex : array.Long("MaxCapacity") ?? 0;
            memory.MaxCapacityBytes += maxKb * 1024;
        }

        return memory;
    }

    internal static string MemoryType(int smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 or 22 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => "",
    };

    /// <summary>Một số máy trả về mã JEDEC thay vì tên hãng RAM.</summary>
    internal static string RamVendor(string raw)
    {
        var code = raw.Trim().ToUpperInvariant();
        if (code.Length >= 4 && code.All(Uri.IsHexDigit))
        {
            var key = code[..4];
            return key switch
            {
                "80CE" or "CE00" => "Samsung",
                "80AD" or "AD00" => "SK Hynix",
                "802C" or "2C00" => "Micron",
                "859B" or "059B" => "Crucial",
                "0198" or "9801" => "Kingston",
                "04CD" or "CD04" => "G.Skill",
                "029E" or "9E02" => "Corsair",
                "04CB" or "CB04" => "ADATA",
                "0443" => "Ramaxel",
                "01BA" => "PNY",
                "8551" => "Qimonda",
                "0783" => "Transcend",
                "04F1" => "Team Group",
                _ => raw.Trim(),
            };
        }

        return raw.Trim();
    }

    private static List<GpuInfo> CollectGpus()
    {
        var vram = ReadGpuMemoryFromRegistry();
        return Wmi.Query("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, AdapterRAM, PNPDeviceID, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, ConfigManagerErrorCode FROM Win32_VideoController")
            .Select(r =>
            {
                var gpu = new GpuInfo
                {
                    Name = r.Str("Name"),
                    Vendor = r.Str("AdapterCompatibility"),
                    DriverVersion = r.Str("DriverVersion"),
                    DriverDate = r.Date("DriverDate"),
                    PnpDeviceId = r.Str("PNPDeviceID"),
                    ResolutionX = r.Int("CurrentHorizontalResolution") ?? 0,
                    ResolutionY = r.Int("CurrentVerticalResolution") ?? 0,
                    RefreshRate = r.Int("CurrentRefreshRate") ?? 0,
                    ErrorCode = r.Int("ConfigManagerErrorCode") ?? 0,
                    VramBytes = r.Long("AdapterRAM") ?? 0,
                };
                if (vram.TryGetValue(gpu.Name, out var bytes) && bytes > gpu.VramBytes) gpu.VramBytes = bytes;
                return gpu;
            })
            .ToList();
    }

    /// <summary>AdapterRAM trong WMI bị giới hạn 4 GB, nên đọc qwMemorySize trong registry của driver.</summary>
    private static Dictionary<string, long> ReadGpuMemoryFromRegistry()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return result;
        foreach (var sub in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
        {
            try
            {
                using var key = cls.OpenSubKey(sub);
                if (key?.GetValue("DriverDesc") is not string desc) continue;
                long bytes = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => l,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => key.GetValue("HardwareInformation.MemorySize") switch
                    {
                        int i => (uint)i,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0,
                    },
                };
                if (bytes > 0) result[desc] = Math.Max(bytes, result.GetValueOrDefault(desc));
            }
            catch (System.Security.SecurityException)
            {
                // Một số khóa con bị khóa quyền, bỏ qua.
            }
        }

        return result;
    }

    private static List<DiskInfo> CollectDisks()
    {
        var disks = Wmi.Query("SELECT DeviceId, FriendlyName, SerialNumber, FirmwareVersion, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", Wmi.Storage)
            .Select(r => new DiskInfo
            {
                Number = r.Str("DeviceId"),
                Model = r.Str("FriendlyName"),
                Serial = r.Str("SerialNumber"),
                Firmware = r.Str("FirmwareVersion"),
                MediaType = (r.Int("MediaType") ?? 0) switch { 3 => DiskMediaType.Hdd, 4 or 5 => DiskMediaType.Ssd, _ => DiskMediaType.Unknown },
                BusType = BusTypeName(r.Int("BusType") ?? 0),
                SizeBytes = r.Long("Size") ?? 0,
                Health = (r.Int("HealthStatus") ?? 5) switch
                {
                    0 => DiskHealth.Healthy,
                    1 => DiskHealth.Warning,
                    2 => DiskHealth.Unhealthy,
                    _ => DiskHealth.Unknown,
                },
            })
            .ToList();

        // Máy cũ / không có Storage WMI: dùng Win32_DiskDrive.
        var diskDrives = Wmi.Query("SELECT Index, Model, PNPDeviceID, SerialNumber, Size, InterfaceType, FirmwareRevision FROM Win32_DiskDrive");
        if (disks.Count == 0)
        {
            disks = diskDrives.Select(r => new DiskInfo
            {
                Number = r.Str("Index"),
                Model = r.Str("Model"),
                Serial = r.Str("SerialNumber"),
                Firmware = r.Str("FirmwareRevision"),
                SizeBytes = r.Long("Size") ?? 0,
                BusType = r.Str("InterfaceType"),
            }).ToList();
        }

        var byNumber = disks.Where(d => d.Number != "").GroupBy(d => d.Number).ToDictionary(g => g.Key, g => g.First());

        foreach (var counter in Wmi.Query("SELECT DeviceId, Temperature, TemperatureMax, Wear, ReadErrorsUncorrected, WriteErrorsUncorrected, PowerOnHours FROM MSFT_StorageReliabilityCounter", Wmi.Storage))
        {
            if (!byNumber.TryGetValue(counter.Str("DeviceId"), out var disk)) continue;
            disk.TemperatureC = counter.Int("Temperature") is > 0 and < 150 and var t ? t : null;
            disk.TemperatureMaxC = counter.Int("TemperatureMax") is > 0 and < 150 and var tm ? tm : null;
            disk.WearPercent = counter.Int("Wear") is >= 0 and <= 100 and var w ? w : null;
            disk.PowerOnHours = counter.Long("PowerOnHours") is > 0 and var h ? h : null;
            disk.ReadErrorsUncorrected = counter.Long("ReadErrorsUncorrected");
            disk.WriteErrorsUncorrected = counter.Long("WriteErrorsUncorrected");
        }

        // S.M.A.R.T dự báo hỏng (ổ SATA): ghép theo PNPDeviceID của Win32_DiskDrive.
        var predict = Wmi.Query("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", Wmi.RootWmi);
        foreach (var drive in diskDrives)
        {
            if (!byNumber.TryGetValue(drive.Str("Index"), out var disk)) continue;
            var pnp = drive.Str("PNPDeviceID");
            var hit = predict.FirstOrDefault(p => pnp.Length > 0 && p.Str("InstanceName").StartsWith(pnp, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) disk.PredictFailure = hit.Bool("PredictFailure");
            if (string.IsNullOrEmpty(disk.Firmware)) disk.Firmware = drive.Str("FirmwareRevision");
        }

        foreach (var d in Wmi.Query("SELECT Number, PartitionStyle FROM MSFT_Disk", Wmi.Storage))
        {
            if (!byNumber.TryGetValue(d.Str("Number"), out var disk)) continue;
            disk.PartitionStyle = (d.Int("PartitionStyle") ?? 0) switch { 1 => "MBR", 2 => "GPT", _ => "RAW" };
        }

        var systemLetter = SystemDriveLetter();
        foreach (var p in Wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Wmi.Storage))
        {
            if (!byNumber.TryGetValue(p.Str("DiskNumber"), out var disk)) continue;
            var letter = p.Str("DriveLetter");
            if (letter.Length == 0 || !char.IsLetter(letter[0])) continue;
            var drive = char.ToUpperInvariant(letter[0]) + ":";
            disk.DriveLetters.Add(drive);
            if (drive.Equals(systemLetter, StringComparison.OrdinalIgnoreCase)) disk.IsSystemDisk = true;
        }

        return disks.OrderBy(d => int.TryParse(d.Number, out var n) ? n : 99).ToList();
    }

    internal static string BusTypeName(int busType) => busType switch
    {
        1 => "SCSI",
        2 => "ATAPI",
        3 => "ATA",
        4 => "1394",
        6 => "Fibre Channel",
        7 => "USB",
        8 => "RAID",
        9 => "iSCSI",
        10 => "SAS",
        11 => "SATA",
        12 => "SD",
        13 => "MMC",
        14 or 15 => "Virtual",
        16 => "Storage Spaces",
        17 => "NVMe",
        18 => "SCM",
        19 => "UFS",
        _ => "",
    };

    internal static string SystemDriveLetter() =>
        (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\").TrimEnd('\\').ToUpperInvariant();

    private static List<VolumeInfo> CollectVolumes()
    {
        var systemLetter = SystemDriveLetter();
        var volumes = new List<VolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                var letter = drive.Name.TrimEnd('\\').ToUpperInvariant();
                volumes.Add(new VolumeInfo
                {
                    Letter = letter,
                    Label = drive.VolumeLabel,
                    FileSystem = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace,
                    IsSystem = letter == systemLetter,
                });
            }
            catch (IOException)
            {
                // Ổ đang bận / BitLocker khóa.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return volumes;
    }

    private static List<MonitorInfo> CollectMonitors()
    {
        var friendlyNames = Wmi.Query("SELECT UserFriendlyName, ManufacturerName, Active FROM WmiMonitorID", Wmi.RootWmi)
            .Where(r => r.Bool("Active") != false)
            .Select(r =>
            {
                var name = r.UShortString("UserFriendlyName");
                var maker = r.UShortString("ManufacturerName");
                return string.IsNullOrWhiteSpace(name) ? maker : name;
            })
            .ToList();

        var monitors = new List<MonitorInfo>();
        var adapter = new NativeMethods.DisplayDevice { cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DisplayDevice>() };
        for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0); i++)
        {
            if ((adapter.StateFlags & NativeMethods.DisplayDeviceAttachedToDesktop) != 0)
            {
                var monitor = new MonitorInfo
                {
                    DeviceName = adapter.DeviceName,
                    IsPrimary = (adapter.StateFlags & NativeMethods.DisplayDevicePrimaryDevice) != 0,
                };

                var mon = new NativeMethods.DisplayDevice { cb = adapter.cb };
                if (NativeMethods.EnumDisplayDevices(adapter.DeviceName, 0, ref mon, 0)) monitor.Name = mon.DeviceString;

                var mode = NewDevMode();
                if (NativeMethods.EnumDisplaySettings(adapter.DeviceName, NativeMethods.EnumCurrentSettings, ref mode))
                {
                    monitor.Width = mode.dmPelsWidth;
                    monitor.Height = mode.dmPelsHeight;
                    monitor.RefreshRate = mode.dmDisplayFrequency;
                }

                for (var m = 0; m < 2000; m++)
                {
                    var candidate = NewDevMode();
                    if (!NativeMethods.EnumDisplaySettings(adapter.DeviceName, m, ref candidate)) break;
                    if ((long)candidate.dmPelsWidth * candidate.dmPelsHeight > (long)monitor.MaxWidth * monitor.MaxHeight)
                    {
                        monitor.MaxWidth = candidate.dmPelsWidth;
                        monitor.MaxHeight = candidate.dmPelsHeight;
                    }

                    if (candidate.dmPelsWidth == monitor.Width && candidate.dmPelsHeight == monitor.Height)
                    {
                        monitor.MaxRefreshAtCurrent = Math.Max(monitor.MaxRefreshAtCurrent, candidate.dmDisplayFrequency);
                    }
                }

                monitors.Add(monitor);
            }

            adapter = new NativeMethods.DisplayDevice { cb = adapter.cb };
        }

        if (friendlyNames.Count == monitors.Count)
        {
            for (var i = 0; i < monitors.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(friendlyNames[i])) monitors[i].Name = friendlyNames[i];
            }
        }

        return monitors;
    }

    private static NativeMethods.DevMode NewDevMode() =>
        new()
        {
            dmDeviceName = "",
            dmFormName = "",
            dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DevMode>(),
        };

    private static readonly string[] VirtualAdapterHints =
        ["Virtual", "VPN", "TAP-", "WAN Miniport", "Hyper-V", "VMware", "VirtualBox", "Bluetooth Device", "Loopback", "Wintun", "ZeroTier", "Npcap"];

    private static List<NetworkAdapterInfo> CollectNetworks() =>
        Wmi.Query("SELECT Name, Manufacturer, MACAddress, Speed, NetConnectionStatus, PNPDeviceID FROM Win32_NetworkAdapter WHERE PhysicalAdapter = TRUE")
            .Where(r => !VirtualAdapterHints.Any(h => r.Str("Name").Contains(h, StringComparison.OrdinalIgnoreCase)))
            .Where(r => r.Str("PNPDeviceID").StartsWith("PCI", StringComparison.OrdinalIgnoreCase)
                        || r.Str("PNPDeviceID").StartsWith("USB", StringComparison.OrdinalIgnoreCase)
                        || r.Str("PNPDeviceID").StartsWith("SD", StringComparison.OrdinalIgnoreCase))
            .Select(r =>
            {
                var name = r.Str("Name");
                return new NetworkAdapterInfo
                {
                    Name = name,
                    Manufacturer = r.Str("Manufacturer"),
                    MacAddress = r.Str("MACAddress"),
                    SpeedBps = r.Long("Speed") is { } s && s < long.MaxValue / 2 ? s : 0,
                    Connected = r.Int("NetConnectionStatus") == 2,
                    IsWireless = new[] { "Wi-Fi", "Wireless", "WLAN", "802.11", "WiFi" }.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase)),
                };
            })
            .ToList();

    private static List<AudioDeviceInfo> CollectAudio() =>
        Wmi.Query("SELECT Name, Manufacturer, Status FROM Win32_SoundDevice")
            .Select(r => new AudioDeviceInfo { Name = r.Str("Name"), Manufacturer = r.Str("Manufacturer"), Status = r.Str("Status") })
            .ToList();

    private static BatteryInfo? CollectBattery()
    {
        var battery = Wmi.First("SELECT Name, EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery");
        if (battery is null) return null;

        var status = battery.Int("BatteryStatus");
        var info = new BatteryInfo
        {
            Name = battery.Str("Name"),
            ChargePercent = battery.Int("EstimatedChargeRemaining"),
            IsCharging = status is null ? null : status is 2 or 6 or 7 or 8 or 9,
        };

        info.DesignCapacity = Wmi.Query("SELECT DesignedCapacity FROM BatteryStaticData", Wmi.RootWmi).Sum(r => r.Long("DesignedCapacity") ?? 0);
        info.FullChargeCapacity = Wmi.Query("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", Wmi.RootWmi).Sum(r => r.Long("FullChargedCapacity") ?? 0);
        var cycles = Wmi.First("SELECT CycleCount FROM BatteryCycleCount", Wmi.RootWmi)?.Int("CycleCount");
        info.CycleCount = cycles is > 0 ? cycles : null;
        return info;
    }

    private static FirmwareInfo CollectFirmware()
    {
        var fw = new FirmwareInfo();
        try
        {
            if (NativeMethods.GetFirmwareType(out var type)) fw.IsUefi = type == 2;
        }
        catch (EntryPointNotFoundException)
        {
        }

        using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
        {
            if (key?.GetValue("UEFISecureBootEnabled") is int sb) fw.SecureBootEnabled = sb == 1;
            else if (fw.IsUefi == false) fw.SecureBootEnabled = false;
        }

        var tpm = Wmi.First("SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm", Wmi.Tpm, 10);
        if (tpm is not null)
        {
            fw.TpmPresent = true;
            fw.TpmEnabled = tpm.Bool("IsEnabled_InitialValue");
            var spec = tpm.Str("SpecVersion");
            fw.TpmVersion = spec.Split(',', StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        }
        else if (NativeMethods.IsAdministrator())
        {
            // Có quyền admin mà không thấy Win32_Tpm -> máy không có / đã tắt TPM.
            fw.TpmPresent = false;
        }

        return fw;
    }
}
