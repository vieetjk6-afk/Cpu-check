using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

/// <summary>Đọc Event Log để phát hiện màn hình xanh, tắt máy đột ngột, lỗi phần cứng, lỗi ổ cứng, crash ứng dụng...</summary>
public static class EventLogCollector
{
    private sealed record Source(string Log, string[] Providers, int[] Ids, EventKind Kind, int Max = 200);

    private static readonly Source[] Sources =
    [
        new("System", ["Microsoft-Windows-Kernel-Power"], [41], EventKind.UnexpectedShutdown),
        new("System", ["Microsoft-Windows-WER-SystemErrorReporting", "BugCheck"], [1001], EventKind.BlueScreen),
        new("System", ["Microsoft-Windows-WHEA-Logger"], [1, 18, 20, 46], EventKind.HardwareErrorFatal),
        new("System", ["Microsoft-Windows-WHEA-Logger"], [17, 19, 47], EventKind.HardwareError),
        new("System", ["disk"], [7, 11, 51, 153], EventKind.DiskError),
        new("System", ["storahci", "stornvme", "iaStorA", "iaStorAC", "iaStorAVC", "iaStorV", "amdsata", "nvme"], [129], EventKind.StorageReset),
        new("System", ["Ntfs", "Microsoft-Windows-Ntfs"], [55, 137, 140], EventKind.FileSystemError),
        new("System", ["Display"], [4101], EventKind.GpuDriverCrash),
        new("System", ["Microsoft-Windows-Resource-Exhaustion-Detector"], [2004], EventKind.LowMemory),
        new("Application", ["Application Error"], [1000], EventKind.AppCrash, 400),
        new("Application", ["Application Hang"], [1002], EventKind.AppHang, 200),
    ];

    public static EventSummary Collect(int days = 30)
    {
        var summary = new EventSummary { Days = days };
        var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        var failures = 0;

        var lists = new List<EventEntry>[Sources.Length];
        Parallel.For(0, Sources.Length, i =>
        {
            lists[i] = [];
            try
            {
                ReadSource(Sources[i], ms, lists[i]);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failures);
                Log.Warn($"Đọc event log {Sources[i].Log}/{Sources[i].Kind} lỗi", ex);
            }
        });

        foreach (var list in lists) summary.Entries.AddRange(list);
        summary.Available = failures < Sources.Length;

        try
        {
            var dumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
            if (Directory.Exists(dumpDir))
            {
                var since = DateTime.Now.AddDays(-days);
                var dumps = new DirectoryInfo(dumpDir).EnumerateFiles("*.dmp").Where(f => f.LastWriteTime >= since).ToList();
                summary.MinidumpCount = dumps.Count;
                summary.LastMinidump = dumps.Count == 0 ? null : dumps.Max(f => f.LastWriteTime);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Cần quyền admin để đọc Minidump.
        }

        return summary;
    }

    private static void ReadSource(Source source, long ms, List<EventEntry> output)
    {
        var providers = string.Join(" or ", source.Providers.Select(p => $"@Name='{p}'"));
        var ids = string.Join(" or ", source.Ids.Select(id => $"EventID={id}"));
        var xpath = $"*[System[Provider[{providers}] and ({ids}) and TimeCreated[timediff(@SystemTime) <= {ms}]]]";

        var query = new EventLogQuery(source.Log, PathType.LogName, xpath) { ReverseDirection = true };
        using var reader = new EventLogReader(query);
        for (var count = 0; count < source.Max; count++)
        {
            using var record = reader.ReadEvent();
            if (record is null) break;
            output.Add(new EventEntry(
                source.Kind,
                record.TimeCreated ?? DateTime.MinValue,
                record.Id,
                record.ProviderName ?? "",
                ExtractDetail(source.Kind, record)));
        }
    }

    private static string? ExtractDetail(EventKind kind, EventRecord record)
    {
        try
        {
            var props = record.Properties;
            switch (kind)
            {
                case EventKind.AppCrash:
                case EventKind.AppHang:
                    return props.Count > 0 ? Convert.ToString(props[0].Value, CultureInfo.InvariantCulture) : null;
                case EventKind.BlueScreen:
                    return props.Count > 0 ? NormalizeBugCheck(Convert.ToString(props[0].Value, CultureInfo.InvariantCulture)) : null;
                case EventKind.UnexpectedShutdown:
                    // Thuộc tính 0 = BugcheckCode (0 nếu mất nguồn, khác 0 nếu do màn hình xanh).
                    return props.Count > 0 ? Convert.ToString(props[0].Value, CultureInfo.InvariantCulture) : null;
                case EventKind.DiskError:
                case EventKind.StorageReset:
                    return props.Count > 0 ? Convert.ToString(props[0].Value, CultureInfo.InvariantCulture) : null;
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"0x0000009f (0x..., ...)" -> "0x0000009F".</summary>
    internal static string? NormalizeBugCheck(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var token = raw.Trim().Split(' ', '(')[0];
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
        {
            return "0x" + code.ToString("X8", CultureInfo.InvariantCulture);
        }

        return token;
    }
}
