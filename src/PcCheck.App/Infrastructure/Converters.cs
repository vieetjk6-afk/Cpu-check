using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PcCheck.Core.Models;

namespace PcCheck.App.Infrastructure;

/// <summary>Màu trạng thái cố định (đọc tốt trên cả nền sáng và tối).</summary>
public static class StatusColors
{
    public static readonly SolidColorBrush Success = Make(0x22, 0xC5, 0x5E);
    public static readonly SolidColorBrush Warning = Make(0xF5, 0x9E, 0x0B);
    public static readonly SolidColorBrush Danger = Make(0xEF, 0x44, 0x44);
    public static readonly SolidColorBrush Info = Make(0x3B, 0x82, 0xF6);
    public static readonly SolidColorBrush Neutral = Make(0x8A, 0x92, 0xA3);

    public static readonly SolidColorBrush SuccessSoft = Make(0x22, 0xC5, 0x5E, 0x26);
    public static readonly SolidColorBrush WarningSoft = Make(0xF5, 0x9E, 0x0B, 0x26);
    public static readonly SolidColorBrush DangerSoft = Make(0xEF, 0x44, 0x44, 0x26);
    public static readonly SolidColorBrush InfoSoft = Make(0x3B, 0x82, 0xF6, 0x26);
    public static readonly SolidColorBrush NeutralSoft = Make(0x8A, 0x92, 0xA3, 0x22);

    private static SolidColorBrush Make(byte r, byte g, byte b, byte a = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush For(Severity s) => s switch
    {
        Severity.Critical => Danger,
        Severity.Warning => Warning,
        Severity.Info => Info,
        _ => Success,
    };

    public static SolidColorBrush SoftFor(Severity s) => s switch
    {
        Severity.Critical => DangerSoft,
        Severity.Warning => WarningSoft,
        Severity.Info => InfoSoft,
        _ => SuccessSoft,
    };

    public static Severity FromCheck(CheckStatus status) => status switch
    {
        CheckStatus.Pass => Severity.Ok,
        CheckStatus.Warning => Severity.Warning,
        CheckStatus.Fail => Severity.Critical,
        _ => Severity.Info,
    };

    public static string GlyphFor(Severity s) => s switch
    {
        Severity.Critical => "", // ErrorBadge
        Severity.Warning => "",  // Warning
        Severity.Info => "",     // Info
        _ => "",                 // CheckMark
    };
}

/// <summary>Severity / CheckStatus -> màu đậm. Tham số "soft" -> màu nền nhạt.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var severity = value switch
        {
            Severity s => s,
            CheckStatus c => StatusColors.FromCheck(c),
            _ => (Severity?)null,
        };
        if (severity is null) return parameter as string == "soft" ? StatusColors.NeutralSoft : StatusColors.Neutral;
        return parameter as string == "soft" ? StatusColors.SoftFor(severity.Value) : StatusColors.For(severity.Value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SeverityGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        Severity s => StatusColors.GlyphFor(s),
        CheckStatus.Unknown => "\uE897", // Help (dấu hỏi)
        CheckStatus c => StatusColors.GlyphFor(StatusColors.FromCheck(c)),
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true -> Visible. Tham số "invert" để đảo.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter as string == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ (parameter as string == "invert");
}

/// <summary>null / chuỗi rỗng / số 0 / danh sách rỗng -> Collapsed. Tham số "invert" để đảo.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            int i => i == 0,
            long l => l == 0,
            double d => d == 0,
            ICollection c => c.Count == 0,
            IEnumerable e => !e.Cast<object>().Any(),
            _ => false,
        };
        if (parameter as string == "invert") empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>So sánh giá trị với tham số (dùng cho RadioButton 2 chiều).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null) return Binding.DoNothing;
        if (targetType == typeof(int) && int.TryParse(parameter.ToString(), out var i)) return i;
        if (targetType.IsEnum) return Enum.Parse(targetType, parameter.ToString()!, true);
        return parameter.ToString()!;
    }
}

/// <summary>Giá trị 0-100 -> độ rộng theo tham số (độ rộng tối đa) cho thanh nhỏ.</summary>
public sealed class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var max = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 100;
        var percent = value switch
        {
            double d => d,
            float f => f,
            int i => i,
            _ => 0,
        };
        return Math.Clamp(percent, 0, 100) / 100 * max;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
