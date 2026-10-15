using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>Nhiệt độ, sức khỏe ổ cứng, pin, RAM và dung lượng trống.</summary>
public static class HardwareAnalyzer
{
    public static IEnumerable<Issue> Analyze(ScanData data)
    {
        var hw = data.Hardware;
        var sensors = data.Sensors;

        // --- Nhiệt độ CPU ---
        var cpuTemp = sensors.CpuTemperature;
        var cpuLoad = sensors.CpuLoad;
        var estimated = sensors.CpuTemperatureEstimated;
        var cpuSeverity = Thresholds.TemperatureSeverity(cpuTemp, Thresholds.CpuTempWarn, Thresholds.CpuTempCritical);

        // Nhiệt độ ước lượng từ ACPI kém chính xác: tối đa chỉ cảnh báo, không kết luận "nghiêm trọng".
        if (estimated && cpuSeverity == Severity.Critical) cpuSeverity = Severity.Warning;
        if (cpuSeverity >= Severity.Warning)
        {
            yield return new Issue
            {
                Id = "temp/cpu",
                Category = IssueCategory.Temperature,
                Severity = cpuSeverity,
                Title = L("issue.cpu_hot.title", Math.Round(cpuTemp!.Value)),
                Detail = estimated
                    ? L("issue.cpu_hot.detail_estimated", Math.Round(cpuTemp.Value), Math.Round(cpuLoad ?? 0))
                    : L("issue.cpu_hot.detail", Math.Round(cpuTemp.Value), Math.Round(cpuLoad ?? 0)),
                Recommendation = L("issue.cpu_hot.rec"),
                Fixes = [FixCatalog.HealthPage, FixCatalog.PowerOptions],
            };
        }
        else if (!estimated && cpuTemp >= Thresholds.CpuIdleTempWarn && cpuLoad is < 25)
        {
            yield return new Issue
            {
                Id = "temp/cpu_idle",
                Category = IssueCategory.Temperature,
                Severity = Severity.Warning,
                Title = L("issue.cpu_idle_hot.title", Math.Round(cpuTemp.Value)),
                Detail = L("issue.cpu_idle_hot.detail", Math.Round(cpuTemp.Value), Math.Round(cpuLoad.Value)),
                Recommendation = L("issue.cpu_hot.rec"),
                Fixes = [FixCatalog.HealthPage],
            };
        }

        if (!sensors.HasCpuSensor && sensors.PawnIoInstalled == false)
        {
            yield return new Issue
            {
                Id = "temp/pawnio",
                Category = IssueCategory.Temperature,
                Severity = Severity.Info,
                Title = estimated ? L("issue.pawnio.title_estimated") : L("issue.pawnio.title"),
                Detail = estimated ? L("issue.pawnio.detail_estimated", Math.Round(cpuTemp!.Value)) : L("issue.pawnio.detail"),
                Recommendation = L("issue.pawnio.rec"),
                Fixes = [FixCatalog.EnableCpuSensor],
            };
        }

        // --- Nhiệt độ GPU ---
        var gpuTemp = sensors.GpuTemperature;
        var gpuSeverity = Thresholds.TemperatureSeverity(gpuTemp, Thresholds.GpuTempWarn, Thresholds.GpuTempCritical);
        if (gpuSeverity >= Severity.Warning)
        {
            yield return new Issue
            {
                Id = "temp/gpu",
                Category = IssueCategory.Temperature,
                Severity = gpuSeverity,
                Title = L("issue.gpu_hot.title", Math.Round(gpuTemp!.Value)),
                Detail = L("issue.gpu_hot.detail", Math.Round(gpuTemp.Value)),
                Recommendation = L("issue.gpu_hot.rec"),
                Fixes = [FixCatalog.HealthPage],
            };
        }

        // --- Ổ cứng ---
        foreach (var disk in hw.Disks)
        {
            foreach (var issue in AnalyzeDisk(disk)) yield return issue;
        }

        // --- Dung lượng trống ---
        foreach (var volume in hw.Volumes)
        {
            var issue = AnalyzeVolume(volume);
            if (issue is not null) yield return issue;
        }

        // --- Pin ---
        if (hw.Battery?.WearPercent is { } wear && wear >= Thresholds.BatteryWearInfo)
        {
            yield return new Issue
            {
                Id = "battery/wear",
                Category = IssueCategory.Hardware,
                Severity = wear >= Thresholds.BatteryWearWarn ? Severity.Warning : Severity.Info,
                Title = L("issue.battery_wear.title", Math.Round(wear)),
                Detail = L("issue.battery_wear.detail", hw.Battery.DesignCapacity, hw.Battery.FullChargeCapacity, hw.Battery.CycleCount?.ToString() ?? "—"),
                Recommendation = wear >= Thresholds.BatteryWearWarn ? L("issue.battery_wear.rec_replace") : L("issue.battery_wear.rec_care"),
                Fixes = [FixCatalog.Tool(SystemTools.BatteryReport, "tool.battery-report.name")],
            };
        }

        // --- RAM ---
        var ramUsed = hw.Memory.UsedPercent;
        if (ramUsed >= Thresholds.RamUsageInfo)
        {
            yield return new Issue
            {
                Id = "ram/usage",
                Category = IssueCategory.Hardware,
                Severity = ramUsed >= Thresholds.RamUsageWarn ? Severity.Warning : Severity.Info,
                Title = L("issue.ram_usage.title", Math.Round(ramUsed)),
                Detail = L("issue.ram_usage.detail", new ByteSize(hw.Memory.TotalBytes - hw.Memory.AvailableBytes), new ByteSize(hw.Memory.TotalBytes)),
                Recommendation = L("issue.ram_usage.rec"),
                Fixes = [FixCatalog.StartupPage],
            };
        }

        // --- GPU báo lỗi ---
        foreach (var gpu in hw.Gpus.Where(g => g.ErrorCode is not 0 and not DeviceErrorCodes.Disabled && !g.IsVirtual))
        {
            yield return new Issue
            {
                Id = "gpu/error/" + gpu.PnpDeviceId,
                Category = IssueCategory.Hardware,
                Severity = Severity.Critical,
                Title = L("issue.gpu_error.title", gpu.Name),
                Detail = L("issue.driver_error.detail", DeviceErrorCodes.Describe(gpu.ErrorCode), gpu.ErrorCode, gpu.PnpDeviceId),
                Recommendation = L("issue.gpu_error.rec"),
                Fixes = [FixCatalog.DriversPage, FixCatalog.DeviceManager],
            };
        }
    }

