using System.Globalization;
using System.Text.RegularExpressions;
using PcCheck.Core.Models;

namespace PcCheck.Core.Analysis;

/// <summary>
/// Ước lượng CPU có nằm trong danh sách hỗ trợ Windows 11 không (Intel Core thế hệ 8+, AMD Ryzen 2000+).
/// Đây là heuristic theo tên CPU; các dòng khó đoán (Pentium, Celeron, Xeon, Athlon) trả về Unknown.
/// </summary>
public static partial class Windows11Cpu
{
    [GeneratedRegex(@"\bI[3579][\s-]+(?:M\s+)?(\d{3,5})", RegexOptions.CultureInvariant)]
    private static partial Regex IntelCoreRegex();

    [GeneratedRegex(@"\bCORE\s+[357]\s+\d{3}", RegexOptions.CultureInvariant)]
    private static partial Regex IntelCoreSeriesRegex();

    [GeneratedRegex(@"RYZEN\s+(?:THREADRIPPER\s+)?(?:PRO\s+)?(?:\d\s+)?(?:PRO\s+)?(\d)\d{3}", RegexOptions.CultureInvariant)]
    private static partial Regex RyzenRegex();

    [GeneratedRegex(@"\b(?:N100|N200|N95|N97|N50|N300|N305)\b", RegexOptions.CultureInvariant)]
    private static partial Regex IntelNSeriesRegex();

    [GeneratedRegex(@"\bA(?:4|6|8|9|10|12)-\d{4}", RegexOptions.CultureInvariant)]
    private static partial Regex AmdApuRegex();

    public static CheckStatus Evaluate(string cpuName)
    {
        var name = cpuName.ToUpperInvariant();
        if (name.Length == 0) return CheckStatus.Unknown;

        if (name.Contains("CORE ULTRA") || name.Contains("SNAPDRAGON") || name.Contains("RYZEN AI") || name.Contains("APPLE"))
            return CheckStatus.Pass;

        if (name.Contains("CORE2") || name.Contains("CORE 2") || name.Contains("ATOM") || name.Contains("PHENOM")
            || name.Contains("ATHLON II") || name.Contains("ATHLON 64") || name.Contains("FX-") || name.Contains("PENTIUM(R) DUAL")
            || name.Contains("PENTIUM DUAL") || AmdApuRegex().IsMatch(name))
            return CheckStatus.Fail;

        if (IntelCoreSeriesRegex().IsMatch(name) || IntelNSeriesRegex().IsMatch(name)) return CheckStatus.Pass;

        var intel = IntelCoreRegex().Match(name);
        if (intel.Success)
        {
            var gen = IntelGeneration(intel.Groups[1].Value);
            return gen >= 8 ? CheckStatus.Pass : CheckStatus.Fail;
        }

        var ryzen = RyzenRegex().Match(name);
        if (ryzen.Success)
        {
            var series = int.Parse(ryzen.Groups[1].Value, CultureInfo.InvariantCulture);
            return series >= 2 ? CheckStatus.Pass : CheckStatus.Fail;
        }

        return CheckStatus.Unknown;
    }

    /// <summary>i5-8250U -> 8, i7-10700K -> 10, i5-1135G7 -> 11, i5-650 -> 1.</summary>
    internal static int IntelGeneration(string digits) => digits.Length switch
    {
        5 => int.Parse(digits[..2], CultureInfo.InvariantCulture),
        4 when digits[0] == '1' => int.Parse(digits[..2], CultureInfo.InvariantCulture),
        4 => digits[0] - '0',
        _ => 1,
    };
}
