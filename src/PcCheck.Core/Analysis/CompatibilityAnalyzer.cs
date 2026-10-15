using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>Kiểm tra tương thích giữa các linh kiện và với Windows 11.</summary>
public static class CompatibilityAnalyzer
{
    private const long Gb = 1024L * 1024 * 1024;

    public static List<CompatibilityCheck> Analyze(ScanData data, DateTime now)
    {
        var checks = new List<CompatibilityCheck>();
        var hw = data.Hardware;

        AddMemoryChecks(hw, checks);
        AddWindows11Checks(hw, checks);
        AddSystemChecks(data, checks, now);
        AddDisplayChecks(hw, checks);
        return checks;
    }

    private static void AddMemoryChecks(HardwareInfo hw, List<CompatibilityCheck> checks)
    {
        var group = L("compat.group.memory");
        var modules = hw.Memory.Modules;
        var total = hw.Memory.InstalledBytes;

        checks.Add(new CompatibilityCheck
        {
            Id = "ram.capacity",
            Group = group,
            Title = L("compat.ram_capacity.title"),
            Status = total >= 8 * Gb - 512L * 1024 * 1024 ? CheckStatus.Pass : total >= 4 * Gb - 512L * 1024 * 1024 ? CheckStatus.Warning : CheckStatus.Fail,
            Detail = L("compat.ram_capacity.detail", new ByteSize(total)),
            Recommendation = total < 8 * Gb - 512L * 1024 * 1024 ? L("compat.ram_capacity.rec") : null,
        });

        if (modules.Count == 0) return;

        if (hw.Memory.SlotCount >= 2)
        {
            var single = modules.Count == 1;
            checks.Add(new CompatibilityCheck
            {
                Id = "ram.channel",
                Group = group,
                Title = L("compat.ram_channel.title"),
                Status = single ? CheckStatus.Warning : CheckStatus.Pass,
                Detail = single
                    ? L("compat.ram_channel.single", hw.Memory.SlotCount)
                    : L("compat.ram_channel.dual", modules.Count, hw.Memory.SlotCount),
                Recommendation = single ? L("compat.ram_channel.rec") : null,
            });
        }

        if (modules.Count >= 2)
        {
            var sizes = modules.Select(m => m.CapacityBytes).Distinct().Count();
            var speeds = modules.Select(m => m.SpeedMhz).Where(s => s > 0).Distinct().Count();
            var parts = modules.Select(m => m.PartNumber.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var mismatch = sizes > 1 || speeds > 1;
            checks.Add(new CompatibilityCheck
            {
                Id = "ram.matched",
                Group = group,
                Title = L("compat.ram_matched.title"),
                Status = mismatch ? CheckStatus.Warning : parts > 1 ? CheckStatus.Info : CheckStatus.Pass,
                Detail = mismatch
                    ? L("compat.ram_matched.mismatch", string.Join(" + ", modules.Select(m => $"{Util.Format.Bytes(m.CapacityBytes)} {m.SpeedMhz}MHz")))
                    : parts > 1 ? L("compat.ram_matched.different_brand") : L("compat.ram_matched.ok"),
                Recommendation = mismatch ? L("compat.ram_matched.rec") : null,
            });
        }

        var rated = modules.Where(m => m.SpeedMhz > 0).Select(m => m.SpeedMhz).DefaultIfEmpty(0).Min();
        var configured = modules.Where(m => m.ConfiguredSpeedMhz > 0).Select(m => m.ConfiguredSpeedMhz).DefaultIfEmpty(0).Min();
        if (rated > 0 && configured > 0)
        {
            var slow = configured < rated * 0.95;
            var laptop = hw.Computer.IsLaptop || modules.All(m => m.FormFactor == "SO-DIMM");
            checks.Add(new CompatibilityCheck
            {
                Id = "ram.speed",
                Group = group,
                Title = L("compat.ram_speed.title"),
                Status = slow ? (laptop ? CheckStatus.Info : CheckStatus.Warning) : CheckStatus.Pass,
                Detail = slow ? L("compat.ram_speed.slow", configured, rated) : L("compat.ram_speed.ok", configured),
                Recommendation = slow ? (laptop ? L("compat.ram_speed.rec_laptop") : L("compat.ram_speed.rec")) : null,
            });
        }

        if (hw.Memory.MaxCapacityBytes > 0 && hw.Memory.SlotCount > 0)
        {
            var freeSlots = Math.Max(0, hw.Memory.SlotCount - modules.Count);
            checks.Add(new CompatibilityCheck
            {
                Id = "ram.upgrade",
                Group = group,
                Title = L("compat.ram_upgrade.title"),
                Status = CheckStatus.Info,
                Detail = L("compat.ram_upgrade.detail", new ByteSize(hw.Memory.MaxCapacityBytes), hw.Memory.SlotCount, freeSlots, modules.FirstOrDefault()?.Type ?? "RAM"),
            });
        }
    }

    private static void AddWindows11Checks(HardwareInfo hw, List<CompatibilityCheck> checks)
    {
        var group = L("compat.group.win11");
        var onWin11 = hw.Os.IsWindows11;

        var cpu = hw.Cpus.FirstOrDefault();
        var cpuStatus = onWin11 ? CheckStatus.Pass : Windows11Cpu.Evaluate(cpu?.Name ?? "");
        checks.Add(new CompatibilityCheck
        {
            Id = "win11.cpu",
            Group = group,
            Title = L("compat.win11_cpu.title"),
            Status = cpuStatus,
            Detail = L(cpuStatus switch
            {
                CheckStatus.Pass => "compat.win11_cpu.pass",
                CheckStatus.Fail => "compat.win11_cpu.fail",
                _ => "compat.win11_cpu.unknown",
            }, cpu?.Name ?? "—"),
        });

        var fw = hw.Firmware;
        var tpmStatus = fw.TpmPresent switch
        {
            true when fw.TpmVersion.StartsWith('2') => CheckStatus.Pass,
            true => CheckStatus.Fail,
            false => CheckStatus.Fail,
            null => onWin11 ? CheckStatus.Pass : CheckStatus.Unknown,
        };
        checks.Add(new CompatibilityCheck
        {
            Id = "win11.tpm",
            Group = group,
            Title = L("compat.tpm.title"),
            Status = tpmStatus,
            Detail = fw.TpmPresent == true ? L("compat.tpm.version", fw.TpmVersion) : fw.TpmPresent == false ? L("compat.tpm.none") : L("compat.tpm.unknown"),
            Recommendation = tpmStatus == CheckStatus.Fail ? L("compat.tpm.rec") : null,
        });

        var uefiStatus = fw.IsUefi switch { true => CheckStatus.Pass, false => CheckStatus.Fail, null => CheckStatus.Unknown };
        checks.Add(new CompatibilityCheck
        {
            Id = "win11.uefi",
            Group = group,
            Title = L("compat.uefi.title"),
            Status = uefiStatus,
            Detail = fw.IsUefi == true ? L("compat.uefi.yes") : fw.IsUefi == false ? L("compat.uefi.legacy") : L("compat.unknown"),
            Recommendation = fw.IsUefi == false ? L("compat.uefi.rec") : null,
        });

        var sbStatus = fw.SecureBootEnabled switch
        {
            true => CheckStatus.Pass,
            false when fw.IsUefi == true => CheckStatus.Warning,
            false => CheckStatus.Fail,
            null => CheckStatus.Unknown,
        };
        checks.Add(new CompatibilityCheck
        {
            Id = "win11.secureboot",
            Group = group,
            Title = L("compat.secureboot.title"),
            Status = sbStatus,
            Detail = fw.SecureBootEnabled == true ? L("compat.secureboot.on") : fw.SecureBootEnabled == false ? L("compat.secureboot.off") : L("compat.unknown"),
            Recommendation = fw.SecureBootEnabled == false ? L("compat.secureboot.rec") : null,
        });

        var systemDisk = hw.Disks.FirstOrDefault(d => d.IsSystemDisk);
        var systemVolume = hw.Volumes.FirstOrDefault(v => v.IsSystem);
        var storageBytes = systemDisk?.SizeBytes ?? systemVolume?.TotalBytes ?? 0;
        checks.Add(new CompatibilityCheck
        {
            Id = "win11.storage",
            Group = group,
            Title = L("compat.win11_storage.title"),
            Status = storageBytes == 0 ? CheckStatus.Unknown : storageBytes >= 64_000_000_000 ? CheckStatus.Pass : CheckStatus.Fail,
            Detail = L("compat.win11_storage.detail", new ByteSize(storageBytes)),
        });

        if (!onWin11)
        {
            var statuses = checks.Where(c => c.Id.StartsWith("win11.", StringComparison.Ordinal)).Select(c => c.Status).ToList();
            var ramOk = hw.Memory.InstalledBytes >= 4 * Gb - 512L * 1024 * 1024;
            var ready = ramOk && statuses.All(s => s == CheckStatus.Pass);
            var blocked = !ramOk || statuses.Any(s => s == CheckStatus.Fail);
            checks.Add(new CompatibilityCheck
            {
                Id = "win11.summary",
                Group = group,
                Title = L("compat.win11_summary.title"),
                Status = ready ? CheckStatus.Pass : blocked ? CheckStatus.Fail : CheckStatus.Warning,
                Detail = ready ? L("compat.win11_summary.ready") : blocked ? L("compat.win11_summary.blocked") : L("compat.win11_summary.maybe"),
                Fixes = ready ? [FixCatalog.Windows11Download] : [],
            });
        }
    }

    private static void AddSystemChecks(ScanData data, List<CompatibilityCheck> checks, DateTime now)
    {
        var hw = data.Hardware;
        var group = L("compat.group.system");

        var cpu64 = hw.Cpus.FirstOrDefault()?.Is64BitCapable ?? true;
        if (!hw.Os.Is64Bit)
        {
            var bigRam = hw.Memory.InstalledBytes > 4 * Gb;
            checks.Add(new CompatibilityCheck
            {
                Id = "os.arch",
                Group = group,
                Title = L("compat.os_arch.title"),
                Status = bigRam ? CheckStatus.Fail : cpu64 ? CheckStatus.Warning : CheckStatus.Info,
                Detail = bigRam ? L("compat.os_arch.ram_wasted", new ByteSize(hw.Memory.InstalledBytes)) : L("compat.os_arch.32bit"),
                Recommendation = cpu64 ? L("compat.os_arch.rec") : null,
            });
        }
        else
        {
            checks.Add(new CompatibilityCheck
            {
                Id = "os.arch",
                Group = group,
                Title = L("compat.os_arch.title"),
                Status = CheckStatus.Pass,
                Detail = L("compat.os_arch.64bit"),
            });
        }

        var systemDisk = hw.Disks.FirstOrDefault(d => d.IsSystemDisk);
        if (systemDisk is not null)
        {
            var hdd = systemDisk.MediaType == DiskMediaType.Hdd && !systemDisk.IsNvme;
            checks.Add(new CompatibilityCheck
            {
                Id = "storage.system_ssd",
                Group = group,
                Title = L("compat.system_ssd.title"),
                Status = hdd ? CheckStatus.Warning : systemDisk.MediaType == DiskMediaType.Unknown && !systemDisk.IsNvme ? CheckStatus.Unknown : CheckStatus.Pass,
                Detail = hdd ? L("compat.system_ssd.hdd", systemDisk.Model) : L("compat.system_ssd.ssd", systemDisk.Model, systemDisk.BusType),
                Recommendation = hdd ? L("compat.system_ssd.rec") : null,
            });

            if (systemDisk.IsUsb)
            {
                checks.Add(new CompatibilityCheck
                {
                    Id = "storage.system_usb",
                    Group = group,
                    Title = L("compat.system_usb.title"),
                    Status = CheckStatus.Warning,
                    Detail = L("compat.system_usb.detail"),
                });
            }
        }

        var cpu = hw.Cpus.FirstOrDefault();
        if (cpu?.VirtualizationEnabled is { } virt)
        {
            var on = virt || hw.Computer.HypervisorPresent;
            checks.Add(new CompatibilityCheck
            {
                Id = "cpu.virtualization",
                Group = group,
                Title = L("compat.virtualization.title"),
                Status = on ? CheckStatus.Pass : CheckStatus.Info,
                Detail = on ? L("compat.virtualization.on") : L("compat.virtualization.off"),
                Recommendation = on ? null : L("compat.virtualization.rec"),
            });
        }

        var activeAv = data.System.Antivirus.Where(a => a.Enabled && !a.IsDefender).ToList();
        if (activeAv.Count >= 2)
        {
            checks.Add(new CompatibilityCheck
            {
                Id = "security.multi_av",
                Group = group,
                Title = L("compat.multi_av.title"),
                Status = CheckStatus.Warning,
                Detail = L("compat.multi_av.detail", string.Join(", ", activeAv.Select(a => a.Name))),
                Recommendation = L("compat.multi_av.rec"),
                Fixes = [FixCatalog.SoftwarePage],
            });
        }

        if (hw.Board.BiosDate is { } biosDate && biosDate.Year > 2000)
        {
            var old = biosDate < now.AddYears(-Thresholds.BiosOldYears);
            var oem = VendorSupport.OemDriverPage(hw.Computer.IsLaptop ? hw.Computer.Manufacturer : hw.Board.Manufacturer);
            checks.Add(new CompatibilityCheck
            {
                Id = "bios.age",
                Group = group,
                Title = L("compat.bios.title"),
                Status = old ? CheckStatus.Info : CheckStatus.Pass,
                Detail = L("compat.bios.detail", hw.Board.BiosVersion, biosDate),
                Recommendation = old ? L("compat.bios.rec") : null,
                Fixes = old && oem is not null ? [FixCatalog.VendorDrivers(oem)] : [],
            });
        }

        var basicGpu = hw.Gpus.Any(g => g.IsBasicDriver);
        if (hw.Gpus.Count > 0)
        {
            checks.Add(new CompatibilityCheck
            {
                Id = "gpu.driver",
                Group = group,
                Title = L("compat.gpu_driver.title"),
                Status = basicGpu ? CheckStatus.Fail : CheckStatus.Pass,
                Detail = basicGpu ? L("compat.gpu_driver.basic") : L("compat.gpu_driver.ok", string.Join(", ", hw.Gpus.Where(g => !g.IsVirtual).Select(g => g.Name))),
                Fixes = basicGpu ? [FixCatalog.DriversPage] : [],
            });
        }
    }

    private static void AddDisplayChecks(HardwareInfo hw, List<CompatibilityCheck> checks)
    {
        var group = L("compat.group.display");
        foreach (var monitor in hw.Monitors.Where(m => m.Width > 0))
        {
            var name = string.IsNullOrWhiteSpace(monitor.Name) ? monitor.DeviceName : monitor.Name;
            var lowRefresh = monitor.MaxRefreshAtCurrent > monitor.RefreshRate + 5;
            checks.Add(new CompatibilityCheck
            {
                Id = "display.refresh." + monitor.DeviceName,
                Group = group,
                Title = L("compat.refresh.title", name),
                Status = lowRefresh ? CheckStatus.Warning : CheckStatus.Pass,
                Detail = lowRefresh
                    ? L("compat.refresh.low", monitor.RefreshRate, monitor.MaxRefreshAtCurrent)
                    : L("compat.refresh.ok", monitor.RefreshRate),
                Recommendation = lowRefresh ? L("compat.refresh.rec") : null,
                Fixes = lowRefresh ? [FixCatalog.DisplaySettings] : [],
            });

            var lowRes = (long)monitor.MaxWidth * monitor.MaxHeight > (long)monitor.Width * monitor.Height;
            checks.Add(new CompatibilityCheck
            {
                Id = "display.resolution." + monitor.DeviceName,
                Group = group,
                Title = L("compat.resolution.title", name),
                Status = lowRes ? CheckStatus.Info : CheckStatus.Pass,
                Detail = lowRes
                    ? L("compat.resolution.low", monitor.Width, monitor.Height, monitor.MaxWidth, monitor.MaxHeight)
                    : L("compat.resolution.ok", monitor.Width, monitor.Height),
                Fixes = lowRes ? [FixCatalog.DisplaySettings] : [],
            });
        }
    }

    /// <summary>Chuyển các mục tương thích Cảnh báo / Không đạt thành Issue để tính điểm và hiển thị ở trang Lỗi.</summary>
    public static IEnumerable<Issue> ToIssues(IEnumerable<CompatibilityCheck> checks, bool isWindows11)
    {
        foreach (var check in checks)
        {
            if (check.Status is not (CheckStatus.Warning or CheckStatus.Fail)) continue;

            // Chưa lên Windows 11 thì từng mục yêu cầu Win11 chỉ là thông tin, tóm tắt mới đáng kể.
            if (check.Id.StartsWith("win11.", StringComparison.Ordinal) && check.Id != "win11.summary") continue;
            if (check.Id == "win11.summary" && isWindows11) continue;

            if (check.Id == "gpu.driver") continue; // Đã có Issue riêng ở phần driver.

            var severity = check.Status == CheckStatus.Fail ? Severity.Warning : Severity.Info;
            if (check.Id is "ram.channel" or "ram.matched" or "ram.speed" or "storage.system_ssd" or "security.multi_av")
                severity = Severity.Warning;
            if (check.Id == "win11.summary") severity = Severity.Info;

            yield return new Issue
            {
                Id = "compat." + check.Id,
                Category = IssueCategory.Compatibility,
                Severity = severity,
                Title = check.Title,
                Detail = check.Detail,
                Recommendation = check.Recommendation,
                Fixes = check.Fixes.Count > 0 ? check.Fixes : [FixCatalog.CompatibilityPage],
            };
        }
    }
}
