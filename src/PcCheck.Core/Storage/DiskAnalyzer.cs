using System.Diagnostics;
using System.IO.Enumeration;
using PcCheck.Core.Cleanup;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Storage;

/// <summary>Tiến độ quét (được đọc định kỳ từ giao diện).</summary>
public sealed class DiskScanProgress
{
    private long _files;
    private long _bytes;

    public long Files => Interlocked.Read(ref _files);
    public long Bytes => Interlocked.Read(ref _bytes);

    internal void Add(long files, long bytes)
    {
        Interlocked.Add(ref _files, files);
        Interlocked.Add(ref _bytes, bytes);
    }
}

/// <summary>
/// Phân tích dung lượng ổ đĩa: kích thước từng thư mục con (quét song song), các tệp lớn nhất và gợi ý những vị trí
/// hay chiếm chỗ có thể dọn. Không đi theo liên kết (junction/symlink) để không đếm trùng; tệp chỉ có trên đám mây
/// (OneDrive "Files On-Demand") không tính vì không chiếm chỗ trên ổ.
/// </summary>
public static class DiskAnalyzer
{
    public const int LargeFileCount = 60;
    public const long LargeFileMinBytes = 50L * 1024 * 1024;

    // Tệp chỉ có trên đám mây: FILE_ATTRIBUTE_RECALL_ON_OPEN (0x40000) / RECALL_ON_DATA_ACCESS (0x400000), hoặc Offline.
    private const FileAttributes CloudOnly = FileAttributes.Offline | (FileAttributes)0x40000 | (FileAttributes)0x400000;

