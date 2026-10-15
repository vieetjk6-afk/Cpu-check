using System.Globalization;
using System.Text;
using Microsoft.Win32;
using PcCheck.Core.Analysis;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

/// <summary>Liệt kê phần mềm đã cài + ước lượng lần dùng cuối (UserAssist + Prefetch).</summary>
public static class SoftwareCollector
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private static readonly string[] IgnoredExePrefixes = ["unins", "uninst", "update", "updater", "crash", "setup", "install", "maintenance", "helper", "elevat", "notif", "report"];

    private static readonly string[] GenericExeNames = ["SETUP.EXE", "UPDATE.EXE", "LAUNCHER.EXE", "INSTALL.EXE", "UNINSTALL.EXE", "APP.EXE", "MAIN.EXE"];

    public static List<InstalledProgram> Collect()
    {
        var programs = new List<InstalledProgram>();
        ReadUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry64, ProgramScope.Machine, programs);
        ReadUninstallKeys(RegistryHive.LocalMachine, RegistryView.Registry32, ProgramScope.Machine, programs);
        ReadUninstallKeys(RegistryHive.CurrentUser, RegistryView.Default, ProgramScope.User, programs);

        programs = programs
            .GroupBy(p => (p.Name.ToUpperInvariant(), p.Version))
            .Select(g => g.First())
            .ToList();

        var usage = ReadUsage();
        foreach (var program in programs)
        {
            ApplyUsage(program, usage);
            program.IsEssential = SoftwareCatalog.IsEssential(program.Name, program.Publisher);
            program.BloatReasonKey = SoftwareCatalog.BloatReason(program.Name, program.Publisher);
        }

        programs.AddRange(ReadStoreBloat());
        return programs.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadUninstallKeys(RegistryHive hive, RegistryView view, ProgramScope scope, List<InstalledProgram> output)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var root = baseKey.OpenSubKey(UninstallPath);
            if (root is null) return;

            foreach (var subName in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(subName);
                    if (key is null) continue;
                    var program = ReadProgram(key, subName, scope, $"{hive}|{view}|{subName}");
                    if (program is not null) output.Add(program);
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
                {
                    // Bỏ qua khóa không đọc được.
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Đọc danh sách phần mềm {hive}/{view} lỗi", ex);
        }
    }

    private static InstalledProgram? ReadProgram(RegistryKey key, string subName, ProgramScope scope, string id)
    {
        var name = (key.GetValue("DisplayName") as string)?.Trim();
        if (string.IsNullOrEmpty(name)) return null;
        if (key.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (key.GetValue("ParentKeyName") is string) return null;
        var releaseType = key.GetValue("ReleaseType") as string ?? "";
        if (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) || releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)) return null;

        var uninstall = key.GetValue("UninstallString") as string ?? "";
        if (uninstall.Length == 0 && key.GetValue("WindowsInstaller") is int wi && wi == 1 && subName.StartsWith('{'))
        {
            uninstall = $"MsiExec.exe /X{subName}";
        }

        if (uninstall.Length == 0) return null;

        var program = new InstalledProgram
        {
            Id = id,
            Name = name,
            Publisher = (key.GetValue("Publisher") as string)?.Trim() ?? "",
            Version = (key.GetValue("DisplayVersion") as string)?.Trim() ?? "",
            UninstallCommand = uninstall,
            QuietUninstallCommand = key.GetValue("QuietUninstallString") as string ?? "",
            InstallLocation = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"') ?? "",
            IconPath = key.GetValue("DisplayIcon") as string ?? "",
            Scope = scope,
        };

        if (key.GetValue("EstimatedSize") is int kb && kb > 0) program.SizeBytes = kb * 1024L;

        if (key.GetValue("InstallDate") is string date
            && DateTime.TryParseExact(date.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var installed))
        {
            program.InstallDate = installed;
        }
        else if (program.InstallLocation.Length > 0)
        {
            try
            {
                if (Directory.Exists(program.InstallLocation)) program.InstallDate = Directory.GetCreationTime(program.InstallLocation);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return program;
    }

    internal sealed record UsageInfo(DateTime LastUsed, int RunCount);

    internal sealed class UsageMap
    {
        public Dictionary<string, UsageInfo> ByExe { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, UsageInfo> ByShortcut { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(Dictionary<string, UsageInfo> map, string name, DateTime time, int count)
        {
            if (map.TryGetValue(name, out var existing))
            {
                map[name] = new UsageInfo(time > existing.LastUsed ? time : existing.LastUsed, existing.RunCount + count);
            }
            else
            {
                map[name] = new UsageInfo(time, count);
            }
        }
    }

    internal static UsageMap ReadUsage()
    {
        var map = new UsageMap();
        ReadUserAssist(map);
        ReadPrefetch(map);
        return map;
    }

    private static void ReadUserAssist(UsageMap map)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist");
            if (root is null) return;
            foreach (var guid in root.GetSubKeyNames())
            {
                using var count = root.OpenSubKey(guid + @"\Count");
                if (count is null) continue;
                foreach (var valueName in count.GetValueNames())
                {
                    if (count.GetValue(valueName) is not byte[] { Length: >= 68 } data) continue;
                    var path = Rot13(valueName);
                    var runs = BitConverter.ToInt32(data, 4);
                    var fileTime = BitConverter.ToInt64(data, 60);
                    if (fileTime <= 0) continue;
                    DateTime time;
                    try
                    {
                        time = DateTime.FromFileTime(fileTime);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        continue;
                    }

                    var fileName = SafeFileName(path);
                    if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        map.Add(map.ByExe, fileName, time, Math.Max(runs, 0));
                    else if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        map.Add(map.ByShortcut, Path.GetFileNameWithoutExtension(fileName), time, Math.Max(runs, 0));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc UserAssist lỗi", ex);
        }
    }

    private static void ReadPrefetch(UsageMap map)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            if (!Directory.Exists(dir)) return;
            foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.pf"))
            {
                var name = file.Name;
                var dash = name.LastIndexOf('-');
                if (dash <= 0) continue;
                map.Add(map.ByExe, name[..dash], file.LastWriteTime, 0);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Prefetch cần quyền admin.
        }
    }

    internal static string Rot13(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
        {
            sb.Append(c switch
            {
                >= 'a' and <= 'z' => (char)('a' + (c - 'a' + 13) % 26),
                >= 'A' and <= 'Z' => (char)('A' + (c - 'A' + 13) % 26),
                _ => c,
            });
        }

        return sb.ToString();
    }

    private static string SafeFileName(string path)
    {
        var idx = path.LastIndexOfAny(['\\', '/']);
        return idx >= 0 ? path[(idx + 1)..] : path;
    }

    internal static void ApplyUsage(InstalledProgram program, UsageMap usage)
    {
        UsageInfo? best = null;
        foreach (var exe in CandidateExecutables(program))
        {
            if (GenericExeNames.Contains(exe.ToUpperInvariant())) continue;
            if (usage.ByExe.TryGetValue(exe, out var info) && (best is null || info.LastUsed > best.LastUsed)) best = info;
        }

        foreach (var (shortcut, info) in usage.ByShortcut)
        {
            if (shortcut.Length < 4) continue;
            var match = program.Name.Equals(shortcut, StringComparison.OrdinalIgnoreCase)
                        || program.Name.StartsWith(shortcut + " ", StringComparison.OrdinalIgnoreCase);
            if (match && (best is null || info.LastUsed > best.LastUsed)) best = info;
        }

        if (best is not null)
        {
            program.LastUsed = best.LastUsed;
            program.RunCount = best.RunCount > 0 ? best.RunCount : null;
        }
    }

    private static IEnumerable<string> CandidateExecutables(InstalledProgram program)
    {
        var icon = program.IconPath.Split(',')[0].Trim().Trim('"');
        if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) yield return Path.GetFileName(icon);

        if (program.InstallLocation.Length == 0) yield break;
        string[] files;
        try
        {
            files = Directory.Exists(program.InstallLocation)
                ? Directory.GetFiles(program.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files = [];
        }

        foreach (var file in files.Take(40))
        {
            var name = Path.GetFileName(file);
            if (IgnoredExePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
            yield return name;
        }
    }

    /// <summary>App Store cài sẵn thường ít dùng (đọc từ registry, nhanh hơn gọi PowerShell).</summary>
    private static IEnumerable<InstalledProgram> ReadStoreBloat()
    {
        var result = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (root is null) return [];
            foreach (var fullName in root.GetSubKeyNames())
            {
                var packageName = fullName.Split('_')[0];
                var friendly = SoftwareCatalog.StoreBloatName(packageName);
                if (friendly is null || result.ContainsKey(packageName)) continue;
                result[packageName] = new InstalledProgram
                {
                    Id = "store|" + packageName,
                    Name = friendly,
                    Publisher = "Microsoft Store",
                    Version = fullName.Split('_').ElementAtOrDefault(1) ?? "",
                    PackageName = packageName,
                    Scope = ProgramScope.Store,
                    BloatReasonKey = SoftwareCatalog.ReasonStoreApp,
                };
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc danh sách app Store lỗi", ex);
        }

        return result.Values;
    }
}
