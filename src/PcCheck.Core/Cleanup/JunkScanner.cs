using System.Diagnostics;
using System.IO.Enumeration;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Storage;
using PcCheck.Core.Util;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Cleanup;

/// <summary>Quét và dọn tệp rác. Chỉ xóa trong các thư mục rác đã biết, bỏ qua tệp đang bị khóa.</summary>
public static class JunkScanner
{
    public const string RecycleBinKey = "recycle";

    internal sealed record Location(string Root, string Pattern = "*", bool Recursive = true, bool DeleteEmptyDirs = true);

    internal sealed record Target(string Key, LocText Name, LocText Description, bool DefaultSelected, TimeSpan MinAge, Func<IEnumerable<Location>> Locations);

    private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Ứng dụng Electron / desktop có bộ đệm riêng (chỉ hiện khi có dữ liệu). Không đụng tới tin nhắn, cài đặt hay đăng nhập.</summary>
    private static readonly (string Name, Func<IEnumerable<string>> Caches)[] AppCaches =
    [
        ("Discord", () => ElectronCaches(Path.Combine(Roaming, "discord"))),
        ("Slack", () => ElectronCaches(Path.Combine(Roaming, "Slack"))),
        ("Microsoft Teams", () => ElectronCaches(Path.Combine(Roaming, @"Microsoft\Teams"))),
        ("Visual Studio Code", () => [.. ElectronCaches(Path.Combine(Roaming, "Code")), Path.Combine(Roaming, @"Code\CachedData"), Path.Combine(Roaming, @"Code\CachedExtensionVSIXs")]),
        ("Spotify", () => [Path.Combine(Local, @"Spotify\Browser\Cache"), Path.Combine(Local, @"Spotify\Browser\GPUCache")]),
        ("Steam", () => [Path.Combine(Local, @"Steam\htmlcache\Cache"), Path.Combine(Local, @"Steam\htmlcache\Code Cache"), Path.Combine(Local, @"Steam\htmlcache\GPUCache")]),
        ("Epic Games Launcher", () => [Path.Combine(Local, @"EpicGamesLauncher\Saved\webcache"), Path.Combine(Local, @"EpicGamesLauncher\Saved\webcache_4147"), Path.Combine(Local, @"EpicGamesLauncher\Saved\webcache_4430")]),
    ];

    private static IEnumerable<string> ElectronCaches(string root) =>
        new[] { "Cache", "Code Cache", "GPUCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", @"Service Worker\ScriptCache" }.Select(c => Path.Combine(root, c));

    private static readonly (string Name, Func<string> UserData)[] ChromiumBrowsers =
    [
        ("Google Chrome", () => Path.Combine(Local, @"Google\Chrome\User Data")),
        ("Microsoft Edge", () => Path.Combine(Local, @"Microsoft\Edge\User Data")),
        ("Cốc Cốc", () => Path.Combine(Local, @"CocCoc\Browser\User Data")),
        ("Brave", () => Path.Combine(Local, @"BraveSoftware\Brave-Browser\User Data")),
        ("Vivaldi", () => Path.Combine(Local, @"Vivaldi\User Data")),
        ("Opera", () => Path.Combine(Local, @"Opera Software")),
    ];

