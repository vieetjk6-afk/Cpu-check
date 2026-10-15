using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>Lỗi hệ thống (màn hình xanh, tắt đột ngột, lỗi phần cứng/ổ đĩa), bảo mật, cập nhật, kích hoạt.</summary>
public static class SystemAnalyzer
{
    public static IEnumerable<Issue> Analyze(ScanData data, DateTime now)
    {
        foreach (var issue in AnalyzeEvents(data)) yield return issue;
        foreach (var issue in AnalyzeStatus(data, now)) yield return issue;
    }

    private static IEnumerable<Issue> AnalyzeEvents(ScanData data)
    {
        var ev = data.Events;
        var days = ev.Days;

        // --- Màn hình xanh ---
        var bsods = ev.Of(EventKind.BlueScreen).ToList();
        var bsodCount = Math.Max(bsods.Count, ev.MinidumpCount);
        if (bsodCount > 0)
        {
            var last = bsods.FirstOrDefault();
            var bug = BugChecks.Lookup(last?.Detail);
            var codes = bsods.Select(b => b.Detail).Where(d => d is not null).Distinct().Take(3).ToList();
            var hint = bug?.Hint ?? BugChecks.HintSystem;
            var fixes = new List<FixAction> { FixCatalog.SfcDism };
            if (hint == BugChecks.HintRam) fixes.Insert(0, FixCatalog.MemoryDiagnostic);
            if (hint is BugChecks.HintDriver or BugChecks.HintGpu) fixes.Insert(0, FixCatalog.DriversPage);
            if (hint == BugChecks.HintDisk) fixes.Insert(0, FixCatalog.Chkdsk("C:"));
            fixes.Add(FixCatalog.ReliabilityMonitor);

            yield return new Issue
            {
                Id = "events.bsod",
                Category = IssueCategory.System,
                Severity = bsodCount >= 3 ? Severity.Critical : Severity.Warning,
                Title = L("issue.bsod.title", bsodCount, days),
                Detail = codes.Count > 0
                    ? L("issue.bsod.detail", string.Join(", ", codes), bug?.Name ?? "—", last!.Time)
                    : L("issue.bsod.detail_dump", ev.LastMinidump),
                Recommendation = L("bugcheck.hint." + hint),
                Fixes = fixes,
            };
        }

        // --- Tắt máy đột ngột (Kernel-Power 41 có BugcheckCode = 0 -> mất nguồn / treo cứng) ---
        var powerLoss = ev.Of(EventKind.UnexpectedShutdown).Count(e => e.Detail is null or "0");
        if (powerLoss >= 2)
        {
            yield return new Issue
            {
                Id = "events.power_loss",
                Category = IssueCategory.System,
                Severity = powerLoss >= 5 ? Severity.Critical : Severity.Warning,
                Title = L("issue.power_loss.title", powerLoss, days),
                Detail = L("issue.power_loss.detail"),
                Recommendation = data.Hardware.Computer.IsLaptop ? L("issue.power_loss.rec_laptop") : L("issue.power_loss.rec_desktop"),
                Fixes = [FixCatalog.HealthPage, FixCatalog.ReliabilityMonitor],
            };
        }

        // --- Lỗi phần cứng WHEA ---
        var fatal = ev.Count(EventKind.HardwareErrorFatal);
        var corrected = ev.Count(EventKind.HardwareError);
        if (fatal > 0 || corrected >= 10)
        {
            yield return new Issue
            {
                Id = "events.whea",
                Category = IssueCategory.Hardware,
                Severity = fatal > 0 ? Severity.Critical : Severity.Warning,
                Title = fatal > 0 ? L("issue.whea_fatal.title", fatal) : L("issue.whea.title", corrected),
                Detail = L("issue.whea.detail", fatal, corrected, days),
                Recommendation = L("issue.whea.rec"),
                Fixes = [FixCatalog.MemoryDiagnostic, FixCatalog.HealthPage],
            };
        }

        // --- Lỗi ổ cứng ---
        var diskErrors = ev.Count(EventKind.DiskError);
        if (diskErrors > 0)
        {
            yield return new Issue
            {
                Id = "events.disk",
                Category = IssueCategory.Storage,
                Severity = diskErrors >= 5 ? Severity.Critical : Severity.Warning,
                Title = L("issue.disk_events.title", diskErrors, days),
                Detail = L("issue.disk_events.detail", ev.Of(EventKind.DiskError).First().Detail ?? "—"),
                Recommendation = L("issue.disk_events.rec"),
                Fixes = [FixCatalog.BackupSettings, FixCatalog.Chkdsk("C:"), FixCatalog.HealthPage],
            };
        }

        var resets = ev.Count(EventKind.StorageReset);
        if (resets >= 3)
        {
            yield return new Issue
            {
                Id = "events.storage_reset",
                Category = IssueCategory.Storage,
                Severity = Severity.Warning,
                Title = L("issue.storage_reset.title", resets, days),
                Detail = L("issue.storage_reset.detail"),
                Recommendation = L("issue.storage_reset.rec"),
                Fixes = [FixCatalog.DriversPage, FixCatalog.HealthPage],
            };
        }

        var fsErrors = ev.Count(EventKind.FileSystemError);
        if (fsErrors > 0)
        {
            yield return new Issue
            {
                Id = "events.ntfs",
                Category = IssueCategory.Storage,
                Severity = Severity.Warning,
                Title = L("issue.ntfs.title", fsErrors),
                Detail = L("issue.ntfs.detail"),
                Recommendation = L("issue.ntfs.rec"),
                Fixes = [FixCatalog.Chkdsk("C:")],
            };
        }

        // --- Driver card màn hình bị treo (TDR) ---
        var tdr = ev.Count(EventKind.GpuDriverCrash);
        if (tdr >= 2)
        {
            yield return new Issue
            {
                Id = "events.tdr",
                Category = IssueCategory.Driver,
                Severity = tdr >= 5 ? Severity.Critical : Severity.Warning,
                Title = L("issue.tdr.title", tdr, days),
                Detail = L("issue.tdr.detail"),
                Recommendation = L("issue.tdr.rec"),
                Fixes = [FixCatalog.DriversPage, FixCatalog.HealthPage],
            };
        }

        var lowMem = ev.Count(EventKind.LowMemory);
        if (lowMem > 0)
        {
            yield return new Issue
            {
                Id = "events.low_memory",
                Category = IssueCategory.Hardware,
                Severity = Severity.Warning,
                Title = L("issue.low_memory.title", lowMem),
                Detail = L("issue.low_memory.detail"),
                Recommendation = L("issue.low_memory.rec"),
                Fixes = [FixCatalog.PerformanceOptions, FixCatalog.StartupPage],
            };
        }

        // --- Ứng dụng hay bị crash ---
        var crashers = ev.Of(EventKind.AppCrash).Concat(ev.Of(EventKind.AppHang))
            .Where(e => !string.IsNullOrWhiteSpace(e.Detail))
            .GroupBy(e => e.Detail!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (App: g.Key, Count: g.Count()))
            .Where(x => x.Count >= 3)
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToList();
        if (crashers.Count > 0)
        {
            yield return new Issue
            {
                Id = "events.app_crash",
                Category = IssueCategory.Software,
                Severity = crashers[0].Count >= 10 ? Severity.Warning : Severity.Info,
                Title = L("issue.app_crash.title", crashers.Count),
                Detail = L("issue.app_crash.detail", string.Join(", ", crashers.Select(c => $"{c.App} ({c.Count})")), days),
                Recommendation = L("issue.app_crash.rec"),
                Fixes = [FixCatalog.ReliabilityMonitor, FixCatalog.SfcDism],
            };
        }
    }

