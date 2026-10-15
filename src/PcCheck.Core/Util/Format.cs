using System.Globalization;
using PcCheck.Core.Localization;

namespace PcCheck.Core.Util;

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Bytes(long bytes, CultureInfo? culture = null)
    {
        culture ??= Loc.Instance.Culture;
        if (bytes < 0) bytes = 0;
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        return value.ToString(format, culture) + " " + Units[unit];
    }

    public static string Bytes(ulong bytes, CultureInfo? culture = null) =>
        Bytes(bytes > long.MaxValue ? long.MaxValue : (long)bytes, culture);

    /// <summary>Làm tròn dung lượng ổ/RAM về con số "thương mại" quen thuộc (vd 476.9 GB -> 512 GB).</summary>
    public static string MarketingSize(long bytes, CultureInfo? culture = null)
    {
        culture ??= Loc.Instance.Culture;
        if (bytes <= 0) return "0 GB";
        var gb = bytes / 1_000_000_000d;
        if (gb >= 1000) return (gb / 1000).ToString("0.#", culture) + " TB";
        return Math.Round(gb).ToString("0", culture) + " GB";
    }

    public static string Mhz(double mhz, CultureInfo? culture = null)
    {
        culture ??= Loc.Instance.Culture;
        return mhz >= 1000
            ? (mhz / 1000).ToString("0.0#", culture) + " GHz"
            : mhz.ToString("0", culture) + " MHz";
    }

    public static string Celsius(double? value, CultureInfo? culture = null)
    {
        culture ??= Loc.Instance.Culture;
        return value is null ? "—" : value.Value.ToString("0", culture) + " °C";
    }

    public static string Percent(double? value, CultureInfo? culture = null)
    {
        culture ??= Loc.Instance.Culture;
        return value is null ? "—" : value.Value.ToString("0", culture) + "%";
    }

    public static string Date(DateTime? value, CultureInfo? culture = null)
    {
        culture ??= Loc.Instance.Culture;
        return value is null ? "—" : value.Value.ToString("d", culture);
    }

    public static string Duration(TimeSpan span)
    {
        if (span.TotalDays >= 1) return Loc.F("unit.days_hours", (int)span.TotalDays, span.Hours);
        if (span.TotalHours >= 1) return Loc.F("unit.hours_minutes", (int)span.TotalHours, span.Minutes);
        return Loc.F("unit.minutes", Math.Max(1, (int)span.TotalMinutes));
    }

    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