    internal static IReadOnlyList<Target> Targets()
    {
        var targets = new List<Target>
        {
            new("user-temp", L("junk.user_temp.name"), L("junk.user_temp.desc"), true, TimeSpan.FromHours(24),
                () => [new(Path.GetTempPath())]),
            new("windows-temp", L("junk.windows_temp.name"), L("junk.windows_temp.desc"), true, TimeSpan.FromHours(24),
                () => [new(Path.Combine(Windows, "Temp"))]),
            new("wu-cache", L("junk.wu_cache.name"), L("junk.wu_cache.desc"), true, TimeSpan.FromDays(3),
                () => [new(Path.Combine(Windows, @"SoftwareDistribution\Download"))]),
            new("delivery-opt", L("junk.delivery_opt.name"), L("junk.delivery_opt.desc"), true, TimeSpan.FromDays(1),
                () => [new(Path.Combine(Windows, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"))]),
            new("error-reports", L("junk.error_reports.name"), L("junk.error_reports.desc"), true, TimeSpan.Zero,
                () =>
                [
                    new(Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive")),
                    new(Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportQueue")),
                    new(Path.Combine(ProgramData, @"Microsoft\Windows\WER\Temp")),
                    new(Path.Combine(Local, @"Microsoft\Windows\WER")),
                ]),
            new("windows-logs", L("junk.windows_logs.name"), L("junk.windows_logs.desc"), true, TimeSpan.FromDays(7),
                () =>
                [
                    new(Path.Combine(Windows, "Logs"), "*.log"),
                    new(Path.Combine(Windows, "Logs"), "*.etl"),
                    new(Path.Combine(Windows, "Logs"), "*.cab"),
                    new(Path.Combine(Windows, @"Panther"), "*.log", Recursive: false, DeleteEmptyDirs: false),
                ]),
            new("thumbnails", L("junk.thumbnails.name"), L("junk.thumbnails.desc"), false, TimeSpan.Zero,
                () =>
                [
                    new(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "thumbcache_*.db", Recursive: false, DeleteEmptyDirs: false),
                    new(Path.Combine(Local, @"Microsoft\Windows\Explorer"), "iconcache_*.db", Recursive: false, DeleteEmptyDirs: false),
                ]),
            new("crash-dumps", L("junk.crash_dumps.name"), L("junk.crash_dumps.desc"), false, TimeSpan.Zero,
                () =>
                [
                    new(Path.Combine(Local, "CrashDumps")),
                    new(Path.Combine(Windows, "Minidump")),
                    new(Path.Combine(Windows, "LiveKernelReports"), "*.dmp"),
                    new(Windows, "MEMORY.DMP", Recursive: false, DeleteEmptyDirs: false),
                ]),
            new("shader-cache", L("junk.shader_cache.name"), L("junk.shader_cache.desc"), false, TimeSpan.Zero,
                () =>
                [
                    new(Path.Combine(Local, "D3DSCache")),
                    new(Path.Combine(Local, @"NVIDIA\DXCache")),
                    new(Path.Combine(Local, @"NVIDIA\GLCache")),
                    new(Path.Combine(Local, @"AMD\DxCache")),
                    new(Path.Combine(Local, @"AMD\DxcCache")),
                    new(Path.Combine(Local, @"AMD\VkCache")),
                    new(Path.Combine(Local, @"Intel\ShaderCache")),
                ]),
        };

        targets.Add(new Target("inet-cache", L("junk.inet_cache.name"), L("junk.inet_cache.desc"), true, TimeSpan.FromDays(1),
            () => [new(Path.Combine(Local, @"Microsoft\Windows\INetCache"))]));

        targets.Add(new Target("dev-caches", L("junk.dev_caches.name"), L("junk.dev_caches.desc"), false, TimeSpan.Zero,
            () =>
            [
                new(Path.Combine(Local, @"npm-cache\_cacache")),
                new(Path.Combine(Local, @"Yarn\Cache")),
                new(Path.Combine(Local, @"pip\cache")),
                new(Path.Combine(Local, @"NuGet\v3-cache")),
                new(Path.Combine(Local, @"NuGet\plugins-cache")),
                new(Path.Combine(Local, "go-build")),
                new(Path.Combine(Local, @"Composer\cache")),
                new(Path.Combine(Local, @"ms-playwright-go")),
                new(Path.Combine(Profile, @".cache\pre-commit")),
            ]));

        foreach (var (name, caches) in AppCaches)
        {
            targets.Add(new Target("app:" + name, L("junk.app.name", name), L("junk.app.desc"), true, TimeSpan.Zero,
                () => caches().Where(Directory.Exists).Select(c => new Location(c))));
        }

        foreach (var (name, userData) in ChromiumBrowsers)
        {
            targets.Add(new Target("browser:" + name, L("junk.browser.name", name), L("junk.browser.desc"), true, TimeSpan.Zero,
                () => ChromiumCacheLocations(userData())));
        }

        targets.Add(new Target("browser:Firefox", L("junk.browser.name", "Firefox"), L("junk.browser.desc"), true, TimeSpan.Zero,
            FirefoxCacheLocations));

        return targets;
    }

    private static IEnumerable<Location> ChromiumCacheLocations(string userData)
    {
        if (!Directory.Exists(userData)) yield break;

        // Mỗi thư mục con có "Cache" / "Code Cache" / "GPUCache" là một hồ sơ (Default, Profile 1, ...) – riêng Opera nằm 1 cấp sâu hơn.
        var profiles = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(userData))
            {
                profiles.Add(dir);
                if (Path.GetFileName(dir).StartsWith("Opera", StringComparison.OrdinalIgnoreCase))
                {
                    profiles.AddRange(Directory.EnumerateDirectories(dir));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        foreach (var profile in profiles)
        {
            foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache", @"Service Worker\ScriptCache" })
            {
                var path = Path.Combine(profile, cache);
                if (Directory.Exists(path)) yield return new Location(path);
            }
        }

        foreach (var shared in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" })
        {
            var path = Path.Combine(userData, shared);
            if (Directory.Exists(path)) yield return new Location(path);
        }
    }

    private static IEnumerable<Location> FirefoxCacheLocations()
    {
        var profiles = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (!Directory.Exists(profiles)) yield break;
        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(profiles);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var profile in dirs)
        {
            var cache = Path.Combine(profile, "cache2");
            if (Directory.Exists(cache)) yield return new Location(cache);
        }
    }

    private static EnumerationOptions Options(bool recursive) => new()
    {
        RecurseSubdirectories = recursive,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Quét kích thước từng nhóm rác (song song, không xóa gì).</summary>
    public static async Task<List<JunkCategory>> ScanAsync(CancellationToken ct = default)
    {
        var targets = Targets();
        var categories = new JunkCategory[targets.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, targets.Count), new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, (i, token) =>
        {
            var target = targets[i];
            var category = new JunkCategory
            {
                Key = target.Key,
                Name = target.Name,
                Description = target.Description,
                DefaultSelected = target.DefaultSelected,
            };
            var cutoff = DateTime.UtcNow - target.MinAge;
            try
            {
                foreach (var location in target.Locations())
                {
                    if (!Directory.Exists(location.Root) || ProtectedPaths.IsProtected(location.Root)) continue;
                    category.Roots.Add(location.Root);
                    foreach (var (size, _) in EnumerateFiles(location, cutoff))
                    {
                        token.ThrowIfCancellationRequested();
                        category.Bytes += size;
                        category.Files++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn($"Quét rác {target.Key} lỗi", ex);
            }

            categories[i] = category;
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        var result = categories.Where(c => c.Files > 0 || !(c.Key.StartsWith("browser:", StringComparison.Ordinal) || c.Key.StartsWith("app:", StringComparison.Ordinal) || c.Key == "dev-caches")).ToList();
        result.Add(ScanRecycleBin());
        return result;
    }

    private static IEnumerable<(long Size, string Path)> EnumerateFiles(Location location, DateTime cutoffUtc)
    {
        var guarded = ProtectedPaths.Patterns.Count > 0;
        var enumerable = new FileSystemEnumerable<(long, string)>(
            location.Root,
            (ref FileSystemEntry entry) => (entry.Length, entry.ToFullPath()),
            Options(location.Recursive))
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                !entry.IsDirectory
                && entry.LastWriteTimeUtc.UtcDateTime <= cutoffUtc
                && (location.Pattern == "*" || FileSystemName.MatchesSimpleExpression(location.Pattern, entry.FileName)),
        };
        return guarded ? enumerable.Where(e => !ProtectedPaths.IsProtected(e.Item2)) : enumerable;
    }

    private static JunkCategory ScanRecycleBin()
    {
        var category = new JunkCategory
        {
            Key = RecycleBinKey,
            Name = L("junk.recycle.name"),
            Description = L("junk.recycle.desc"),
            DefaultSelected = false,
        };
        try
        {
            var info = new NativeMethods.ShQueryRbInfo { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.ShQueryRbInfo>() };
            if (NativeMethods.SHQueryRecycleBin(null, ref info) == 0)
            {
                category.Bytes = info.i64Size;
                category.Files = (int)Math.Min(info.i64NumItems, int.MaxValue);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc thùng rác lỗi", ex);
        }

        return category;
    }

    /// <summary>Xóa các nhóm đã chọn. Tệp đang dùng/bị khóa sẽ được bỏ qua.</summary>
    public static async Task<CleanResult> CleanAsync(IEnumerable<string> keys, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        long freed = 0;
        int deleted = 0, skipped = 0;
        var history = new List<OperationRecord>();

        await Task.Run(() =>
        {
            foreach (var target in Targets().Where(t => selected.Contains(t.Key)))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(target.Name.ToString());
                var cutoff = DateTime.UtcNow - target.MinAge;
                long categoryFreed = 0;
                int categoryDeleted = 0, categorySkipped = 0;
                foreach (var location in target.Locations())
                {
                    if (!Directory.Exists(location.Root) || ProtectedPaths.IsProtected(location.Root)) continue;
                    foreach (var (size, path) in EnumerateFiles(location, cutoff).ToList())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (TryDelete(path))
                        {
                            categoryFreed += size;
                            categoryDeleted++;
                        }
                        else
                        {
                            categorySkipped++;
                        }
                    }

                    if (location.DeleteEmptyDirs && location.Recursive) DeleteEmptyDirectories(location.Root);
                }

                freed += categoryFreed;
                deleted += categoryDeleted;
                skipped += categorySkipped;
                if (categoryDeleted > 0 || categorySkipped > 0)
                {
                    history.Add(new OperationRecord
                    {
                        Time = DateTime.Now, Source = OperationLog.SourceJunk, Action = OperationLog.ActionRemoved, Target = target.Name.ToString(),
                        Bytes = categoryFreed, Count = categoryDeleted, Detail = categorySkipped > 0 ? $"skipped {categorySkipped}" : null,
                    });
                }
            }

            if (selected.Contains(RecycleBinKey))
            {
                progress?.Report(Loc.T("junk.recycle.name"));
                var before = ScanRecycleBin();
                try
                {
                    var hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null,
                        NativeMethods.SherbNoConfirmation | NativeMethods.SherbNoProgressUi | NativeMethods.SherbNoSound);
                    if (hr == 0)
                    {
                        freed += before.Bytes;
                        deleted += before.Files;
                        history.Add(new OperationRecord
                        {
                            Time = DateTime.Now, Source = OperationLog.SourceJunk, Action = OperationLog.ActionRemoved, Target = Loc.T("junk.recycle.name"),
                            Bytes = before.Bytes, Count = before.Files,
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Dọn thùng rác lỗi", ex);
                }
            }
        }, ct).ConfigureAwait(false);

        OperationLog.Record(history);
        Log.Info($"Dọn rác: giải phóng {freed} byte, xóa {deleted} tệp, bỏ qua {skipped}");
        return new CleanResult(freed, deleted, skipped);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void DeleteEmptyDirectories(string root)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", Options(true)).OrderByDescending(d => d.Length).ToList())
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Mở trình dọn dẹp ổ đĩa của Windows (để xóa Windows.old / bản cập nhật cũ an toàn).</summary>
    public static void OpenDiskCleanup()
    {
        Process.Start(new ProcessStartInfo("cleanmgr.exe") { UseShellExecute = true });
    }
}
