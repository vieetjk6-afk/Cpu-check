using System.Text;
using System.Text.RegularExpressions;
using PcCheck.Core.Models;

namespace PcCheck.Core.Cleanup;

public enum LeftoverKind
{
    Folder,
    File,
    Shortcut,
    RegistryKey,
    RegistryValue,
    ScheduledTask,
    Service,
}

/// <summary>Một phần còn sót sau khi gỡ phần mềm.</summary>
public sealed class Leftover
{
    public required LeftoverKind Kind { get; init; }

    /// <summary>Đường dẫn tệp/thư mục, khóa registry (HKCU\...), tên tác vụ (\...) hoặc tên dịch vụ.</summary>
    public required string Path { get; init; }

    /// <summary>Với RegistryValue: tên giá trị trong khóa Path.</summary>
    public string ValueName { get; init; } = "";

    public string Program { get; init; } = "";
    public long Bytes { get; init; }

    /// <summary>Chắc chắn thuộc phần mềm đã gỡ (chọn sẵn để xóa); ngược lại người dùng tự quyết.</summary>
    public bool HighConfidence { get; init; }

    /// <summary>Khóa lý do (leftover.reason.*).</summary>
    public string ReasonKey { get; init; } = "";
}

/// <summary>
/// Quy tắc nhận diện phần còn sót – thuần, kiểm thử được. Nguyên tắc an toàn: chỉ khớp thư mục/khóa mang ĐÚNG tên sản phẩm
/// (hoặc Hãng\Sản phẩm, hoặc thư mục cài đặt ghi trong registry); không bao giờ đụng tới thư mục gốc, thư mục hãng dùng chung,
/// thư mục hệ thống, hay thư mục còn chứa / thuộc phần mềm khác đang cài.
/// </summary>
public static partial class LeftoverRules
{
    /// <summary>Tên thư mục/khóa dùng chung cho nhiều sản phẩm – không bao giờ xóa.</summary>
    private static readonly HashSet<string> Umbrella = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Windows NT", "WindowsApps", "Common Files", "Microsoft Shared", "Package Cache", "Packages", "Temp",
        "Programs", "Apps", "Application Data", "Installer", "Downloaded Installations", "Google", "Intel", "Intel Corporation",
        "NVIDIA", "NVIDIA Corporation", "AMD", "ATI Technologies", "Realtek", "Adobe", "Mozilla", "Apple", "Apple Computer, Inc.",
        "Oracle", "Java", "Lenovo", "Dell", "HP", "Hewlett-Packard", "ASUS", "ASUSTeK", "Acer", "MSI", "Gigabyte", "Samsung",
        "Logitech", "Corsair", "Razer", "JetBrains", "Unity", "Unity Technologies", "Epic Games", "Valve", "EA", "Ubisoft",
        "Classes", "Clients", "Policies", "RegisteredApplications", "Wow6432Node", "WOW6432Node", "Software", "System",
        "CurrentVersion", "Uninstall", "Run", "Desktop", "Start Menu", "Startup", "Documents", "Downloads", "Public",
        "Users", "Default", "ProgramData", "Program Files", "Program Files (x86)", "AppData", "Local", "LocalLow", "Roaming",
        "Steam", "steamapps", "Tencent", "Riot Games", "Electron", "Squirrel", "nodejs", "Python", "dotnet",

