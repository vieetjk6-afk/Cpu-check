using PcCheck.Core.Models;

namespace PcCheck.Core.Partitions;

/// <summary>Các thao tác trên một phân vùng.</summary>
public enum PartitionOp
{
    Shrink,
    Extend,
    Delete,
    Format,
    ChangeLetter,
    RemoveLetter,
    Label,
    Merge,
    ConvertToNtfs,
    CheckFileSystem,
}

/// <summary>
/// Logic thuần (không gọi hệ thống, dễ kiểm thử): phân loại phân vùng, tính vùng trống, quy tắc an toàn cho từng thao tác,
/// căn chỉnh kích thước và kiểm tra dữ liệu nhập.
/// </summary>
/// <param name="Keep">Phân vùng giữ lại và được mở rộng.</param>
/// <param name="Removed">Phân vùng bị xóa (dữ liệu chép sang Keep nếu chọn).</param>
/// <param name="Recoveries">Phân vùng khôi phục nằm giữa, xóa kèm (WinRE chuyển vào ổ Windows).</param>
public sealed record MergePlan(PartitionInfo Keep, PartitionInfo Removed, IReadOnlyList<PartitionInfo> Recoveries);

/// <param name="Free">Vùng trống sẽ mở rộng vào.</param>
/// <param name="Recoveries">Phân vùng khôi phục chắn giữa, phải xóa trước khi mở rộng.</param>
public sealed record ExtendPlan(FreeRegion Free, IReadOnlyList<PartitionInfo> Recoveries)
{
    /// <summary>Kích thước tối đa của phân vùng sau khi mở rộng.</summary>
    public long MaxSize(PartitionInfo p) => PartitionLayout.AlignDown(Free.End - p.Offset);
}

public static class PartitionLayout
{
    public const long MiB = 1024L * 1024;
    public const long GiB = 1024L * MiB;

    /// <summary>Vùng trống nhỏ hơn mức này là khoảng căn chỉnh, không hiện ra.</summary>
    public const long MinFreeRegion = 16 * MiB;

    /// <summary>Phân vùng mới nhỏ nhất cho phép tạo.</summary>
    public const long MinNewPartition = 16 * MiB;

    /// <summary>Format-Volume của Windows không tạo FAT32 lớn hơn 32 GB.</summary>
    public const long Fat32MaxSize = 32 * GiB;

    public static readonly IReadOnlyList<string> FileSystems = ["NTFS", "exFAT", "FAT32"];

    // GPT partition type GUID
    private const string GptEfi = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b";
    private const string GptMsr = "e3c9e316-0b5c-4db8-817d-f92df00215ae";
    private const string GptRecovery = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";
    private const string GptBasicData = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
    private const string GptLdmMeta = "5808c8aa-7e8f-42e0-85d2-e1e90434cfb3";
    private const string GptLdmData = "af9b60a0-1431-4f62-bc68-3311714a69ad";
    private const string GptStorageSpaces = "e75caf8f-f680-4cee-afa3-b001e56efc2d";

    /// <summary>Loại phân vùng MBR của dữ liệu Windows (FAT12/16/32, NTFS/exFAT, kể cả bản ẩn).</summary>
    private static readonly HashSet<int> MbrData = [0x01, 0x04, 0x06, 0x07, 0x0B, 0x0C, 0x0E, 0x11, 0x14, 0x16, 0x17, 0x1B, 0x1C, 0x1E];

    public static PartitionKind Classify(string gptType, int mbrType, string typeName)
    {
        var gpt = gptType.Trim().Trim('{', '}').ToLowerInvariant();
        if (gpt.Length > 0)
        {
            return gpt switch
            {
                GptEfi => PartitionKind.Efi,
                GptMsr => PartitionKind.Reserved,
                GptRecovery => PartitionKind.Recovery,
                GptBasicData => PartitionKind.Data,
                GptLdmMeta or GptLdmData or GptStorageSpaces => PartitionKind.Dynamic,
                _ => PartitionKind.Other,
            };
        }

        if (mbrType != 0)
        {
            return mbrType switch
            {
                0xEF => PartitionKind.Efi,
                0x27 => PartitionKind.Recovery,
                0x05 or 0x0F or 0x85 => PartitionKind.Extended,
                0x42 => PartitionKind.Dynamic,
                _ when MbrData.Contains(mbrType) => PartitionKind.Data,
                _ => PartitionKind.Other,
            };
        }

        return typeName.Trim().ToLowerInvariant() switch
        {
            "system" => PartitionKind.Efi,
            "reserved" => PartitionKind.Reserved,
            "recovery" => PartitionKind.Recovery,
            "extended" => PartitionKind.Extended,
            "basic" or "ifs" or "logical" or "fat32" or "fat16" or "fat12" or "huge" => PartitionKind.Data,
            _ => PartitionKind.Other,
        };
    }