    /// <summary>Các ổ đĩa và thư mục người dùng để bắt đầu phân tích.</summary>
    public static List<DiskLocation> Overview()
    {
        var list = new List<DiskLocation>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                list.Add(new DiskLocation
                {
                    Name = drive.Name.TrimEnd('\\'),
                    Path = drive.RootDirectory.FullName,
                    IsDrive = true,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace,
                    Label = drive.VolumeLabel,
                    Format = drive.DriveFormat,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Đọc ổ {drive.Name} lỗi", ex);
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var (name, path) in new[]
                 {
                     ("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
                     ("Downloads", Path.Combine(profile, "Downloads")),
                     ("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                     ("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
                     ("Videos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
                     ("Music", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
                     ("AppData", Path.Combine(profile, "AppData")),
                     (Path.GetFileName(profile), profile),
                 })
        {
            if (path.Length > 0 && Directory.Exists(path)) list.Add(new DiskLocation { Name = name, Path = path });
        }

        return list;
    }

    /// <summary>Quét một thư mục: kích thước từng mục con và các tệp lớn nhất bên trong.</summary>
    public static Task<DiskReport> ScanAsync(string path, DiskScanProgress? progress = null, CancellationToken ct = default) =>
        Task.Run(() => Scan(path, progress, ct), ct);

    private static DiskReport Scan(string path, DiskScanProgress? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var report = new DiskReport { Path = path };
        var known = KnownFolders.Current;
        var largest = new TopFiles(LargeFileCount);
        var dirs = new List<DiskEntry>();

        try
        {
            foreach (var info in new DirectoryInfo(path).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }))
            {
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (info is DirectoryInfo)
                {
                    var entry = new DiskEntry { Name = info.Name, Path = info.FullName, IsDirectory = true, LastWriteUtc = info.LastWriteTimeUtc };
                    entry.InsightKey = Insight(entry.Path, true, known);
                    dirs.Add(entry);
                    report.Entries.Add(entry);
                }
                else if (info is FileInfo file)
                {
                    var bytes = (file.Attributes & CloudOnly) != 0 ? 0 : file.Length;
                    report.Entries.Add(new DiskEntry
                    {
                        Name = file.Name,
                        Path = file.FullName,
                        Bytes = bytes,
                        Files = 1,
                        LastWriteUtc = file.LastWriteTimeUtc,
                        InsightKey = Insight(file.FullName, false, known),
                    });
                    largest.Offer(new LargeFile(file.Name, file.FullName, bytes, file.LastWriteTimeUtc));
                    progress?.Add(1, bytes);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($"Không đọc được {path}", ex);
            report.Status = ScanStatus.Unavailable;
            return report;
        }

        var locals = new TopFiles[dirs.Count];
        try
        {
            Parallel.For(0, dirs.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) }, i =>
            {
                locals[i] = new TopFiles(LargeFileCount);
                MeasureDirectory(dirs[i], locals[i], progress, ct);
            });
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            throw new OperationCanceledException(ct);
        }

        foreach (var local in locals) largest.Merge(local);
        report.Entries.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        report.LargeFiles.AddRange(largest.Sorted().Where(f => f.Bytes >= LargeFileMinBytes));
        report.TotalBytes = report.Entries.Sum(e => e.Bytes);
        report.TotalFiles = report.Entries.Sum(e => e.Files);
        report.Status = report.Entries.Any(e => e.Status != ScanStatus.Complete) ? ScanStatus.Partial : ScanStatus.Complete;
        report.Duration = watch.Elapsed;
        return report;
    }

    /// <summary>Dung lượng và số tệp của một thư mục (không đi theo liên kết).</summary>
    public static (long Bytes, long Files, ScanStatus Status) Measure(string path, CancellationToken ct = default)
    {
        var entry = new DiskEntry { Name = Path.GetFileName(path), Path = path, IsDirectory = true };
        MeasureDirectory(entry, new TopFiles(0), null, ct);
        return (entry.Bytes, entry.Files, entry.Status);
    }

    private static void MeasureDirectory(DiskEntry entry, TopFiles largest, DiskScanProgress? progress, CancellationToken ct)
    {
        long pendingFiles = 0, pendingBytes = 0;
        var errors = 0;
        var newest = entry.LastWriteUtc ?? DateTime.MinValue;
        try
        {
            using var enumerator = new SizeEnumerator(entry.Path);
            try
            {
                while (enumerator.MoveNext())
                {
                    var (name, fullPath, length, writeUtc) = enumerator.Current;
                    entry.Bytes += length;
                    entry.Files++;
                    if (writeUtc > newest) newest = writeUtc;
                    if (length >= LargeFileMinBytes) largest.Offer(new LargeFile(name, fullPath, length, writeUtc));
                    pendingFiles++;
                    pendingBytes += length;
                    if (pendingFiles >= 512)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Add(pendingFiles, pendingBytes);
                        pendingFiles = pendingBytes = 0;
                    }
                }
            }
            finally
            {
                errors = enumerator.Errors;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            errors++;
        }

        ct.ThrowIfCancellationRequested();
        progress?.Add(pendingFiles, pendingBytes);
        entry.LastWriteUtc = newest == DateTime.MinValue ? null : newest;
        entry.Status = errors == 0 ? ScanStatus.Complete : entry.Files == 0 ? ScanStatus.Unavailable : ScanStatus.Partial;
    }

    /// <summary>Duyệt đệ quy, bỏ qua liên kết và đếm số thư mục không đọc được (để báo kết quả "chưa đầy đủ").</summary>
    private sealed class SizeEnumerator : FileSystemEnumerator<(string Name, string Path, long Length, DateTime WriteUtc)>
    {
        public SizeEnumerator(string root)
            : base(root, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false })
        {
        }

        public int Errors { get; private set; }

        protected override bool ContinueOnError(int error)
        {
            Errors++;
            return true;
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry) => !entry.IsDirectory;

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0;

        protected override (string, string, long, DateTime) TransformEntry(ref FileSystemEntry entry)
        {
            var cloud = (entry.Attributes & CloudOnly) != 0;
            return (entry.FileName.ToString(), entry.ToFullPath(), cloud ? 0 : entry.Length, entry.LastWriteTimeUtc.UtcDateTime);
        }
    }

    /// <summary>Giữ N tệp lớn nhất (min-heap).</summary>
    private sealed class TopFiles(int capacity)
    {
        private readonly PriorityQueue<LargeFile, long> _heap = new();

        public void Offer(LargeFile file)
        {
            if (file.Bytes <= 0) return;
            if (_heap.Count < capacity)
            {
                _heap.Enqueue(file, file.Bytes);
            }
            else if (_heap.TryPeek(out _, out var smallest) && file.Bytes > smallest)
            {
                _heap.EnqueueDequeue(file, file.Bytes);
            }
        }

        public void Merge(TopFiles? other)
        {
            if (other is null) return;
            foreach (var (file, _) in other._heap.UnorderedItems) Offer(file);
        }

        public IEnumerable<LargeFile> Sorted() => _heap.UnorderedItems.Select(i => i.Element).OrderByDescending(f => f.Bytes);
    }

    // ===== Gợi ý =====

    /// <summary>Các thư mục đã biết dùng để nhận diện gợi ý (tách ra để kiểm thử).</summary>
    public sealed record KnownFolders(string Windows, string Downloads, string UserTemp, string LocalAppData)
    {
        public static KnownFolders Current { get; } = new(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    private static readonly Dictionary<string, string> DirectoryInsights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["node_modules"] = "node_modules",
        ["npm-cache"] = "dev_cache",
        [".gradle"] = "dev_cache",
        [".m2"] = "dev_cache",
        [".nuget"] = "dev_cache",
        ["go-build"] = "dev_cache",
        [".cargo"] = "dev_cache",
        ["pip"] = "dev_cache",
        ["Yarn"] = "dev_cache",
        ["pnpm"] = "dev_cache",
        ["__pycache__"] = "build_output",
        ["$Recycle.Bin"] = "recycle",
        ["Windows.old"] = "windows_old",
        ["$Windows.~BT"] = "windows_old",
        ["$Windows.~WS"] = "windows_old",
        ["SoftwareDistribution"] = "wu",
        ["CrashDumps"] = "dumps",
        ["Minidump"] = "dumps",
        ["LiveKernelReports"] = "dumps",
        ["D3DSCache"] = "shader",
        ["DXCache"] = "shader",
        ["WinSxS"] = "winsxs",
        ["Installer"] = "msi_installer",
        ["System Volume Information"] = "restore_points",
        ["Docker"] = "docker",
        ["wsl"] = "wsl",
    };

    private static readonly HashSet<string> LocalAppDataOnly = new(StringComparer.OrdinalIgnoreCase) { "pip", "Yarn", "pnpm", "Docker", "wsl" };

    private static readonly Dictionary<string, string> FileInsights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hiberfil.sys"] = "hiberfil",
        ["pagefile.sys"] = "pagefile",
        ["swapfile.sys"] = "pagefile",
        ["MEMORY.DMP"] = "dumps",
        ["ext4.vhdx"] = "wsl",
        ["docker_data.vhdx"] = "docker",
    };

    /// <summary>Tất cả khóa gợi ý có thể trả về (để kiểm tra bản dịch "storage.insight.*").</summary>
    public static IEnumerable<string> AllInsightKeys =>
        DirectoryInsights.Values.Concat(FileInsights.Values).Concat(["installer", "downloads", "temp"]).Distinct();

    /// <summary>Khóa gợi ý cho một mục (null nếu không có gì đặc biệt). Thuần logic, dùng đường dẫn kiểu Windows.</summary>
    public static string? Insight(string path, bool isDirectory, KnownFolders known)
    {
        var name = LeftoverRules.LastSegment(path);
        if (!isDirectory)
        {
            if (FileInsights.TryGetValue(name, out var fileKey)) return fileKey;
            var ext = Path.GetExtension(name);
            if (InstallerFinder.KindOf(ext) is not null && LeftoverRules.IsUnder(path, known.Downloads)) return "installer";
            return null;
        }

        if (known.Downloads.Length > 0 && LeftoverRules.SamePath(path, known.Downloads)) return "downloads";
        if (known.UserTemp.Length > 0 && LeftoverRules.SamePath(path, known.UserTemp)) return "temp";
        if (known.Windows.Length > 0 && LeftoverRules.SamePath(path, Path.Combine(known.Windows, "Temp"))) return "temp";
        if (name.Equals("Temp", StringComparison.OrdinalIgnoreCase) && known.LocalAppData.Length > 0 && LeftoverRules.SamePath(path, Path.Combine(known.LocalAppData, "Temp"))) return "temp";

        // "Installer" chỉ có ý nghĩa là C:\Windows\Installer; "pip"/"Yarn"/"pnpm"/"Docker"/"wsl" chỉ khi nằm trong AppData\Local.
        if (name.Equals("Installer", StringComparison.OrdinalIgnoreCase))
        {
            return known.Windows.Length > 0 && LeftoverRules.SamePath(path, Path.Combine(known.Windows, "Installer")) ? "msi_installer" : null;
        }

        if (LocalAppDataOnly.Contains(name) && !(known.LocalAppData.Length > 0 && LeftoverRules.IsUnder(path, known.LocalAppData)))
        {
            return null;
        }

        return DirectoryInsights.TryGetValue(name, out var key) ? key : null;
    }
}
