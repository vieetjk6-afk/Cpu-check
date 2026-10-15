using System.Text.Json;
using System.Text.Json.Serialization;

namespace PcCheck.Core.Util;

/// <summary>Cài đặt người dùng, lưu ở %AppData%\PcCheck\settings.json.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "";
    public string Theme { get; set; } = "dark";
    public bool AutoScanOnStartup { get; set; } = true;
    public bool LiveMonitoring { get; set; } = true;

    /// <summary>Chu kỳ cập nhật số liệu trực tiếp (giây): 1, 2 hoặc 5.</summary>
    public int LiveIntervalSeconds { get; set; } = 2;

    public WidgetSettings Widget { get; set; } = new();

    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcCheck", "settings.json");

    // Cho phép NaN (vị trí widget chưa đặt) khi ghi/đọc JSON.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    internal static AppSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        settings.Widget ??= new WidgetSettings();
        settings.Widget.Metrics = settings.Widget.Metrics?.Where(WidgetSettings.AllMetrics.Contains).Distinct().ToList() ?? [.. WidgetSettings.DefaultMetrics];
        return settings;
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return FromJson(File.ReadAllText(FilePath));
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc cài đặt lỗi, dùng mặc định", ex);
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, ToJson());
        }
        catch (Exception ex)
        {
            Log.Warn("Lưu cài đặt lỗi", ex);
        }
    }
}

/// <summary>Cài đặt widget nổi trên màn hình.</summary>
public sealed class WidgetSettings
{
    /// <summary>Tất cả thông số widget hỗ trợ (giao diện hiển thị theo thứ tự này).</summary>
    public static readonly IReadOnlyList<string> AllMetrics =
        ["cpu", "cpu_temp", "gpu", "gpu_temp", "vram", "ram", "net", "disk", "disk_temp", "devices", "fans", "battery", "uptime", "clock"];

    public static readonly IReadOnlyList<string> DefaultMetrics = ["cpu", "cpu_temp", "gpu", "gpu_temp", "ram", "net", "disk"];

    public bool Visible { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool Locked { get; set; }
    public bool ShowGraphs { get; set; } = true;

    /// <summary>Thu gọn widget thành lưới nhỏ chỉ có biểu tượng + giá trị.</summary>
    public bool Collapsed { get; set; }

    /// <summary>Độ đục 0.4 – 1.0.</summary>
    public double Opacity { get; set; } = 0.95;

    /// <summary>Tỉ lệ phóng to: 0.85 (nhỏ), 1.0 (vừa), 1.2 (lớn).</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>"vertical" hoặc "horizontal".</summary>
    public string Layout { get; set; } = "vertical";

    /// <summary>Vị trí trên màn hình (DIP); NaN = chưa đặt, dùng góc trên bên phải.</summary>
    public double Left { get; set; } = double.NaN;

    public double Top { get; set; } = double.NaN;

    public List<string> Metrics { get; set; } = [.. DefaultMetrics];
}