    /// <summary>Tính các vùng chưa cấp phát (bỏ qua khoảng căn chỉnh nhỏ, vùng đầu ổ và bảng GPT dự phòng ở cuối).</summary>
    public static List<FreeRegion> FreeRegions(PhysicalDiskInfo disk)
    {
        var regions = new List<FreeRegion>();
        if (disk.Style == PartitionStyle.Raw || disk.Size <= 0) return regions;

        var start = MiB; // MBR / GPT header – Windows luôn căn phân vùng đầu ở 1 MB
        var end = disk.Style == PartitionStyle.Gpt ? disk.Size - MiB : disk.Size; // bảng GPT dự phòng ở cuối ổ
        var cursor = start;
        foreach (var p in disk.Partitions.Where(p => p.Size > 0 && !p.IsLogical).OrderBy(p => p.Offset))
        {
            if (p.Offset > cursor) Add(cursor, p.Offset);
            cursor = Math.Max(cursor, p.End);
        }

        if (end > cursor) Add(cursor, end);
        return regions;

        void Add(long from, long to)
        {
            var alignedFrom = AlignUp(from);
            var alignedTo = AlignDown(to);
            if (alignedTo - alignedFrom >= MinFreeRegion) regions.Add(new FreeRegion(disk.Number, alignedFrom, alignedTo - alignedFrom));
        }
    }

    public static long AlignUp(long bytes) => (bytes + MiB - 1) / MiB * MiB;

    public static long AlignDown(long bytes) => bytes / MiB * MiB;

    /// <summary>Vùng trống liền ngay sau phân vùng (có thể mở rộng vào).</summary>
    public static FreeRegion? FreeAfter(PhysicalDiskInfo disk, PartitionInfo partition) =>
        disk.FreeRegions.FirstOrDefault(r => r.Offset >= partition.End && r.Offset - partition.End < MinFreeRegion);

    public static bool CanExtendFileSystem(string fs) =>
        fs.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || fs.Equals("ReFS", StringComparison.OrdinalIgnoreCase);

    /// <summary>Phân vùng không được xóa / format: Windows, trình khởi động, tệp hoán trang, nơi chứa ứng dụng.</summary>
    public static string? CriticalReason(PhysicalDiskInfo disk, PartitionInfo p)
    {
        if (p.IsWindows) return "part.block.windows";
        if (p.IsBootLoader || (disk.IsSystemDisk && p.Kind == PartitionKind.Efi)) return "part.block.boot";
        if (disk.IsSystemDisk && p.Kind == PartitionKind.Reserved) return "part.block.msr";
        if (p.HasPageFile) return "part.block.pagefile";
        if (p.HasThisApp) return "part.block.app";
        return null;
    }