    private static IEnumerable<Issue> AnalyzeStatus(ScanData data, DateTime now)
    {
        var s = data.System;
        var os = data.Hardware.Os;

        if (!s.IsAdmin)
        {
            yield return new Issue
            {
                Id = "system.not_admin",
                Category = IssueCategory.System,
                Severity = Severity.Info,
                Title = L("issue.not_admin.title"),
                Detail = L("issue.not_admin.detail"),
            };
        }

        // --- Phiên bản Windows hết hỗ trợ ---
        var end = WindowsLifecycle.EndOfSupport(os.Build, os.Edition);
        if (end is not null && end < now)
        {
            var win10 = !os.IsWindows11;
            yield return new Issue
            {
                Id = "system.eos",
                Category = IssueCategory.Security,
                Severity = Severity.Warning,
                Title = win10 ? L("issue.win10_eos.title") : L("issue.win_version_eos.title", os.DisplayVersion),
                Detail = win10 ? L("issue.win10_eos.detail", end.Value) : L("issue.win_version_eos.detail", os.DisplayVersion, end.Value, WindowsLifecycle.Latest.Version),
                Recommendation = win10 ? L("issue.win10_eos.rec") : L("issue.win_version_eos.rec"),
                Fixes = win10 ? [FixCatalog.CompatibilityPage, FixCatalog.WindowsUpdate] : [FixCatalog.WindowsUpdate],
            };
        }

        if (s.PendingReboot)
        {
            yield return new Issue
            {
                Id = "system.pending_reboot",
                Category = IssueCategory.System,
                Severity = Severity.Warning,
                Title = L("issue.pending_reboot.title"),
                Detail = L("issue.pending_reboot.detail"),
                Fixes = [FixCatalog.Restart],
            };
        }

        if (s.Uptime.TotalDays >= Thresholds.UptimeDaysInfo)
        {
            yield return new Issue
            {
                Id = "system.uptime",
                Category = IssueCategory.System,
                Severity = Severity.Info,
                Title = L("issue.uptime.title", (int)s.Uptime.TotalDays),
                Detail = s.FastStartupEnabled == true ? L("issue.uptime.detail_fast") : L("issue.uptime.detail"),
                Fixes = [FixCatalog.Restart],
            };
        }

        // --- Diệt virus ---
        var enabledAv = s.Antivirus.Where(a => a.Enabled).ToList();
        var defenderOn = s.DefenderRealtime == true;
        if (s.Antivirus.Count > 0 && enabledAv.Count == 0 && !defenderOn)
        {
            yield return new Issue
            {
                Id = "security.no_av",
                Category = IssueCategory.Security,
                Severity = Severity.Critical,
                Title = L("issue.no_av.title"),
                Detail = L("issue.no_av.detail"),
                Recommendation = L("issue.no_av.rec"),
                Fixes = [FixCatalog.WindowsSecurity],
            };
        }
        else
        {
            var outdated = enabledAv.Where(a => !a.UpToDate).Select(a => a.Name).ToList();
            if (defenderOn && s.DefenderSignatureAgeDays > Thresholds.DefenderSignatureDaysWarn && !outdated.Any(n => n.Contains("Defender", StringComparison.OrdinalIgnoreCase)))
                outdated.Add("Microsoft Defender");
            if (outdated.Count > 0)
            {
                yield return new Issue
                {
                    Id = "security.av_outdated",
                    Category = IssueCategory.Security,
                    Severity = Severity.Warning,
                    Title = L("issue.av_outdated.title"),
                    Detail = L("issue.av_outdated.detail", string.Join(", ", outdated)),
                    Fixes = [FixCatalog.WindowsSecurity, FixCatalog.WindowsUpdate],
                };
            }
        }

        if (s.FirewallKnown && s.FirewallDisabledProfiles.Count > 0 && !s.ThirdPartyFirewall)
        {
            yield return new Issue
            {
                Id = "security.firewall",
                Category = IssueCategory.Security,
                Severity = s.FirewallDisabledProfiles.Contains("Public") ? Severity.Warning : Severity.Info,
                Title = L("issue.firewall.title"),
                Detail = L("issue.firewall.detail", string.Join(", ", s.FirewallDisabledProfiles)),
                Fixes = [FixCatalog.FirewallSettings],
            };
        }

        if (s.UacEnabled == false)
        {
            yield return new Issue
            {
                Id = "security.uac",
                Category = IssueCategory.Security,
                Severity = Severity.Warning,
                Title = L("issue.uac.title"),
                Detail = L("issue.uac.detail"),
                Fixes = [FixCatalog.UacSettings],
            };
        }

        if (s.LicenseStatus is not null and not 1)
        {
            yield return new Issue
            {
                Id = "system.activation",
                Category = IssueCategory.System,
                Severity = Severity.Warning,
                Title = L("issue.activation.title"),
                Detail = L("issue.activation.detail"),
                Fixes = [FixCatalog.Activation],
            };
        }

        if (s.LastUpdateInstalled is { } last && (now - last).TotalDays > Thresholds.UpdateDaysWarn)
        {
            yield return new Issue
            {
                Id = "system.updates",
                Category = IssueCategory.Security,
                Severity = Severity.Warning,
                Title = L("issue.updates.title", (int)(now - last).TotalDays),
                Detail = L("issue.updates.detail", last),
                Fixes = [FixCatalog.WindowsUpdate, FixCatalog.Tool(SystemTools.WindowsUpdateReset, "tool.wu-reset.name", confirm: true)],
            };
        }

        if (s.PageFileEnabled == false && data.Hardware.Memory.TotalBytes < 16L * 1024 * 1024 * 1024)
        {
            yield return new Issue
            {
                Id = "system.pagefile",
                Category = IssueCategory.System,
                Severity = Severity.Warning,
                Title = L("issue.pagefile.title"),
                Detail = L("issue.pagefile.detail", new ByteSize(data.Hardware.Memory.TotalBytes)),
                Fixes = [FixCatalog.PerformanceOptions],
            };
        }

        if (s.WindowsOldPresent)
        {
            yield return new Issue
            {
                Id = "system.windows_old",
                Category = IssueCategory.Junk,
                Severity = Severity.Info,
                Title = L("issue.windows_old.title"),
                Detail = L("issue.windows_old.detail"),
                Fixes = [FixCatalog.StorageSense, FixCatalog.DiskCleanup],
            };
        }
    }
}
