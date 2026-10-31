using System.IO.Enumeration;
using PcCheck.Core.Cleanup;

namespace PcCheck.Core.Storage;

/// <summary>
/// Danh sách bảo vệ do người dùng đặt (Cài đặt → Thư mục được bảo vệ): mọi chức năng xóa của PC Check (tệp rác,
/// phân tích ổ đĩa, tệp cài đặt, thư mục build) đều bỏ qua đường dẫn nằm trong đây. Hỗ trợ ký tự đại diện * và ?.
/// </summary>
public static class ProtectedPaths
{
    private static volatile string[] _patterns = [];

    public static IReadOnlyList<string> Patterns => _patterns;

    public static void Set(IEnumerable<string>? patterns) =>
        _patterns = patterns?.Select(Normalize).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];

    public static bool IsProtected(string path) => IsProtected(path, _patterns);

    /// <summary>Đường dẫn trùng, nằm bên trong một mục bảo vệ, hoặc khớp mẫu (kể cả khi thư mục cha khớp mẫu).</summary>
    public static bool IsProtected(string path, IReadOnlyCollection<string> patterns)
    {
        if (patterns.Count == 0 || string.IsNullOrWhiteSpace(path)) return false;
        var target = Normalize(path);
        foreach (var raw in patterns)
        {
            var pattern = Normalize(raw);
            if (pattern.Length == 0) continue;
            if (pattern.IndexOfAny(['*', '?']) >= 0)
            {
                // "\" là ký tự thoát trong MatchesSimpleExpression -> so khớp với "/" thay cho "\".
                // So khớp cả đường dẫn lẫn từng thư mục cha: "C:\Work\*" bảo vệ mọi thứ bên dưới C:\Work.
                var expression = pattern.Replace('\\', '/');
                for (var current = target; current.Length > 0; current = Parent(current))
                {
                    if (FileSystemName.MatchesSimpleExpression(expression, current.Replace('\\', '/'), ignoreCase: true)) return true;
                }
            }
            else if (LeftoverRules.SamePath(target, pattern) || LeftoverRules.IsUnder(target, pattern))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Có mục bảo vệ nào nằm bên trong thư mục này không (xóa cả thư mục sẽ xóa luôn mục bảo vệ).</summary>
    public static bool ContainsProtected(string folder) => ContainsProtected(folder, _patterns);

    public static bool ContainsProtected(string folder, IReadOnlyCollection<string> patterns)
    {
        foreach (var raw in patterns)
        {
            var pattern = Normalize(raw);
            var literal = pattern.IndexOfAny(['*', '?']) is var i and >= 0 ? pattern[..i].TrimEnd('\\') : pattern;
            if (literal.Length > 0 && LeftoverRules.IsUnder(literal, folder)) return true;
        }

        return false;
    }

    internal static string Normalize(string path)
    {
        var p = path.Trim().Trim('"').Replace('/', '\\');
        return p.Length > 3 ? p.TrimEnd('\\') : p;
    }

    private static string Parent(string path)
    {
        var index = path.LastIndexOf('\\');
        return index <= 0 ? "" : path[..index];
    }
}

/// <summary>Lý do không cho xóa một đường dẫn trong trang Phân tích ổ đĩa.</summary>
public enum DeleteBlock
{
    None,

    /// <summary>Gốc ổ đĩa, thư mục Windows hoặc tệp hệ thống (pagefile, hiberfil...).</summary>
    System,

    /// <summary>Thư mục gốc quan trọng (Program Files, Users, AppData, Desktop...) hoặc thư mục chứa chúng.</summary>
    SystemFolder,

    /// <summary>Nằm trong (hoặc chứa) danh sách bảo vệ của người dùng.</summary>
    Protected,
}

/// <summary>Chặn xóa nhầm những vị trí làm hỏng Windows hoặc mất dữ liệu hàng loạt.</summary>
public static class PathGuard
{
    private static readonly HashSet<string> SystemNamesAtRoot = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$Recycle.Bin", "$WinREAgent", "$SysReset", "$Windows.~BT", "$Windows.~WS", "Recovery", "Boot", "EFI",
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp", "bootmgr", "BOOTNXT", "Config.Msi", "Documents and Settings",
    };

    private static readonly Lazy<(string Windows, string[] Roots)> Defaults = new(() =>
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var profile = F(Environment.SpecialFolder.UserProfile);
        var local = F(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = F(Environment.SpecialFolder.ProgramFiles);
        var roots = new[]
        {
            programFiles, F(Environment.SpecialFolder.ProgramFilesX86), F(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(programFiles, "WindowsApps"),
            profile, Path.GetDirectoryName(profile) ?? "", F(Environment.SpecialFolder.CommonDocuments),
            F(Environment.SpecialFolder.ApplicationData), local, Path.Combine(profile, @"AppData\LocalLow"), Path.Combine(profile, "AppData"),
            Path.Combine(local, "Programs"), Path.Combine(local, "Packages"), Path.Combine(local, "Microsoft"), F(Environment.SpecialFolder.CommonApplicationData) is { Length: > 0 } pd ? Path.Combine(pd, "Microsoft") : "",
            F(Environment.SpecialFolder.Desktop), F(Environment.SpecialFolder.MyDocuments), F(Environment.SpecialFolder.MyPictures),
            F(Environment.SpecialFolder.MyMusic), F(Environment.SpecialFolder.MyVideos), Path.Combine(profile, "Downloads"),
            Environment.GetEnvironmentVariable("OneDrive") ?? "",
        };
        return (F(Environment.SpecialFolder.Windows), roots.Where(r => r.Length > 0).ToArray());
    });

    public static DeleteBlock Check(string path) =>
        Check(path, Defaults.Value.Windows, Defaults.Value.Roots, ProtectedPaths.Patterns);

    public static DeleteBlock Check(string path, string windowsDir, IEnumerable<string> criticalRoots, IReadOnlyCollection<string> protectedPatterns)
    {
        if (string.IsNullOrWhiteSpace(path)) return DeleteBlock.System;
        var normalized = ProtectedPaths.Normalize(path);
        if (normalized.Length <= 3 || normalized.StartsWith(@"\\", StringComparison.Ordinal) && normalized.Count(c => c == '\\') <= 3) return DeleteBlock.System;
        if (!string.IsNullOrEmpty(windowsDir) && (LeftoverRules.SamePath(normalized, windowsDir) || LeftoverRules.IsUnder(normalized, windowsDir))) return DeleteBlock.System;

        // Tệp/thư mục hệ thống ngay ở gốc ổ (C:\pagefile.sys, D:\System Volume Information...) và mọi thứ bên trong chúng.
        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].EndsWith(':') && SystemNamesAtRoot.Contains(segments[1])) return DeleteBlock.System;

        foreach (var root in criticalRoots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (LeftoverRules.SamePath(normalized, root) || LeftoverRules.IsUnder(root, normalized)) return DeleteBlock.SystemFolder;
        }

        // Ứng dụng Store: không xóa tay bên trong WindowsApps.
        if (segments.Any(s => s.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase))) return DeleteBlock.System;

        if (ProtectedPaths.IsProtected(normalized, protectedPatterns) || ProtectedPaths.ContainsProtected(normalized, protectedPatterns)) return DeleteBlock.Protected;
        return DeleteBlock.None;
    }

    /// <summary>Nằm trong Program Files: nên gỡ bằng tab Phần mềm thay vì xóa tay (chỉ cảnh báo, không chặn).</summary>
    public static bool IsInsideProgramFiles(string path)
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (root.Length > 0 && LeftoverRules.IsUnder(path, root)) return true;
        }

        return false;
    }
}
