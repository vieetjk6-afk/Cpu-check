using System.Text.Json;

namespace PcCheck.Core.Util;

/// <summary>Cài đặt người dùng, lưu ở %AppData%\PcCheck\settings.json.</summary>
public sealed class AppSettings
{
    public string Language { get; set; } = "";
    public string Theme { get; set; } = "dark";
    public bool AutoScanOnStartup { get; set; } = true;
    public bool LiveMonitoring { get; set; } = true;

    public static string FilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PcCheck", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Warn("Lưu cài đặt lỗi", ex);
        }
    }
}
