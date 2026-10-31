using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Storage;

/// <summary>
/// Tìm thư mục build và thư viện tải về của các dự án lập trình (node_modules, target, bin/obj, .venv, .next...) –
/// thường chiếm hàng chục GB và tạo lại được bằng một lệnh build/cài đặt. Mỗi loại chỉ được nhận khi thư mục cha có
/// tệp dự án tương ứng (package.json, Cargo.toml, *.csproj...), nên thư mục "build"/"bin" bình thường không bị đụng tới.
/// </summary>
public static class ProjectPurge
{
    public const int MaxDepth = 6;

    /// <summary>Dự án có thay đổi trong số ngày này được coi là "đang làm" và không chọn sẵn.</summary>
    public const int RecentDays = 7;

    /// <summary>Một loại thư mục cần dọn: tên, có phải thư viện tải về không, và điều kiện để nhận.</summary>
    internal sealed record Rule(string Name, bool IsDependency, Func<string, string, bool> Matches);

    private static bool ParentHas(string parent, params string[] files) => files.Any(f => File.Exists(Path.Combine(parent, f)));

    private static bool ParentHasPattern(string parent, params string[] patterns)
    {
        try
        {
            return patterns.Any(p => Directory.EnumerateFiles(parent, p).Any());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static readonly string[] JsProject = ["package.json"];
    private static readonly string[] GradleProject = ["build.gradle", "build.gradle.kts", "settings.gradle", "settings.gradle.kts"];
    private static readonly string[] DotnetProject = ["*.csproj", "*.fsproj", "*.vbproj"];

    internal static readonly IReadOnlyList<Rule> Rules =
    [
        new("node_modules", true, (parent, _) => ParentHas(parent, JsProject)),
        new("target", false, (parent, _) => ParentHas(parent, "Cargo.toml", "pom.xml")),
        new("build", false, (parent, _) => ParentHas(parent, [.. GradleProject, "package.json", "pubspec.yaml", "CMakeLists.txt"])),
        new("dist", false, (parent, _) => ParentHas(parent, "package.json", "pyproject.toml", "setup.py")),
        new("bin", false, (parent, _) => ParentHasPattern(parent, DotnetProject)),
        new("obj", false, (parent, _) => ParentHasPattern(parent, DotnetProject)),
        new(".venv", true, (_, dir) => File.Exists(Path.Combine(dir, "pyvenv.cfg"))),
        new("venv", true, (_, dir) => File.Exists(Path.Combine(dir, "pyvenv.cfg"))),
        new("__pycache__", false, (_, _) => true),
        new(".pytest_cache", false, (_, _) => true),
        new(".mypy_cache", false, (_, _) => true),
        new(".ruff_cache", false, (_, _) => true),
        new(".tox", true, (parent, _) => ParentHas(parent, "tox.ini", "pyproject.toml", "setup.cfg")),
        new(".nox", true, (parent, _) => ParentHas(parent, "noxfile.py")),
        new(".gradle", false, (parent, _) => ParentHas(parent, GradleProject)),
        new(".cxx", false, (parent, _) => ParentHas(parent, GradleProject)),
        new(".next", false, (parent, _) => ParentHas(parent, JsProject)),
        new(".nuxt", false, (parent, _) => ParentHas(parent, JsProject)),
        new(".output", false, (parent, _) => ParentHas(parent, JsProject)),
        new(".svelte-kit", false, (parent, _) => ParentHas(parent, JsProject)),
        new(".angular", false, (parent, _) => ParentHas(parent, JsProject)),
        new(".astro", false, (parent, _) => ParentHas(parent, JsProject)),
        new(".turbo", false, (parent, _) => ParentHas(parent, JsProject)),
        new(".parcel-cache", false, (parent, _) => ParentHas(parent, JsProject)),
        new(".expo", false, (parent, _) => ParentHas(parent, JsProject)),
        new("coverage", false, (parent, _) => ParentHas(parent, JsProject)),
        new("vendor", true, (parent, dir) => ParentHas(parent, "composer.json") && File.Exists(Path.Combine(dir, "autoload.php"))),
        new(".dart_tool", false, (parent, _) => ParentHas(parent, "pubspec.yaml")),
        new("Pods", true, (parent, _) => ParentHas(parent, "Podfile")),
        new(".zig-cache", false, (parent, _) => ParentHas(parent, "build.zig")),
        new("zig-out", false, (parent, _) => ParentHas(parent, "build.zig")),
        new(".terragrunt-cache", true, (parent, _) => ParentHas(parent, "terragrunt.hcl")),
    ];

    private static readonly Dictionary<string, Rule> RuleByName = Rules.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Không đi vào những thư mục này khi tìm (lịch sử git, thư mục hệ thống/ứng dụng).</summary>
    private static readonly HashSet<string> Prune = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn", ".vs", ".idea", "AppData", "Program Files", "Program Files (x86)", "Windows", "$Recycle.Bin", "System Volume Information",
    };

    public static bool IsTargetName(string name) => RuleByName.ContainsKey(name);

    /// <summary>Thư mục dự án mặc định (có thì dùng) + thư mục người dùng tự thêm trong Cài đặt.</summary>
    public static List<string> SearchRoots(IEnumerable<string>? custom = null)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var candidates = new List<string>
        {
            Path.Combine(profile, "source"), Path.Combine(profile, "Projects"), Path.Combine(profile, "Project"), Path.Combine(profile, "GitHub"),
            Path.Combine(profile, "Code"), Path.Combine(profile, "dev"), Path.Combine(profile, "Workspace"), Path.Combine(profile, "Repos"),
            Path.Combine(profile, "Development"), Path.Combine(profile, "www"), Path.Combine(documents, "GitHub"), Path.Combine(documents, "Projects"),
            Path.Combine(documents, "Code"),
        };
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                foreach (var name in new[] { "Projects", "Project", "Code", "dev", "GitHub", "Repos", "Workspace", "source", "xampp\\htdocs", "laragon\\www" })
                {
                    candidates.Add(Path.Combine(drive.RootDirectory.FullName, name));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (custom is not null) candidates.AddRange(custom.Where(c => !string.IsNullOrWhiteSpace(c)));

        // Bỏ trùng và thư mục con của thư mục khác trong danh sách.
        var existing = candidates.Where(Directory.Exists).Select(p => Path.GetFullPath(p).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return existing.Where(p => !existing.Any(o => Cleanup.LeftoverRules.IsUnder(p, o))).ToList();
    }

    public static Task<List<ProjectArtifact>> ScanAsync(IEnumerable<string> roots, IProgress<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Scan(roots.ToList(), progress, ct), ct);

    private static List<ProjectArtifact> Scan(List<string> roots, IProgress<string>? progress, CancellationToken ct)
    {
        var found = new List<ProjectArtifact>();
        var nowUtc = DateTime.UtcNow;
        foreach (var root in roots)
        {
            progress?.Report(root);
            Walk(root, 0);
        }

        // Đo dung lượng song song.
        Parallel.ForEach(found, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 6) }, artifact =>
        {
            var (bytes, files, _) = DiskAnalyzer.Measure(artifact.Path, ct);
            artifact.Bytes = bytes;
            artifact.Files = files;
        });

        return found.Where(a => a.Bytes > 0).OrderByDescending(a => a.Bytes).ToList();

        void Walk(string dir, int depth)
        {
            if (depth > MaxDepth) return;
            ct.ThrowIfCancellationRequested();
            List<string> children;
            try
            {
                children = Directory.EnumerateDirectories(dir).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (Prune.Contains(name) || IsLinkOrSystem(child)) continue;
                if (RuleByName.TryGetValue(name, out var rule))
                {
                    if (rule.Matches(dir, child) && !ProtectedPaths.IsProtected(child) && !ProtectedPaths.ContainsProtected(child))
                    {
                        var activity = LastActivity(dir, child);
                        found.Add(new ProjectArtifact
                        {
                            ProjectPath = dir,
                            ProjectName = Path.GetFileName(dir.TrimEnd('\\')),
                            Path = child,
                            Kind = rule.Name,
                            IsDependency = rule.IsDependency,
                            LastActivityUtc = activity,
                            IsRecent = (nowUtc - activity).TotalDays < RecentDays,
                        });
                        continue; // không đi vào bên trong thư mục build
                    }

                    // "build"/"bin" không thuộc dự án nhận diện được: có thể là dữ liệu thật – vẫn tìm tiếp bên trong.
                }

                Walk(child, depth + 1);
            }
        }
    }

