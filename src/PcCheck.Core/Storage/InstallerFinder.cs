using System.IO.Enumeration;
using PcCheck.Core.Cleanup;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Storage;

/// <summary>
/// Tìm tệp cài đặt, ảnh đĩa và tệp nén đã tải về (Downloads, Desktop, Zalo/Telegram...) – thường không còn cần sau khi
/// cài xong. Chỉ đọc; việc xóa đi qua Thùng rác ở trang Phân tích ổ đĩa.
/// </summary>
public static class InstallerFinder
{
    public const long MinBytes = 1024 * 1024;

    /// <summary>Tệp cũ hơn số ngày này mới được chọn sẵn.</summary>
    public const int SuggestAfterDays = 7;

    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = "installer", [".msi"] = "installer", [".msix"] = "installer", [".msixbundle"] = "installer",
        [".appx"] = "installer", [".appxbundle"] = "installer", [".msp"] = "installer",
        [".iso"] = "image", [".img"] = "image",
        [".zip"] = "archive", [".rar"] = "archive", [".7z"] = "archive",
    };

    private static readonly string[] SetupWords = ["SETUP", "INSTALLER", "INSTALL", "OFFLINE", "ONLINE", "LATEST", "WEB", "FULL"];

    /// <summary>"installer", "image", "archive" hoặc null nếu không phải loại cần tìm.</summary>
    public static string? KindOf(string extension) => Kinds.TryGetValue(extension, out var kind) ? kind : null;

    /// <summary>Thư mục tìm kèm độ sâu: Downloads (gồm cả thư mục con của Telegram/trình duyệt), Desktop, tệp nhận từ Zalo.</summary>
    public static IEnumerable<(string Path, int Depth)> SearchRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return (Path.Combine(profile, "Downloads"), 3);
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 1);
        yield return (Path.Combine(documents, "Zalo Received Files"), 2);
        yield return (Path.Combine(documents, "Telegram Desktop"), 1);
    }

    public static Task<List<InstallerFile>> ScanAsync(IEnumerable<InstalledProgram> programs, CancellationToken ct = default) =>
        Task.Run(() => Scan(programs.ToList(), ct), ct);

    private static List<InstallerFile> Scan(List<InstalledProgram> programs, CancellationToken ct)
    {
        var result = new List<InstallerFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var programNames = programs.Where(p => !p.IsEssential).Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var (root, depth) in SearchRoots())
        {
            if (root.Length == 0 || !Directory.Exists(root)) continue;
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System, MaxRecursionDepth = depth - 1 };
            var files = new FileSystemEnumerable<InstallerFile?>(root, (ref FileSystemEntry entry) =>
            {
                var name = entry.FileName.ToString();
                var kind = KindOf(Path.GetExtension(name));
                if (kind is null) return null;
                return new InstallerFile
                {
                    Name = name,
                    Path = entry.ToFullPath(),
                    Bytes = entry.Length,
                    LastWriteUtc = entry.LastWriteTimeUtc.UtcDateTime,
                    Kind = kind,
                };
            }, options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && entry.Length >= MinBytes,
            };

            try
            {
                foreach (var file in files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (file is null || !seen.Add(file.Path) || ProtectedPaths.IsProtected(file.Path)) continue;
                    file.InstalledMatch = MatchInstalled(file.Name, programNames);
                    result.Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Tìm tệp cài đặt trong {root} lỗi", ex);
            }

        }

        result.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        return result;
    }

    /// <summary>Có nên chọn sẵn: tệp cài/ảnh đĩa cũ hơn 7 ngày và (đã cài phần mềm cùng tên, là .msi/.iso, hoặc tên có "setup/installer").</summary>
    public static bool IsSuggested(InstallerFile file, DateTime nowUtc)
    {
        if (file.Kind == "archive" || file.AgeDays(nowUtc) < SuggestAfterDays) return false;
        if (file.InstalledMatch is not null || file.Kind == "image") return true;
        var ext = Path.GetExtension(file.Name);
        if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) return true;
        var key = LeftoverRules.Key(Path.GetFileNameWithoutExtension(file.Name));
        return key.Contains("SETUP", StringComparison.Ordinal) || key.Contains("INSTALL", StringComparison.Ordinal);
    }

    /// <summary>Khóa tên tệp cài để so với tên phần mềm: bỏ phiên bản, kiến trúc và các chữ setup/installer.</summary>
    public static string Key(string fileOrProgramName)
    {
        var key = LeftoverRules.Key(LeftoverRules.CleanName(fileOrProgramName));
        foreach (var word in SetupWords)
        {
            if (key.Length > word.Length + 2) key = key.Replace(word, "", StringComparison.Ordinal);
        }

        return key;
    }

    /// <summary>Từ quá chung chung, không đủ để nhận ra phần mềm (vd "Microsoft", "Player").</summary>
    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "MICROSOFT", "GOOGLE", "ADOBE", "APPLE", "MOZILLA", "INTEL", "AMD", "NVIDIA", "MEDIA", "PLAYER", "UPDATE", "UPDATER", "DRIVER", "DRIVERS",
        "TOOL", "TOOLS", "SOFTWARE", "CLIENT", "RUNTIME", "STUDIO", "VISUAL", "DESKTOP", "APP", "APPS", "SERVICE", "HELPER", "SUPPORT", "EDITION",
        "WINDOWS", "SYSTEM", "MANAGER", "CENTER", "SUITE", "PACK", "PLUS", "PRO", "FREE", "GAME", "GAMES", "READER", "VIEWER", "SDK",
    };

    /// <summary>Tên phần mềm đã cài khớp với tệp cài, null nếu không có.</summary>
    /// <remarks>
    /// Khớp khi: trùng cả tên ("ZaloSetup" ↔ "Zalo"), tên tệp bắt đầu bằng tên phần mềm ("UltraViewer_setup_vi" ↔ "UltraViewer"),
    /// hoặc trùng một từ đặc trưng của tên ("ChromeSetup" ↔ "Google Chrome", vlc-3.0-win64.exe ↔ VLC media player).
    /// Không khớp theo một phần của từ ("photos" ≠ "Photoshop").
    /// </remarks>
    public static string? MatchInstalled(string fileName, IEnumerable<string> programNames)
    {
        var fileKey = Key(Path.GetFileNameWithoutExtension(fileName));
        if (fileKey.Length < 3) return null;
        foreach (var name in programNames)
        {
            var programKey = Key(name);
            if (programKey.Length < 3) continue;
            if (programKey == fileKey || (programKey.Length >= 4 && fileKey.StartsWith(programKey, StringComparison.Ordinal))) return name;

            var words = LeftoverRules.CleanName(name).Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
                .Select(LeftoverRules.Key).Where(w => w.Length > 0).ToList();
            for (var w = 0; w < words.Count; w++)
            {
                var word = words[w];
                if (GenericWords.Contains(word)) continue;
                var distinctive = word.Length >= 4 || (w == 0 && word.Length >= 3);
                if (distinctive && word == fileKey) return name;
            }
        }

        return null;
    }
}
