using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Analysis;

/// <summary>Gộp tất cả analyzer: dữ liệu thô -> danh sách vấn đề, kiểm tra tương thích, điểm sức khỏe.</summary>
public static class Analyzer
{
    public static ScanResult Analyze(ScanData data, DateTime now, TimeSpan duration = default)
    {
        var driverProblems = DriverAnalyzer.FindProblems(data.Devices, now);
        var checks = CompatibilityAnalyzer.Analyze(data, now);
        var security = SafeSecurity(data, now);

        var issues = new List<Issue>();
        Collect(issues, () => HardwareAnalyzer.Analyze(data));
        Collect(issues, () => DriverAnalyzer.Analyze(data, driverProblems, now));
        Collect(issues, () => SystemAnalyzer.Analyze(data, now));
        Collect(issues, () => CleanupAnalyzer.Analyze(data, now));
        Collect(issues, () => CompatibilityAnalyzer.ToIssues(checks, data.Hardware.Os.IsWindows11));
        Collect(issues, () => SecurityAnalyzer.ToIssues(security));

        var unique = issues
            .GroupBy(i => i.Id)
            .Select(g => g.OrderByDescending(i => i.Severity).First())
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Category)
            .ToList();
        IssueGuides.Attach(unique);

        return new ScanResult
        {
            Data = data,
            Issues = unique,
            Checks = checks,
            DriverProblems = driverProblems,
            Score = HealthScore.Compute(unique),
            Security = security,
            Time = now,
            Duration = duration,
        };
    }

    private static SecurityReport SafeSecurity(ScanData data, DateTime now)
    {
        try
        {
            return SecurityAnalyzer.Analyze(data, now);
        }
        catch (Exception ex)
        {
            Log.Error("Phân tích bảo mật lỗi", ex);
            return SecurityReport.Empty;
        }
    }

    private static void Collect(List<Issue> output, Func<IEnumerable<Issue>> producer)
    {
        try
        {
            output.AddRange(producer());
        }
        catch (Exception ex)
        {
            // Một analyzer lỗi không được làm hỏng cả lần quét.
            Log.Error("Analyzer lỗi", ex);
        }
    }
}