        // Thư mục dùng chung giữa nhiều phiên bản / thành phần, thường chứa dữ liệu (cơ sở dữ liệu, dự án).
        "Microsoft SQL Server", "Microsoft SQL Server Compact Edition", "Microsoft Office", "Microsoft Visual Studio",
        "Microsoft Analysis Services", "Microsoft.NET", "Reference Assemblies", "MSBuild", "IIS", "IIS Express", "Windows Kits",
        "PostgreSQL", "MySQL", "MariaDB", "MongoDB",
    };

    /// <summary>Tệp dữ liệu của người dùng (cơ sở dữ liệu, tài liệu, bản lưu game...): thư mục chứa chúng không bao giờ được chọn sẵn.</summary>
    private static readonly HashSet<string> DataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mdf", ".ldf", ".ndf", ".bak", ".trn", ".db", ".db3", ".sqlite", ".sqlite3", ".accdb", ".mdb", ".ibd", ".frm", ".myd", ".myi",
        ".dbf", ".fdb", ".gdb", ".pst", ".ost", ".kdbx", ".doc", ".docx", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx", ".odt", ".ods", ".sav",
    };

    /// <summary>Tệp đánh dấu thư mục dữ liệu của máy chủ cơ sở dữ liệu (PostgreSQL, MySQL/MariaDB).</summary>
    private static readonly HashSet<string> DataMarkers = new(StringComparer.OrdinalIgnoreCase) { "PG_VERSION", "ibdata1" };

    public static bool IsDataFile(string fileName) =>
        DataMarkers.Contains(fileName)
        || (DataExtensions.Contains(System.IO.Path.GetExtension(fileName)) && !fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Tên sản phẩm "sạch" để so khớp thư mục: bỏ số phiên bản, (x64)/(64-bit), hậu tố ngôn ngữ...
    /// "WinRAR 6.24 (64-bit)" → "WinRAR"; "Mozilla Firefox (x64 vi)" → "Mozilla Firefox"; "Zoom Workplace" → "Zoom Workplace".
    /// Giữ năm/số hiệu sản phẩm ("SQL Server 2019") để không khớp thư mục dùng chung của mọi phiên bản.
    /// </summary>
    public static string CleanName(string name)
    {
        var text = ParenRegex().Replace(name, " ");
        text = VersionRegex().Replace(text, " ");
        text = ArchRegex().Replace(text, " ");
        text = string.Join(' ', text.Split([' ', '_'], StringSplitOptions.RemoveEmptyEntries).Where(t => t != "-"));
        return text.Trim(' ', '.', ',', '-');
    }

    /// <summary>Chuẩn hóa để so sánh: chữ hoa, bỏ khoảng trắng/ký tự đặc biệt ("Foxit PDF Reader" ≈ "FoxitPDFReader").</summary>
    public static string Key(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        }

        return sb.ToString();
    }

    /// <summary>Tên đủ đặc trưng để tìm theo tên (không quá ngắn, không phải tên hãng dùng chung).</summary>
    public static bool IsDistinctive(string name) => Key(name).Length >= 4 && !Umbrella.Contains(name.Trim()) && !Umbrella.Contains(Key(name));

    /// <summary>Tên hãng gọn: "Foxit Software Inc." → "Foxit Software"; "Zoom Video Communications, Inc." → "Zoom Video Communications".</summary>
    public static string CleanPublisher(string publisher)
    {
        var text = CompanySuffixRegex().Replace(publisher, "");
        return text.Trim(' ', '.', ',');
    }

    /// <summary>Tên thư mục khớp sản phẩm: trùng tên sạch, hoặc trùng tên gốc.</summary>
    public static bool FolderMatches(string folderName, InstalledProgram program)
    {
        if (Umbrella.Contains(folderName)) return false;
        var key = Key(folderName);
        if (key.Length < 4) return false;
        return key == Key(CleanName(program.Name)) || key == Key(program.Name);
    }

    /// <summary>Thư mục hãng khớp (để tìm Hãng\Sản phẩm).</summary>
    public static bool PublisherMatches(string folderName, InstalledProgram program)
    {
        if (program.Publisher.Length == 0) return false;
        var key = Key(folderName);
        return key.Length >= 3 && (key == Key(program.Publisher) || key == Key(CleanPublisher(program.Publisher)));
    }

    private static string Norm(string path) => path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();

    /// <summary>Phần cuối của đường dẫn Windows (tự tách theo '\' để chạy giống nhau trên mọi hệ điều hành, kể cả khi test).</summary>
    public static string LastSegment(string path)
    {
        var trimmed = path.Replace('/', '\\').TrimEnd('\\');
        return trimmed[(trimmed.LastIndexOf('\\') + 1)..];
    }

    public static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        var p = Norm(path);
        var r = Norm(root);
        return p.Length > r.Length && p.StartsWith(r + "\\", StringComparison.Ordinal);
    }

    public static bool SamePath(string a, string b) => Norm(a) == Norm(b);

    /// <summary>
    /// Thư mục không được xóa: thư mục gốc đã biết (Program Files, AppData...), thư mục Windows, ổ đĩa, tên dùng chung,
    /// hoặc liên quan tới phần mềm khác đang cài (chứa, nằm trong, hoặc trùng thư mục cài của phần mềm đó).
    /// </summary>
    public static bool IsProtectedFolder(string path, IEnumerable<string> protectedRoots, string windowsDir, IEnumerable<string> otherInstallPaths)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        var normalized = Norm(path);
        if (normalized.Length <= 3) return true; // C:\
        if (Umbrella.Contains(LastSegment(normalized))) return true;
        if (SamePath(path, windowsDir) || IsUnder(path, windowsDir)) return true;
        foreach (var root in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (SamePath(path, root) || IsUnder(root, path)) return true;
        }

        foreach (var other in otherInstallPaths)
        {
            if (string.IsNullOrWhiteSpace(other)) continue;
            if (SamePath(path, other) || IsUnder(other, path) || IsUnder(path, other)) return true;
        }

        return false;
    }

    /// <summary>Thư mục chứa tệp thực thi trong một dòng lệnh / DisplayIcon ("C:\X\app.exe",0 → C:\X).</summary>
    public static string? FolderOf(string commandOrIcon)
    {
        var text = commandOrIcon.Trim();
        if (text.Length == 0) return null;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            text = end > 1 ? text[1..end] : text.Trim('"');
        }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe > 0) text = text[..(exe + 4)];
            var comma = text.LastIndexOf(',');
            if (comma > 2) text = text[..comma];
        }

        if (text.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase) || text.Contains("rundll32", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Environment.ExpandEnvironmentVariables(text).Replace('/', '\\');
        var slash = path.LastIndexOf('\\');
        return slash > 2 ? path[..slash] : null; // "C:\app.exe" -> thư mục gốc ổ đĩa: không bao giờ là phần còn sót
    }

    /// <summary>Khóa registry an toàn để xóa: Software\Sản phẩm hoặc Software\Hãng\Sản phẩm (không phải khóa hệ thống / hãng dùng chung).</summary>
    public static bool IsSafeRegistryKey(string subKey)
    {
        var parts = subKey.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase)) return false;
        var rest = parts.Skip(1).Where(p => !p.Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase)).ToList();
        if (rest.Count is 0 or > 2) return false;

        // Software\Classes\X, Software\Policies\X, Software\Microsoft\X... là khóa hệ thống / dùng chung.
        if (rest.Count == 2 && Umbrella.Contains(rest[0])) return false;
        return !Umbrella.Contains(rest[^1]) && Key(rest[^1]).Length >= 4;
    }

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex ParenRegex();

    [GeneratedRegex(@"(?<![A-Za-z])v?\d+(\.\d+)+[a-z]?\b|\bversion\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"\b(x64|x86|64-bit|32-bit|64 bit|32 bit|amd64|arm64|win64|win32)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ArchRegex();

    [GeneratedRegex(@",?\s*\b(Inc|Inc\.|LLC|Ltd|Ltd\.|Limited|Corp|Corp\.|Corporation|Co\.|GmbH|S\.A\.|B\.V\.|AG|JSC|Pte)\b\.?", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySuffixRegex();
}
