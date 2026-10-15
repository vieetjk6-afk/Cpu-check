using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>Tệp rác, phần mềm ít dùng / không mong muốn, chương trình khởi động.</summary>
public static class CleanupAnalyzer
{
    public static IEnumerable<Issue> Analyze(ScanData data, DateTime now)
    {
        var junkBytes = data.Junk.Where(j => j.DefaultSelected).Sum(j => j.Bytes);
        if (junkBytes >= Thresholds.JunkInfoBytes)
        {
            yield return new Issue
            {
                Id = "junk/total",
                Category = IssueCategory.Junk,
                Severity = junkBytes >= Thresholds.JunkWarnBytes ? Severity.Warning : Severity.Info,
                Title = L("issue.junk.title", new ByteSize(junkBytes)),
                Detail = L("issue.junk.detail", TopJunk(data.Junk)),
                Recommendation = L("issue.junk.rec"),
                Fixes = [FixCatalog.CleanupPage],
            };
        }

        var bloat = data.Programs.Where(p => p.BloatReasonKey is not null && p.Scope != ProgramScope.Store).ToList();
        if (bloat.Count > 0)
        {
            var crack = bloat.Any(p => p.BloatReasonKey == SoftwareCatalog.ReasonCrack);
            yield return new Issue
            {
                Id = "software/bloat",
                Category = IssueCategory.Software,
                Severity = crack ? Severity.Critical : Severity.Warning,
                Title = L("issue.bloat.title", bloat.Count),
                Detail = L("issue.bloat.detail", string.Join(", ", bloat.Select(p => p.Name).Take(6))),
                Recommendation = crack ? L("issue.bloat.rec_crack") : L("issue.bloat.rec"),
                Fixes = [FixCatalog.SoftwarePage],
            };
        }

        var unused = UnusedPrograms(data.Programs, now).ToList();
        if (unused.Count > 0)
        {
            var size = unused.Sum(p => p.SizeBytes ?? 0);
            yield return new Issue
            {
                Id = "software/unused",
                Category = IssueCategory.Software,
                Severity = Severity.Info,
                Title = L("issue.unused.title", unused.Count, Thresholds.UnusedDays),
                Detail = L("issue.unused.detail", string.Join(", ", unused.OrderByDescending(p => p.SizeBytes ?? 0).Select(p => p.Name).Take(6)), new ByteSize(size)),
                Recommendation = L("issue.unused.rec"),
                Fixes = [FixCatalog.SoftwarePage],
            };
        }

        var enabledStartup = data.Startup.Count(s => s.Enabled);
        if (enabledStartup >= Thresholds.StartupInfo)
        {
            yield return new Issue
            {
                Id = "startup/count",
                Category = IssueCategory.Software,
                Severity = enabledStartup >= Thresholds.StartupWarn ? Severity.Warning : Severity.Info,
                Title = L("issue.startup.title", enabledStartup),
                Detail = L("issue.startup.detail"),
                Recommendation = L("issue.startup.rec"),
                Fixes = [FixCatalog.StartupPage],
            };
        }
    }

    /// <summary>Phần mềm có dữ liệu sử dụng và đã lâu không mở (bỏ qua phần mềm thiết yếu và cài gần đây).</summary>
    public static IEnumerable<InstalledProgram> UnusedPrograms(IEnumerable<InstalledProgram> programs, DateTime now) =>
        programs.Where(p => IsUnused(p, now));

    public static bool IsUnused(InstalledProgram p, DateTime now) =>
        !p.IsEssential
        && p.Scope != ProgramScope.Store
        && p.LastUsed is not null
        && (now - p.LastUsed.Value).TotalDays >= Thresholds.UnusedDays
        && (p.InstallDate is null || (now - p.InstallDate.Value).TotalDays >= Thresholds.UnusedDays);

    private static string TopJunk(IEnumerable<JunkCategory> junk) =>
        string.Join(", ", junk.Where(j => j.Bytes > 0).OrderByDescending(j => j.Bytes).Take(4)
            .Select(j => $"{j.Name} ({new ByteSize(j.Bytes)})"));
}
