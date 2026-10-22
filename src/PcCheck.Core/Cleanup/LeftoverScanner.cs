using System.Xml.Linq;
using Microsoft.Win32;
using PcCheck.Core.Analysis;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Cleanup;

/// <summary>
/// Tìm phần còn sót của phần mềm SAU khi trình gỡ cài đặt đã chạy xong: thư mục cài, thư mục dữ liệu (AppData/ProgramData),
/// lối tắt Start Menu/Desktop, khóa registry Software\..., mục gỡ cài đặt mồ côi, tác vụ hẹn giờ, dịch vụ, mục tự chạy.
/// Chỉ đọc – việc xóa do <see cref="LeftoverRemover"/> thực hiện sau khi người dùng xác nhận.
/// </summary>
public static class LeftoverScanner
{
    private const long LargeDataBytes = 500L * 1024 * 1024;
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private sealed record Roots(string Windows, string[] Protected, (string Path, bool UserData)[] Search, string[] Shortcuts);

    private static Roots CurrentRoots()
    {
        string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var local = F(Environment.SpecialFolder.LocalApplicationData);
        var profile = F(Environment.SpecialFolder.UserProfile);
        var localLow = Path.Combine(profile, "AppData", "LocalLow");
        var protectedRoots = new[]
        {
            F(Environment.SpecialFolder.ProgramFiles), F(Environment.SpecialFolder.ProgramFilesX86), F(Environment.SpecialFolder.CommonApplicationData),
            F(Environment.SpecialFolder.ApplicationData), local, localLow, Path.Combine(local, "Programs"), profile,
            Path.GetDirectoryName(profile) ?? "", F(Environment.SpecialFolder.Desktop), F(Environment.SpecialFolder.CommonDesktopDirectory),
            F(Environment.SpecialFolder.MyDocuments), F(Environment.SpecialFolder.CommonDocuments), F(Environment.SpecialFolder.StartMenu),
            F(Environment.SpecialFolder.CommonStartMenu), F(Environment.SpecialFolder.Programs), F(Environment.SpecialFolder.CommonPrograms),
            F(Environment.SpecialFolder.Startup), F(Environment.SpecialFolder.CommonStartup), F(Environment.SpecialFolder.CommonProgramFiles),
            F(Environment.SpecialFolder.CommonProgramFilesX86), Path.GetTempPath(), Path.Combine(profile, "Downloads"),
        };
        return new Roots(
            F(Environment.SpecialFolder.Windows),
            protectedRoots.Where(r => r.Length > 0).ToArray(),
            [
                (F(Environment.SpecialFolder.ProgramFiles), false), (F(Environment.SpecialFolder.ProgramFilesX86), false),
                (Path.Combine(local, "Programs"), false), (F(Environment.SpecialFolder.CommonApplicationData), true),
                (F(Environment.SpecialFolder.ApplicationData), true), (local, true), (localLow, true),
                (F(Environment.SpecialFolder.CommonPrograms), false), (F(Environment.SpecialFolder.Programs), false),
            ],
            [
                F(Environment.SpecialFolder.CommonPrograms), F(Environment.SpecialFolder.Programs),
                F(Environment.SpecialFolder.Desktop), F(Environment.SpecialFolder.CommonDesktopDirectory),
            ]);
    }

    /// <param name="program">Phần mềm vừa gỡ (thông tin đọc TRƯỚC khi gỡ).</param>
    /// <param name="stillInstalled">Danh sách phần mềm còn lại sau khi gỡ (để không đụng vào thư mục của chúng).</param>
    public static List<Leftover> Scan(InstalledProgram program, IReadOnlyList<InstalledProgram> stillInstalled)
    {
        var list = new List<Leftover>();
        if (program.Scope == ProgramScope.Store) return list;

        // Phần mềm khác cùng tên sạch (vd 2 phiên bản song song) còn cài: không tìm theo tên để khỏi xóa nhầm.
        var cleanKey = LeftoverRules.Key(LeftoverRules.CleanName(program.Name));
        var sameNameInstalled = stillInstalled.Any(p => LeftoverRules.Key(LeftoverRules.CleanName(p.Name)) == cleanKey);

        var roots = CurrentRoots();
        var others = stillInstalled
            .SelectMany(p => new[] { p.InstallLocation, LeftoverRules.FolderOf(p.IconPath), LeftoverRules.FolderOf(p.UninstallCommand) })
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var folders = new List<(string Path, bool UserData, string Reason)>();
        void AddFolder(string? path, bool userData, string reason)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if (!Directory.Exists(path)) return;
            if (LeftoverRules.IsProtectedFolder(path, roots.Protected, roots.Windows, others)) return;
            if (folders.Any(f => LeftoverRules.SamePath(f.Path, path) || LeftoverRules.IsUnder(path, f.Path))) return;
            folders.RemoveAll(f => LeftoverRules.IsUnder(f.Path, path));
            folders.Add((path, userData, reason));
        }

