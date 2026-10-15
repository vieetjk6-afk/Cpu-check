namespace PcCheck.Core.Analysis;

/// <summary>Thời hạn hỗ trợ của từng phiên bản Windows (theo build), bản Home/Pro và Enterprise/Education.</summary>
public static class WindowsLifecycle
{
    public sealed record Release(int Build, string Version, DateTime HomeProEnd, DateTime EnterpriseEnd);

    private static readonly Release[] Releases =
    [
        new(10240, "1507", new(2017, 5, 9), new(2017, 5, 9)),
        new(10586, "1511", new(2017, 10, 10), new(2018, 4, 10)),
        new(14393, "1607", new(2018, 4, 10), new(2019, 4, 9)),
        new(15063, "1703", new(2018, 10, 9), new(2019, 10, 8)),
        new(16299, "1709", new(2019, 4, 9), new(2020, 10, 13)),
        new(17134, "1803", new(2019, 11, 12), new(2021, 5, 11)),
        new(17763, "1809", new(2020, 11, 10), new(2021, 5, 11)),
        new(18362, "1903", new(2020, 12, 8), new(2020, 12, 8)),
        new(18363, "1909", new(2021, 5, 11), new(2022, 5, 10)),
        new(19041, "2004", new(2021, 12, 14), new(2021, 12, 14)),
        new(19042, "20H2", new(2022, 5, 10), new(2023, 5, 9)),
        new(19043, "21H1", new(2022, 12, 13), new(2022, 12, 13)),
        new(19044, "21H2", new(2023, 6, 13), new(2024, 6, 11)),
        new(19045, "22H2", new(2025, 10, 14), new(2025, 10, 14)),
        new(22000, "21H2", new(2023, 10, 10), new(2024, 10, 8)),
        new(22621, "22H2", new(2024, 10, 8), new(2025, 10, 14)),
        new(22631, "23H2", new(2025, 11, 11), new(2026, 11, 10)),
        new(26100, "24H2", new(2026, 10, 13), new(2027, 10, 12)),
        new(26200, "25H2", new(2027, 10, 12), new(2028, 10, 10)),
    ];

    public static Release? Find(int build) => Releases.LastOrDefault(r => r.Build == build);

    /// <summary>Phiên bản được hỗ trợ mới nhất trong bảng (để gợi ý cập nhật lên).</summary>
    public static Release Latest => Releases[^1];

    public static bool IsLongTermServicing(string edition) =>
        edition.Contains("EnterpriseS", StringComparison.OrdinalIgnoreCase)
        || edition.Contains("IoT", StringComparison.OrdinalIgnoreCase)
        || edition.Contains("Server", StringComparison.OrdinalIgnoreCase);

    public static bool IsEnterpriseLike(string edition) =>
        edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
        || edition.Contains("Education", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ngày hết hỗ trợ của bản đang chạy; null nếu không biết (build mới hơn bảng, LTSC, Server...).</summary>
    public static DateTime? EndOfSupport(int build, string edition)
    {
        if (IsLongTermServicing(edition)) return null;
        var release = Find(build);
        if (release is null) return null;
        return IsEnterpriseLike(edition) ? release.EnterpriseEnd : release.HomeProEnd;
    }
}