    /// <summary>
    /// Lý do không cho phép thao tác (khóa dịch) – null nếu được phép. Thao tác vẫn hỏi xác nhận ở giao diện;
    /// đây là lớp chặn những việc làm hỏng Windows hoặc chắc chắn thất bại.
    /// </summary>
    public static string? Blocker(PhysicalDiskInfo disk, PartitionInfo p, PartitionOp op)
    {
        if (disk.IsOffline) return "part.block.offline";
        if (disk.IsReadOnly) return "part.block.readonly";
        if (p.Kind == PartitionKind.Dynamic) return "part.block.dynamic";

        switch (op)
        {
            case PartitionOp.Delete:
                if (CriticalReason(disk, p) is { } critical) return critical;
                return null;

            case PartitionOp.Format:
                if (CriticalReason(disk, p) is { } c) return c;
                return p.Kind is PartitionKind.Data or PartitionKind.Other ? null : "part.block.kind";

            case PartitionOp.Shrink:
                if (p.Kind != PartitionKind.Data) return "part.block.kind";
                if (!p.FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) return "part.block.shrink_fs";
                return null;

            case PartitionOp.Extend:
                if (p.Kind != PartitionKind.Data) return "part.block.kind";
                if (!CanExtendFileSystem(p.FileSystem)) return "part.block.extend_fs";
                return PlanExtend(disk, p) is null ? "part.block.no_space_after" : null;

            case PartitionOp.ChangeLetter:
            case PartitionOp.RemoveLetter:
                if (p.IsWindows) return "part.block.windows";
                if (p.HasPageFile) return "part.block.pagefile";
                if (p.HasThisApp) return "part.block.app";
                if (!p.HasVolume || p.Kind != PartitionKind.Data) return "part.block.kind";
                return op == PartitionOp.RemoveLetter && p.DriveLetter is null ? "part.block.no_letter" : null;

            case PartitionOp.Label:
                return p.HasVolume && p.FileSystem.Length > 0 ? null : "part.block.kind";

            case PartitionOp.ConvertToNtfs:
                if (p.IsWindows) return "part.block.windows";
                if (p.DriveLetter is null) return "part.block.no_letter";
                return p.FileSystem.StartsWith("FAT", StringComparison.OrdinalIgnoreCase) ? null : "part.block.not_fat";

            case PartitionOp.CheckFileSystem:
                return p.DriveLetter is null || p.FileSystem.Length == 0 ? "part.block.no_letter" : null;

            case PartitionOp.Merge:
                return PlanMerge(disk, p, out _);
        }

        return null;
    }

    /// <summary>
    /// Lập kế hoạch gộp cho phân vùng đang chọn: ưu tiên giữ phân vùng này và gộp phân vùng phía sau vào; nếu không được thì
    /// gộp phân vùng này vào phân vùng dữ liệu phía trước (vd chọn D: → gộp D: vào C:). Phân vùng khôi phục nằm giữa
    /// (thường gặp: C: | Recovery | D:) được xóa kèm, môi trường khôi phục chuyển vào ổ Windows bằng reagentc.
    /// </summary>
    public static string? PlanMerge(PhysicalDiskInfo disk, PartitionInfo selected, out MergePlan? plan)
    {
        var forward = TryMergeInto(disk, selected, out plan);
        if (forward is null) return null;

        var previous = disk.Partitions
            .Where(p => p.IsLogical == selected.IsLogical && p.End <= selected.Offset && p.Kind is PartitionKind.Data)
            .OrderByDescending(p => p.Offset)
            .FirstOrDefault();
        string? backwardError = null;
        if (previous is not null)
        {
            backwardError = TryMergeInto(disk, previous, out var backward);
            if (backwardError is null && ReferenceEquals(backward!.Removed, selected))
            {
                plan = backward;
                return null;
            }
        }

        plan = null;
        // Phía sau không có gì để gộp: lý do từ phía trước (vd phân vùng đang chọn chứa pagefile) hữu ích hơn.
        return forward == "part.block.no_next" && backwardError is not null && backwardError != "part.block.no_next" && backwardError.EndsWith("_next", StringComparison.Ordinal)
            ? backwardError
            : forward;
    }

    /// <summary>Giữ <paramref name="keep"/>, xóa phân vùng dữ liệu kế tiếp (và phân vùng khôi phục nằm giữa), rồi mở rộng keep.</summary>
    internal static string? TryMergeInto(PhysicalDiskInfo disk, PartitionInfo keep, out MergePlan? plan)
    {
        plan = null;
        if (keep.Kind != PartitionKind.Data || !keep.HasVolume) return "part.block.kind";
        if (!CanExtendFileSystem(keep.FileSystem)) return "part.block.extend_fs";

        var recoveries = new List<PartitionInfo>();
        foreach (var p in After(disk, keep))
        {
            if (p.Kind == PartitionKind.Recovery)
            {
                recoveries.Add(p);
                continue;
            }

            if (p.Kind == PartitionKind.Extended) return "part.block.no_next";
            if (p.Kind is not (PartitionKind.Data or PartitionKind.Other)) return "part.block.next_kind";
            if (CriticalReason(disk, p) is { } critical) return critical + "_next";
            plan = new MergePlan(keep, p, recoveries);
            return null;
        }

        return "part.block.no_next";
    }

