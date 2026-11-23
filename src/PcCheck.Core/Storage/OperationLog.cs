using System.Text;
using System.Text.Json;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Storage;

/// <summary>
/// Lịch sử dọn dẹp: mỗi lần dọn rác, xóa phần còn sót, xóa tệp/thư mục được ghi một dòng JSON vào
/// %LocalAppData%\PcCheck\history.jsonl để xem lại đã xóa gì, khi nào, giải phóng bao nhiêu.
/// </summary>
public static class OperationLog
{
    public const string SourceJunk = "junk";
    public const string SourceLeftovers = "leftovers";
    public const string SourceDisk = "disk";
    public const string SourceInstallers = "installers";
    public const string SourcePurge = "purge";

    public const string ActionRemoved = "removed";
    public const string ActionRecycled = "recycled";
    public const string ActionSkipped = "skipped";
    public const string ActionFailed = "failed";

    /// <summary>Giới hạn kích thước tệp lịch sử; vượt quá thì giữ nửa mới nhất.</summary>
    private const long MaxBytes = 1024 * 1024;

    private static readonly Lock Sync = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcCheck", "history.jsonl");

    /// <summary>Phát khi có dòng mới (trên luồng gọi Record).</summary>
    public static event EventHandler? Changed;

    public static void Record(string source, string action, string target, long bytes = 0, int count = 1, string? detail = null) =>
        Record([new OperationRecord { Time = DateTime.Now, Source = source, Action = action, Target = target, Bytes = bytes, Count = count, Detail = detail }]);

    public static void Record(IEnumerable<OperationRecord> records)
    {
        var lines = records.Select(r => JsonSerializer.Serialize(r, Json)).ToList();
        if (lines.Count == 0) return;
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                TrimIfNeeded();
                File.AppendAllLines(FilePath, lines, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Ghi lịch sử dọn dẹp lỗi", ex);
            return;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Các dòng mới nhất trước (tối đa <paramref name="limit"/>).</summary>
    public static List<OperationRecord> Read(int limit = 500)
    {
        var result = new List<OperationRecord>();
        try
        {
            string[] lines;
            lock (Sync)
            {
                if (!File.Exists(FilePath)) return result;
                lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            }

            for (var i = lines.Length - 1; i >= 0 && result.Count < limit; i--)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                try
                {
                    if (JsonSerializer.Deserialize<OperationRecord>(lines[i], Json) is { } record) result.Add(record);
                }
                catch (JsonException)
                {
                    // Dòng hỏng (ghi dở khi tắt máy): bỏ qua.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Đọc lịch sử dọn dẹp lỗi", ex);
        }

        return result;
    }

    public static void Clear()
    {
        try
        {
            lock (Sync)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Xóa lịch sử dọn dẹp lỗi", ex);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void TrimIfNeeded()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxBytes) return;
        var lines = File.ReadAllLines(FilePath, Encoding.UTF8);
        File.WriteAllLines(FilePath, lines.Skip(lines.Length / 2), Encoding.UTF8);
    }
}
