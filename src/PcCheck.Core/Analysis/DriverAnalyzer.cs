using PcCheck.Core.Fixes;
using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>Phát hiện driver chưa cài, lỗi, dùng driver chung, không chữ ký, quá cũ.</summary>
public static class DriverAnalyzer
{
    /// <summary>Lớp thiết bị đáng kiểm tra tuổi driver (System/chipset thường là INF cũ nhưng vẫn ổn nên bỏ qua).</summary>
    private static readonly HashSet<string> AgeCheckedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Display", "Net", "MEDIA", "Bluetooth", "HDC", "SCSIAdapter", "Camera", "Image",
    };

    private static readonly HashSet<string> CriticalClasses = new(StringComparer.OrdinalIgnoreCase) { "Display", "Net", "HDC", "SCSIAdapter" };

    public static List<DriverProblem> FindProblems(IEnumerable<DeviceInfo> devices, DateTime now)
    {
        var problems = new List<DriverProblem>();
        foreach (var device in devices)
        {
            var problem = Classify(device, now);
            if (problem is not null) problems.Add(problem);
        }

        return problems
            .OrderByDescending(p => p.Severity)
            .ThenBy(p => p.Kind)
            .ThenBy(p => p.Device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal static DriverProblem? Classify(DeviceInfo d, DateTime now)
    {
        if (d.ErrorCode == DeviceErrorCodes.NotConnected) return null;
        if (d.ErrorCode == DeviceErrorCodes.Disabled) return new DriverProblem(d, DriverProblemKind.Disabled, Severity.Info);

        var critical = CriticalClasses.Contains(d.ClassName);
        if (d.ErrorCode != 0)
        {
            var missing = DeviceErrorCodes.MeansMissingDriver(d.ErrorCode) || !d.HasDriver;
            return new DriverProblem(d, missing ? DriverProblemKind.Missing : DriverProblemKind.DeviceError, critical ? Severity.Critical : Severity.Warning);
        }

        if (IsGenericDisplay(d)) return new DriverProblem(d, DriverProblemKind.Generic, Severity.Critical);
        if (IsGenericAudio(d)) return new DriverProblem(d, DriverProblemKind.Generic, Severity.Info);
        if (d.IsSigned == false) return new DriverProblem(d, DriverProblemKind.Unsigned, Severity.Warning);
        if (IsOutdated(d, now)) return new DriverProblem(d, DriverProblemKind.Outdated, Severity.Info);
        return null;
    }

    internal static bool IsGenericDisplay(DeviceInfo d) =>
        d.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase)
        && (d.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)
            || d.Name.Contains("Standard VGA", StringComparison.OrdinalIgnoreCase)
            || d.Service.Equals("BasicDisplay", StringComparison.OrdinalIgnoreCase));

    internal static bool IsGenericAudio(DeviceInfo d) =>
        d.ClassName.Equals("MEDIA", StringComparison.OrdinalIgnoreCase)
        && d.Name.Equals("High Definition Audio Device", StringComparison.OrdinalIgnoreCase)
        && d.Service.Equals("HdAudio", StringComparison.OrdinalIgnoreCase);

    internal static bool IsOutdated(DeviceInfo d, DateTime now)
    {
        if (!AgeCheckedClasses.Contains(d.ClassName) || d.IsMicrosoftDriver || d.DriverDate is null) return false;
        if (d.DriverDate.Value.Year < 2005) return false; // INF chung có ngày giả (1968, 2006...).
        var years = d.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase) ? Thresholds.GpuDriverOldYears : Thresholds.DriverOldYears;
        return d.DriverDate.Value < now.AddYears(-years);
    }

    public static IEnumerable<Issue> Analyze(ScanData data, IReadOnlyList<DriverProblem> problems, DateTime now)
    {
        var oemPage = VendorSupport.OemDriverPage(data.Hardware.Computer.IsLaptop ? data.Hardware.Computer.Manufacturer : data.Hardware.Board.Manufacturer)
                      ?? VendorSupport.OemDriverPage(data.Hardware.Computer.Manufacturer);

        foreach (var p in problems.Where(p => p.Kind is DriverProblemKind.Missing or DriverProblemKind.DeviceError).Take(15))
        {
            var fixes = new List<FixAction> { FixCatalog.SearchDriver(p.Device), FixCatalog.OptionalUpdates, FixCatalog.DeviceManager };
            yield return new Issue
            {
                Id = "driver/" + p.Kind + "." + p.Device.DeviceId,
                Category = IssueCategory.Driver,
                Severity = p.Severity,
                Title = p.Kind == DriverProblemKind.Missing ? L("issue.driver_missing.title", p.Device.Name) : L("issue.driver_error.title", p.Device.Name),
                Detail = L("issue.driver_error.detail", DeviceErrorCodes.Describe(p.Device.ErrorCode), p.Device.ErrorCode, p.Device.SearchId),
                Recommendation = p.Kind == DriverProblemKind.Missing ? L("issue.driver_missing.rec") : L("issue.driver_error.rec"),
                Fixes = fixes,
            };
        }

        foreach (var p in problems.Where(p => p.Kind == DriverProblemKind.Generic))
        {
            if (p.Device.ClassName.Equals("Display", StringComparison.OrdinalIgnoreCase))
            {
                var gpu = data.Hardware.Gpus.FirstOrDefault(g => !g.IsBasicDriver && !g.IsVirtual);
                var page = VendorSupport.GpuDriverPage(gpu?.Name ?? p.Device.Name, gpu?.Vendor ?? p.Device.Manufacturer);
                var fixes = new List<FixAction>();
                if (page is not null) fixes.Add(FixCatalog.VendorDrivers(page.Value.Url));
                fixes.Add(FixCatalog.OptionalUpdates);
                fixes.Add(FixCatalog.SearchDriver(p.Device));
                yield return new Issue
                {
                    Id = "driver/generic_display",
                    Category = IssueCategory.Driver,
                    Severity = Severity.Critical,
                    Title = L("issue.driver_basic_display.title"),
                    Detail = L("issue.driver_basic_display.detail"),
                    Recommendation = L("issue.driver_basic_display.rec"),
                    Fixes = fixes,
                };
            }
            else
            {
                var fixes = new List<FixAction>();
                if (oemPage is not null) fixes.Add(FixCatalog.VendorDrivers(oemPage));
                fixes.Add(FixCatalog.OptionalUpdates);
                yield return new Issue
                {
                    Id = "driver/generic_audio",
                    Category = IssueCategory.Driver,
                    Severity = Severity.Info,
                    Title = L("issue.driver_generic_audio.title"),
                    Detail = L("issue.driver_generic_audio.detail"),
                    Recommendation = L("issue.driver_generic_audio.rec"),
                    Fixes = fixes,
                };
            }
        }

        var unsigned = problems.Where(p => p.Kind == DriverProblemKind.Unsigned).ToList();
        if (unsigned.Count > 0)
        {
            yield return new Issue
            {
                Id = "driver/unsigned",
                Category = IssueCategory.Driver,
                Severity = Severity.Warning,
                Title = L("issue.driver_unsigned.title", unsigned.Count),
                Detail = L("issue.driver_unsigned.detail", Names(unsigned)),
                Recommendation = L("issue.driver_unsigned.rec"),
                Fixes = [FixCatalog.DriversPage, FixCatalog.OptionalUpdates],
            };
        }

        var outdated = problems.Where(p => p.Kind == DriverProblemKind.Outdated).ToList();
        if (outdated.Count > 0)
        {
            var fixes = new List<FixAction> { FixCatalog.DriversPage, FixCatalog.OptionalUpdates };
            if (oemPage is not null) fixes.Add(FixCatalog.VendorDrivers(oemPage));
            yield return new Issue
            {
                Id = "driver/outdated",
                Category = IssueCategory.Driver,
                Severity = Severity.Info,
                Title = L("issue.driver_outdated.title", outdated.Count),
                Detail = L("issue.driver_outdated.detail", Names(outdated)),
                Recommendation = L("issue.driver_outdated.rec"),
                Fixes = fixes,
            };
        }

        var disabled = problems.Where(p => p.Kind == DriverProblemKind.Disabled).ToList();
        if (disabled.Count > 0)
        {
            yield return new Issue
            {
                Id = "driver/disabled",
                Category = IssueCategory.Driver,
                Severity = Severity.Info,
                Title = L("issue.driver_disabled.title", disabled.Count),
                Detail = L("issue.driver_disabled.detail", Names(disabled)),
                Recommendation = L("issue.driver_disabled.rec"),
                Fixes = [FixCatalog.DeviceManager],
            };
        }
    }

    private static string Names(IEnumerable<DriverProblem> problems)
    {
        var list = problems.Select(p => p.Device.Name).Distinct().ToList();
        var shown = string.Join(", ", list.Take(5));
        return list.Count > 5 ? shown + $" (+{list.Count - 5})" : shown;
    }
}
