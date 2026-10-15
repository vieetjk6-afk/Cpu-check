using System.Text;

namespace PcCheck.Core.Util;

/// <summary>Ghi log đơn giản ra %LocalAppData%\PcCheck\logs (không cần thư viện ngoài).</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcCheck", "logs");

    public static string FilePath => Path.Combine(Directory, "pccheck.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message, Exception? ex = null) => Write("WARN", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch
        {
            // Không để lỗi ghi log làm hỏng ứng dụng.
        }
    }
}
