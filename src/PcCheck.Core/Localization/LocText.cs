using System.Globalization;

namespace PcCheck.Core.Localization;

/// <summary>
/// Chuỗi cần dịch: giữ khóa + tham số, chỉ dịch khi hiển thị nên đổi ngôn ngữ là cập nhật ngay.
/// </summary>
public sealed class LocText
{
    public LocText(string key, params object?[] args)
    {
        Key = key;
        Args = args;
    }

    public string Key { get; }

    public object?[] Args { get; }

    public override string ToString() => Loc.F(Key, Args);
}

public static class LocFactory
{
    public static LocText L(string key, params object?[] args) => new(key, args);
}

/// <summary>Dung lượng byte, tự định dạng theo ngôn ngữ đang chọn (dấu thập phân vi/en khác nhau).</summary>
public readonly record struct ByteSize(long Bytes) : IFormattable
{
    public override string ToString() => ToString(null, Loc.Instance.Culture);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var culture = formatProvider as CultureInfo ?? Loc.Instance.Culture;
        return Util.Format.Bytes(Bytes, culture);
    }
}
