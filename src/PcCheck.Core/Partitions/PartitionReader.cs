using PcCheck.Core.Collectors;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Partitions;

/// <summary>Đọc ổ cứng, phân vùng và volume qua WMI (root\Microsoft\Windows\Storage – cùng nguồn với Disk Management).</summary>
public static class PartitionReader
{
    internal const string DiskQuery = "SELECT Number, FriendlyName, Model, Size, PartitionStyle, IsOffline, IsReadOnly, IsBoot, IsSystem, BusType, NumberOfPartitions FROM MSFT_Disk";
    internal const string PartitionQuery = "SELECT DiskNumber, PartitionNumber, Offset, Size, GptType, MbrType, DriveLetter, IsActive, IsBoot, IsSystem, IsHidden, AccessPaths FROM MSFT_Partition";
    internal const string VolumeQuery = "SELECT Path, DriveLetter, FileSystemLabel, FileSystem, Size, SizeRemaining FROM MSFT_Volume";

    public static List<PhysicalDiskInfo> Read()
    {
        var disks = Wmi.Query(DiskQuery, Wmi.Storage);
        // Lưu ý: "Type" KHÔNG phải thuộc tính WMI của MSFT_Partition (chỉ là cột PowerShell tự tính) – đưa vào là cả câu truy vấn bị từ chối.
        var partitions = Wmi.Query(PartitionQuery, Wmi.Storage);
        var volumes = Wmi.Query(VolumeQuery, Wmi.Storage);
        var pageFiles = Wmi.Query("SELECT Name FROM Win32_PageFileUsage")
            .Select(r => r.Str("Name"))
            .Where(n => n.Length > 1 && n[1] == ':')
            .Select(n => char.ToUpperInvariant(n[0]))
            .ToHashSet();
        return Assemble(disks, partitions, volumes, pageFiles, RootLetter(Environment.ProcessPath), RootLetter(Environment.SystemDirectory));
    }

    private static char? RootLetter(string? path) =>
        path is { Length: > 1 } p && p[1] == ':' && char.IsAsciiLetter(p[0]) ? char.ToUpperInvariant(p[0]) : null;