        Safe(() => AddFolder(program.InstallLocation, false, "install_dir"));
        foreach (var source in new[] { program.IconPath, program.UninstallCommand })
        {
            var dir = LeftoverRules.FolderOf(source);
            if (dir is not null && LeftoverRules.FolderMatches(Path.GetFileName(dir), program)) Safe(() => AddFolder(dir, false, "install_dir"));
        }

        if (!sameNameInstalled && LeftoverRules.IsDistinctive(LeftoverRules.CleanName(program.Name)))
        {
            foreach (var (root, userData) in roots.Search)
            {
                Safe(() =>
                {
                    if (!Directory.Exists(root)) return;
                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        var name = Path.GetFileName(dir);
                        if (LeftoverRules.FolderMatches(name, program))
                        {
                            AddFolder(dir, userData, userData ? "data_dir" : "program_dir");
                        }
                        else if (LeftoverRules.PublisherMatches(name, program))
                        {
                            foreach (var sub in Directory.EnumerateDirectories(dir).Where(d => LeftoverRules.FolderMatches(Path.GetFileName(d), program)))
                                AddFolder(sub, userData, userData ? "data_dir" : "program_dir");
                        }
                    }
                });
            }
        }

        foreach (var (path, userData, reason) in folders)
        {
            var bytes = FolderSize(path);
            var large = userData && bytes > LargeDataBytes;
            list.Add(new Leftover
            {
                Kind = LeftoverKind.Folder,
                Path = path,
                Program = program.Name,
                Bytes = bytes,
                HighConfidence = !large,
                ReasonKey = large ? "leftover.reason.large_data" : "leftover.reason." + reason,
            });
        }

        var folderPaths = folders.Select(f => f.Path).ToList();
        bool InRemoved(string? file) => file is { Length: > 0 } && folderPaths.Any(f => LeftoverRules.IsUnder(file, f));
        bool Matches(string text) => !sameNameInstalled && LeftoverRules.IsDistinctive(LeftoverRules.CleanName(program.Name))
                                     && SecurityCatalog.ContainsWord(text, LeftoverRules.CleanName(program.Name));

        Safe(() => list.AddRange(Shortcuts(program, roots, folderPaths, InRemoved, Matches)));
        Safe(() => list.AddRange(RegistryKeys(program, sameNameInstalled)));
        Safe(() => list.AddRange(RunValues(program, InRemoved, Matches)));
        Safe(() => list.AddRange(Tasks(program, InRemoved, Matches)));
        Safe(() => list.AddRange(Services(program, InRemoved, Matches)));
        return list;
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            Log.Warn("Tìm phần còn sót lỗi", ex);
        }
    }

    private static long FolderSize(string path)
    {
        long total = 0;
        var count = 0;
        try
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                total += file.Length;
                if (++count > 200_000) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return total;
    }

    // ===== Lối tắt =====

    private static IEnumerable<Leftover> Shortcuts(InstalledProgram program, Roots roots, List<string> removedFolders, Func<string?, bool> inRemoved, Func<string, bool> matches)
    {
        foreach (var root in roots.Shortcuts.Where(Directory.Exists))
        {
            var recursive = !LeftoverRules.SamePath(root, Environment.GetFolderPath(Environment.SpecialFolder.Desktop))
                            && !LeftoverRules.SamePath(root, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true }).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (removedFolders.Any(f => LeftoverRules.IsUnder(file, f))) continue; // nằm trong thư mục Start Menu đã liệt kê
                var target = ShellLink.Resolve(file)?.Target ?? "";
                var broken = target.Length > 0 && !File.Exists(target) && !Directory.Exists(target);
                if (inRemoved(target) || (broken && matches(Path.GetFileNameWithoutExtension(file))))
                {
                    yield return new Leftover
                    {
                        Kind = LeftoverKind.Shortcut,
                        Path = file,
                        Program = program.Name,
                        HighConfidence = true,
                        ReasonKey = "leftover.reason.shortcut",
                    };
                }
            }
        }
    }

    // ===== Registry =====

    private static IEnumerable<Leftover> RegistryKeys(InstalledProgram program, bool sameNameInstalled)
    {
        // Mục gỡ cài đặt còn lại dù phần mềm đã gỡ (mồ côi).
        var parts = program.Id.Split('|');
        if (parts.Length == 3 && Enum.TryParse<RegistryHive>(parts[0], out var idHive) && Enum.TryParse<RegistryView>(parts[1], out var idView))
        {
            using var root = RegistryKey.OpenBaseKey(idHive, idView);
            using var key = root.OpenSubKey($@"{UninstallPath}\{parts[2]}");
            if (key is not null)
            {
                yield return new Leftover
                {
                    Kind = LeftoverKind.RegistryKey,
                    Path = $@"{HivePrefix(idHive)}\{(idView == RegistryView.Registry32 && idHive == RegistryHive.LocalMachine ? @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" : UninstallPath)}\{parts[2]}",
                    Program = program.Name,
                    HighConfidence = true,
                    ReasonKey = "leftover.reason.uninstall_entry",
                };
            }
        }

        if (sameNameInstalled) yield break;
        var names = new[] { program.Name, LeftoverRules.CleanName(program.Name) }.Where(LeftoverRules.IsDistinctive).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var publishers = new[] { program.Publisher, LeftoverRules.CleanPublisher(program.Publisher) }.Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var candidates = new List<string>();
        foreach (var name in names)
        {
            candidates.Add($@"Software\{name}");
            candidates.AddRange(publishers.Select(p => $@"Software\{p}\{name}"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hive, view, wow) in new[]
                 {
                     (RegistryHive.CurrentUser, RegistryView.Default, false),
                     (RegistryHive.LocalMachine, RegistryView.Registry64, false),
                     (RegistryHive.LocalMachine, RegistryView.Registry32, true),
                 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            foreach (var sub in candidates.Where(LeftoverRules.IsSafeRegistryKey))
            {
                using var key = root.OpenSubKey(sub);
                if (key is null) continue;
                var display = wow ? @"SOFTWARE\WOW6432Node" + sub[8..] : sub;
                var full = $@"{HivePrefix(hive)}\{display}";
                if (!seen.Add(full)) continue;
                yield return new Leftover
                {
                    Kind = LeftoverKind.RegistryKey,
                    Path = full,
                    Program = program.Name,
                    HighConfidence = true,
                    ReasonKey = "leftover.reason.settings",
                };
            }
        }
    }

    internal static string HivePrefix(RegistryHive hive) => hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";

    private static IEnumerable<Leftover> RunValues(InstalledProgram program, Func<string?, bool> inRemoved, Func<string, bool> matches)
    {
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        foreach (var (hive, view, label) in new[]
                 {
                     (RegistryHive.CurrentUser, RegistryView.Default, @"HKCU\" + run),
                     (RegistryHive.LocalMachine, RegistryView.Registry64, @"HKLM\" + run),
                     (RegistryHive.LocalMachine, RegistryView.Registry32, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
                 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var key = root.OpenSubKey(run);
            foreach (var name in key?.GetValueNames() ?? [])
            {
                var exe = SecurityCatalog.ExecutableOf(key!.GetValue(name)?.ToString() ?? "");
                var missing = exe.Length > 0 && Path.IsPathRooted(exe) && !File.Exists(exe);
                if (inRemoved(exe) || (missing && (matches(name) || matches(exe))))
                {
                    yield return new Leftover
                    {
                        Kind = LeftoverKind.RegistryValue,
                        Path = label,
                        ValueName = name,
                        Program = program.Name,
                        HighConfidence = true,
                        ReasonKey = "leftover.reason.autostart",
                    };
                }
            }
        }
    }

    private static IEnumerable<Leftover> Tasks(InstalledProgram program, Func<string?, bool> inRemoved, Func<string, bool> matches)
    {
        var root = Path.Combine(Environment.SystemDirectory, "Tasks");
        if (!Directory.Exists(root)) yield break;
        foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList())
        {
            var taskPath = file[root.Length..];
            if (taskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;
            string command;
            try
            {
                command = XDocument.Load(file).Descendants().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value.Trim().Trim('"') ?? "";
            }
            catch (Exception)
            {
                continue;
            }

            command = Environment.ExpandEnvironmentVariables(command);
            var missing = command.Length > 0 && Path.IsPathRooted(command) && !File.Exists(command);
            if (inRemoved(command) || (missing && (matches(Path.GetFileName(taskPath)) || matches(command))))
            {
                yield return new Leftover
                {
                    Kind = LeftoverKind.ScheduledTask,
                    Path = taskPath,
                    Program = program.Name,
                    HighConfidence = true,
                    ReasonKey = "leftover.reason.task",
                };
            }
        }
    }

    private static IEnumerable<Leftover> Services(InstalledProgram program, Func<string?, bool> inRemoved, Func<string, bool> matches)
    {
        using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
        foreach (var name in services?.GetSubKeyNames() ?? [])
        {
            using var key = services!.OpenSubKey(name);
            if (key?.GetValue("ImagePath") is not string image || key.GetValue("Type") is not int type || (type & 0x30) == 0) continue; // chỉ dịch vụ ứng dụng (Win32)
            var exe = SecurityCatalog.ExecutableOf(image);
            if (exe.Length == 0 || !Path.IsPathRooted(exe) || File.Exists(exe)) continue; // chỉ dịch vụ mà tệp chạy đã bị xóa
            var display = key.GetValue("DisplayName") as string ?? name;
            if (inRemoved(exe) || matches(display) || matches(name) || matches(exe))
            {
                yield return new Leftover
                {
                    Kind = LeftoverKind.Service,
                    Path = name,
                    Program = program.Name,
                    HighConfidence = true,
                    ReasonKey = "leftover.reason.service",
                };
            }
        }
    }
}