    internal static IEnumerable<Issue> AnalyzeDisk(DiskInfo disk)
    {
        var name = string.IsNullOrWhiteSpace(disk.Model) ? L("common.disk_number", disk.Number).ToString() : disk.Model;
        var driveHint = disk.DriveLetters.FirstOrDefault() ?? "C:";

        if (disk.Health == DiskHealth.Unhealthy || disk.PredictFailure == true)
        {
            yield return new Issue
            {
                Id = "disk/failing/" + disk.Number,
                Category = IssueCategory.Storage,
                Severity = Severity.Critical,
                Title = L("issue.disk_failing.title", name),
                Detail = L("issue.disk_failing.detail", name),
                Recommendation = L("issue.disk_failing.rec"),
                Fixes = [FixCatalog.BackupSettings, FixCatalog.HealthPage],
            };
        }
        else if (disk.Health == DiskHealth.Warning)
        {
            yield return new Issue
            {
                Id = "disk/warning/" + disk.Number,
                Category = IssueCategory.Storage,
                Severity = Severity.Warning,
                Title = L("issue.disk_warning.title", name),
                Detail = L("issue.disk_warning.detail", name),
                Recommendation = L("issue.disk_failing.rec"),
                Fixes = [FixCatalog.BackupSettings, FixCatalog.Chkdsk(driveHint)],
            };
        }

        if (disk.WearPercent is { } wear && disk.IsSolidState && wear >= Thresholds.SsdWearWarn)
        {
            yield return new Issue
            {
                Id = "disk/wear/" + disk.Number,
                Category = IssueCategory.Storage,
                Severity = wear >= Thresholds.SsdWearCritical ? Severity.Critical : Severity.Warning,
                Title = L("issue.ssd_wear.title", name, wear),
                Detail = L("issue.ssd_wear.detail", wear, 100 - wear),
                Recommendation = L("issue.ssd_wear.rec"),
                Fixes = [FixCatalog.BackupSettings],
            };
        }

        var uncorrected = (disk.ReadErrorsUncorrected ?? 0) + (disk.WriteErrorsUncorrected ?? 0);
        if (uncorrected > 0)
        {
            yield return new Issue
            {
                Id = "disk/errors/" + disk.Number,
                Category = IssueCategory.Storage,
                Severity = Severity.Warning,
                Title = L("issue.disk_errors.title", name),
                Detail = L("issue.disk_errors.detail", uncorrected),
                Recommendation = L("issue.disk_errors.rec"),
                Fixes = [FixCatalog.Chkdsk(driveHint), FixCatalog.BackupSettings],
            };
        }

        var (warn, critical) = disk.IsSolidState
            ? (Thresholds.SsdTempWarn, Thresholds.SsdTempCritical)
            : (Thresholds.HddTempWarn, Thresholds.HddTempCritical);
        var tempSeverity = Thresholds.TemperatureSeverity(disk.TemperatureC, warn, critical);
        if (tempSeverity >= Severity.Warning)
        {
            yield return new Issue
            {
                Id = "disk/temp/" + disk.Number,
                Category = IssueCategory.Temperature,
                Severity = tempSeverity,
                Title = L("issue.disk_hot.title", name, disk.TemperatureC),
                Detail = L("issue.disk_hot.detail", disk.TemperatureC, warn),
                Recommendation = L("issue.disk_hot.rec"),
                Fixes = [FixCatalog.HealthPage],
            };
        }
    }

    internal static Issue? AnalyzeVolume(VolumeInfo volume)
    {
        if (volume.TotalBytes <= 0) return null;
        Severity severity;
        if (volume.IsSystem)
        {
            severity = volume.FreePercent < Thresholds.SystemFreePercentCritical || volume.FreeBytes < Thresholds.SystemFreeBytesCritical
                ? Severity.Critical
                : volume.FreePercent < Thresholds.SystemFreePercentWarn || volume.FreeBytes < Thresholds.SystemFreeBytesWarn
                    ? Severity.Warning
                    : Severity.Ok;
        }
        else
        {
            severity = volume.FreePercent < Thresholds.DataFreePercentWarn ? Severity.Warning : Severity.Ok;
        }

        if (severity == Severity.Ok) return null;
        return new Issue
        {
            Id = "volume/space/" + volume.Letter,
            Category = IssueCategory.Storage,
            Severity = severity,
            Title = L("issue.low_space.title", volume.Letter, new ByteSize(volume.FreeBytes)),
            Detail = L("issue.low_space.detail", volume.Letter, new ByteSize(volume.FreeBytes), new ByteSize(volume.TotalBytes), Math.Round(volume.FreePercent)),
            Recommendation = volume.IsSystem ? L("issue.low_space.rec_system") : L("issue.low_space.rec_data"),
            Fixes = volume.IsSystem ? [FixCatalog.CleanupPage, FixCatalog.StorageSense, FixCatalog.SoftwarePage] : [FixCatalog.StorageSense],
        };
    }
}
