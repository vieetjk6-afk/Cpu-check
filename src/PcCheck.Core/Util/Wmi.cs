using System.Globalization;
using System.Management;

namespace PcCheck.Core.Util;

/// <summary>Một dòng kết quả WMI đã sao chép sang bộ nhớ (tách khỏi COM, dễ kiểm thử).</summary>
public sealed class WmiRow : Dictionary<string, object?>
{
    public WmiRow() : base(StringComparer.OrdinalIgnoreCase)
    {
    }

    public string Str(string key) => TryGetValue(key, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";

    public long? Long(string key)
    {
        if (!TryGetValue(key, out var v) || v is null) return null;
        try
        {
            return v switch
            {
                string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null,
                ulong u => u > long.MaxValue ? long.MaxValue : (long)u,
                _ => Convert.ToInt64(v, CultureInfo.InvariantCulture),
            };
        }
        catch
        {
            return null;
        }
    }

    public int? Int(string key)
    {
        var l = Long(key);
        return l is null ? null : (int)Math.Clamp(l.Value, int.MinValue, int.MaxValue);
    }

    public bool? Bool(string key)
    {
        if (!TryGetValue(key, out var v) || v is null) return null;
        return v switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var r) ? r : null,
            _ => Long(key) is { } l ? l != 0 : null,
        };
    }

    public string[] StrArray(string key) => TryGetValue(key, out var v) && v is string[] arr ? arr : [];

    /// <summary>Đọc kiểu CIM_DATETIME ("20240115000000.000000+000").</summary>
    public DateTime? Date(string key)
    {
        var s = Str(key);
        if (s.Length < 8) return null;
        try
        {
            return ManagementDateTimeConverter.ToDateTime(s);
        }
        catch
        {
            return DateTime.TryParseExact(s[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }
    }

    /// <summary>Chuỗi UTF-16 dạng mảng ushort (vd WmiMonitorID.UserFriendlyName).</summary>
    public string UShortString(string key)
    {
        if (!TryGetValue(key, out var v) || v is not ushort[] arr) return "";
        return new string(arr.TakeWhile(c => c != 0).Select(c => (char)c).ToArray()).Trim();
    }
}

public static class Wmi
{
    public const string Cimv2 = @"root\cimv2";
    public const string RootWmi = @"root\wmi";
    public const string Storage = @"root\Microsoft\Windows\Storage";
    public const string SecurityCenter = @"root\SecurityCenter2";
    public const string Defender = @"root\Microsoft\Windows\Defender";
    public const string Tpm = @"root\cimv2\Security\MicrosoftTpm";
    public const string Power = @"root\cimv2\power";

    public static List<WmiRow> Query(string wql, string scope = Cimv2, int timeoutSeconds = 20)
    {
        var rows = new List<WmiRow>();
        try
        {
            var options = new System.Management.EnumerationOptions
            {
                ReturnImmediately = true,
                Rewindable = false,
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
            using var collection = searcher.Get();
            foreach (var obj in collection)
            {
                using (obj)
                {
                    var row = new WmiRow();
                    foreach (var prop in obj.Properties)
                    {
                        row[prop.Name] = prop.Value;
                    }

                    rows.Add(row);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"WMI query failed [{scope}] {wql}", ex);
        }

        return rows;
    }

    public static WmiRow? First(string wql, string scope = Cimv2, int timeoutSeconds = 20) =>
        Query(wql, scope, timeoutSeconds).FirstOrDefault();
}