    /// <summary>
    /// Vùng có thể mở rộng vào: vùng trống liền sau, hoặc vùng trống nằm sau (các) phân vùng khôi phục chắn ngay phía sau
    /// (trường hợp "Extend Volume" bị mờ trong Disk Management). Null nếu không mở rộng được.
    /// </summary>
    public static ExtendPlan? PlanExtend(PhysicalDiskInfo disk, PartitionInfo p)
    {
        if (FreeAfter(disk, p) is { } direct) return new ExtendPlan(direct, []);

        var recoveries = new List<PartitionInfo>();
        var cursor = p.End;
        foreach (var next in After(disk, p))
        {
            if (next.Offset - cursor >= MinFreeRegion) break; // có vùng trống ở giữa – đã xét ở FreeAfter
            if (next.Kind != PartitionKind.Recovery) break;
            recoveries.Add(next);
            cursor = next.End;
        }

        if (recoveries.Count == 0) return null;
        var free = disk.FreeRegions.FirstOrDefault(r => r.Offset >= cursor && r.Offset - cursor < MinFreeRegion);
        return free is null ? null : new ExtendPlan(free, recoveries);
    }

    /// <summary>Các phân vùng phía sau (cùng cấp chính/logic), theo thứ tự.</summary>
    private static IEnumerable<PartitionInfo> After(PhysicalDiskInfo disk, PartitionInfo partition) =>
        disk.Partitions.Where(p => p.IsLogical == partition.IsLogical && p.Offset >= partition.End).OrderBy(p => p.Offset);

    /// <summary>Đủ chỗ trống trong phân vùng giữ lại để chép toàn bộ dữ liệu của phân vùng bị gộp (chừa 1 GB).</summary>
    public static bool CanCopyForMerge(PartitionInfo keep, PartitionInfo removed) =>
        keep.FreeBytes is { } free && removed.UsedBytes is { } used && removed.DriveLetter is not null && keep.DriveLetter is not null
        && free - GiB >= used;

    /// <summary>Kiểm tra hệ thống tệp được chọn có tạo được với kích thước này không.</summary>
    public static string? FileSystemBlocker(string fs, long size)
    {
        if (!FileSystems.Contains(fs, StringComparer.OrdinalIgnoreCase)) return "part.error.fs";
        if (fs.Equals("FAT32", StringComparison.OrdinalIgnoreCase) && size > Fat32MaxSize) return "part.error.fat32_size";
        return null;
    }

    /// <summary>Độ dài nhãn tối đa theo hệ thống tệp.</summary>
    public static int MaxLabelLength(string fs) => fs.ToUpperInvariant() switch
    {
        "FAT32" or "FAT" => 11,
        "EXFAT" => 15,
        _ => 32,
    };

    /// <summary>Làm sạch nhãn ổ: bỏ ký tự điều khiển và ký tự Windows không cho dùng, cắt theo độ dài tối đa.</summary>
    public static string SanitizeLabel(string? label, string fs)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var invalid = fs.StartsWith("FAT", StringComparison.OrdinalIgnoreCase) || fs.Equals("exFAT", StringComparison.OrdinalIgnoreCase)
            ? "*?/\\|,;:+=<>[]\"."
            : "\"*/:<>?\\|";
        var clean = new string(label.Where(c => !char.IsControl(c) && !invalid.Contains(c)).ToArray()).Trim();
        var max = MaxLabelLength(fs);
        return clean.Length > max ? clean[..max].TrimEnd() : clean;
    }

    /// <summary>Đọc số dung lượng người dùng nhập (GB, chấp nhận dấu "," hoặc ".") → byte căn theo MB. Null nếu không hợp lệ.</summary>
    public static long? ParseGigabytes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = text.Trim().Replace(" ", "").Replace(',', '.');
        if (!double.TryParse(normalized, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gb)) return null;
        if (double.IsNaN(gb) || gb <= 0 || gb > 1_000_000) return null;
        return AlignDown((long)Math.Round(gb * GiB));
    }

    /// <summary>Ký tự ổ còn trống (D..Z), bỏ qua các ký tự đang dùng.</summary>
    public static List<char> FreeLetters(IEnumerable<char> used)
    {
        var taken = used.Select(char.ToUpperInvariant).ToHashSet();
        return Enumerable.Range('D', 'Z' - 'D' + 1).Select(i => (char)i).Where(c => !taken.Contains(c)).ToList();
    }
}