    private static bool IsLinkOrSystem(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Lần sửa gần nhất của dự án: tệp ở cấp ngoài cùng của dự án (không tính thư mục build) và chính thư mục build.</summary>
    private static DateTime LastActivity(string project, string artifact)
    {
        var newest = DateTime.MinValue;
        try
        {
            newest = Directory.GetLastWriteTimeUtc(artifact);
            foreach (var file in new DirectoryInfo(project).EnumerateFiles())
            {
                if (file.LastWriteTimeUtc > newest) newest = file.LastWriteTimeUtc;
            }

            foreach (var dir in new DirectoryInfo(project).EnumerateDirectories())
            {
                if (IsTargetName(dir.Name) || Prune.Contains(dir.Name)) continue;
                if (dir.LastWriteTimeUtc > newest) newest = dir.LastWriteTimeUtc;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Đọc thời gian dự án {project} lỗi", ex);
        }

        return newest == DateTime.MinValue ? DateTime.UtcNow : newest;
    }

    /// <summary>Xóa vĩnh viễn (giống "mo purge": thư mục build tạo lại được, quá lớn cho Thùng rác).</summary>
    public static Task<DeleteResult> DeleteAsync(ProjectArtifact artifact, CancellationToken ct = default) => Task.Run(() =>
    {
        // Kiểm tra lại ngay trước khi xóa: thư mục vẫn đúng là thư mục build của dự án và không bị bảo vệ.
        var parent = Path.GetDirectoryName(artifact.Path.TrimEnd('\\'));
        if (parent is null || !RuleByName.TryGetValue(Path.GetFileName(artifact.Path.TrimEnd('\\')), out var rule) || !rule.Matches(parent, artifact.Path)
            || ProtectedPaths.IsProtected(artifact.Path) || ProtectedPaths.ContainsProtected(artifact.Path) || PathGuard.Check(artifact.Path) != DeleteBlock.None)
        {
            Log.Warn($"Bỏ qua (không còn hợp lệ để xóa): {artifact.Path}");
            return new DeleteResult(0, 0, 1);
        }

        return FileDeleter.DeleteTree(artifact.Path, ct);
    }, ct);
}