    /// <summary>Ghép dữ liệu WMI thành mô hình (tách riêng để kiểm thử).</summary>
    internal static List<PhysicalDiskInfo> Assemble(
        List<WmiRow> diskRows,
        List<WmiRow> partitionRows,
        List<WmiRow> volumeRows,
        IReadOnlySet<char> pageFileLetters,
        char? appLetter,
        char? windowsLetter)
    {
        var volumesByPath = new Dictionary<string, WmiRow>(StringComparer.OrdinalIgnoreCase);
        var volumesByLetter = new Dictionary<char, WmiRow>();
        foreach (var v in volumeRows)
        {
            if (v.Str("Path") is { Length: > 0 } path) volumesByPath[path] = v;
            if (LetterOf(v) is { } l) volumesByLetter[l] = v;
        }

        var result = new List<PhysicalDiskInfo>();
        foreach (var d in diskRows.OrderBy(r => r.Int("Number") ?? 0))
        {
            var number = d.Int("Number") ?? 0;
            var parts = new List<PartitionInfo>();
            foreach (var p in partitionRows.Where(r => r.Int("DiskNumber") == number))
            {
                var letter = LetterOf(p);
                var volume = p.StrArray("AccessPaths").Select(a => volumesByPath.GetValueOrDefault(a)).FirstOrDefault(v => v is not null)
                             ?? (letter is { } l ? volumesByLetter.GetValueOrDefault(l) : null);
                var mbrType = p.Int("MbrType") ?? 0;
                var gptType = p.Str("GptType");
                var typeName = p.Str("Type"); // chỉ có trong dữ liệu kiểm thử / nguồn khác WMI
                var kind = PartitionLayout.Classify(gptType, mbrType, typeName);
                var isWindows = (p.Bool("IsBoot") ?? false) || (letter is { } wl && wl == windowsLetter);
                parts.Add(new PartitionInfo
                {
                    DiskNumber = number,
                    PartitionNumber = p.Int("PartitionNumber") ?? 0,
                    Offset = p.Long("Offset") ?? 0,
                    Size = p.Long("Size") ?? 0,
                    Kind = kind,
                    TypeName = typeName,
                    GptType = gptType,
                    MbrType = mbrType,
                    DriveLetter = letter,
                    Label = volume?.Str("FileSystemLabel") ?? "",
                    FileSystem = volume?.Str("FileSystem") ?? "",
                    FreeBytes = volume?.Long("SizeRemaining"),
                    VolumePath = volume?.Str("Path") ?? "",
                    IsWindows = isWindows,
                    IsBootLoader = p.Bool("IsSystem") ?? false,
                    IsActive = p.Bool("IsActive") ?? false,
                    IsHidden = p.Bool("IsHidden") ?? false,
                    IsLogical = typeName.Equals("Logical", StringComparison.OrdinalIgnoreCase),
                    HasPageFile = letter is { } pl && pageFileLetters.Contains(pl),
                    HasThisApp = letter is { } al && al == appLetter,
                });
            }

            parts.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            parts = MarkLogical(parts);
            var bus = d.Int("BusType") ?? 0;
            var name = d.Str("FriendlyName");
            var disk = new PhysicalDiskInfo
            {
                Number = number,
                Name = name.Length > 0 ? name : d.Str("Model"),
                Size = d.Long("Size") ?? 0,
                Style = (d.Int("PartitionStyle") ?? 0) switch
                {
                    1 => PartitionStyle.Mbr,
                    2 => PartitionStyle.Gpt,
                    _ => PartitionStyle.Raw,
                },
                Bus = HardwareCollector.BusTypeName(bus),
                IsOffline = d.Bool("IsOffline") ?? false,
                IsReadOnly = d.Bool("IsReadOnly") ?? false,
                IsSystemDisk = (d.Bool("IsBoot") ?? false) || (d.Bool("IsSystem") ?? false) || parts.Any(p => p.IsWindows || p.IsBootLoader),
                IsRemovable = bus is 7 or 12 or 13,
                Partitions = parts,
                ReportedPartitions = d.Int("NumberOfPartitions"),
            };
            disk.FreeRegions = PartitionLayout.FreeRegions(disk);
            result.Add(disk);
        }

        return result;
    }

    /// <summary>Phân vùng MBR nằm trong vùng của phân vùng mở rộng (Extended) là phân vùng logic.</summary>
    internal static List<PartitionInfo> MarkLogical(List<PartitionInfo> parts)
    {
        var extended = parts.Where(p => p.Kind == PartitionKind.Extended).ToList();
        if (extended.Count == 0) return parts;
        return parts.Select(p => !p.IsLogical && p.Kind != PartitionKind.Extended && extended.Any(e => p.Offset >= e.Offset && p.End <= e.End)
            ? new PartitionInfo
            {
                DiskNumber = p.DiskNumber, PartitionNumber = p.PartitionNumber, Offset = p.Offset, Size = p.Size, Kind = p.Kind,
                TypeName = p.TypeName, GptType = p.GptType, MbrType = p.MbrType, DriveLetter = p.DriveLetter, Label = p.Label,
                FileSystem = p.FileSystem, FreeBytes = p.FreeBytes, IsWindows = p.IsWindows, IsBootLoader = p.IsBootLoader,
                IsActive = p.IsActive, IsHidden = p.IsHidden, IsLogical = true, HasPageFile = p.HasPageFile, HasThisApp = p.HasThisApp,
                VolumePath = p.VolumePath,
            }
            : p).ToList();
    }

    /// <summary>DriveLetter của MSFT_Partition/MSFT_Volume là char16: có thể là char, số (mã ký tự) hoặc '\0'.</summary>
    internal static char? LetterOf(WmiRow row)
    {
        if (!row.TryGetValue("DriveLetter", out var value) || value is null) return null;
        var c = value switch
        {
            char ch => ch,
            string s when s.Length > 0 => s[0],
            ushort u => (char)u,
            int i => (char)i,
            _ => '\0',
        };
        return char.IsAsciiLetter(c) ? char.ToUpperInvariant(c) : null;
    }
}
