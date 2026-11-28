using System.Text.Json;
using PcCheck.Core.Util;

namespace PcCheck.Core.Performance;

/// <summary>Lưu kết quả đo gần nhất để so sánh lần sau (LocalAppData\PcCheck\benchmark.json).</summary>
public static class BenchmarkStore
{
    public static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcCheck", "benchmark.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static BenchmarkResult? Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<BenchmarkResult>(File.ReadAllText(FilePath), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Đọc kết quả đo trước lỗi", ex);
            return null;
        }
    }

    public static void Save(BenchmarkResult result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(result, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Lưu kết quả đo lỗi", ex);
        }
    }
}
