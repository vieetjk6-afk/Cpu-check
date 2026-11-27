namespace PcCheck.Core.Models;

public enum PartitionStyle
{
    /// <summary>Ổ chưa khởi tạo (RAW) – chưa có bảng phân vùng.</summary>
    Raw,
    Mbr,
    Gpt,
}

/// <summary>Loại phân vùng theo vai trò (suy ra từ GPT type / MBR type).</summary>
public enum PartitionKind
{
    /// <summary>Phân vùng dữ liệu thông thường (Basic data / NTFS / FAT...).</summary>
    Data,

    /// <summary>EFI System Partition – chứa trình khởi động UEFI.</summary>
    Efi,

    /// <summary>Microsoft Reserved (MSR) – vùng dành riêng của Windows trên ổ GPT, không có hệ thống tệp.</summary>
    Reserved,

    /// <summary>Phân vùng khôi phục (WinRE / của hãng máy).</summary>
    Recovery,

    /// <summary>Phân vùng mở rộng (MBR) chứa các phân vùng logic.</summary>
    Extended,

    /// <summary>Thuộc ổ động (Dynamic disk / LDM) – Windows Storage API không hỗ trợ thao tác.</summary>
    Dynamic,

    /// <summary>Phân vùng của hệ điều hành khác (Linux, macOS...) hoặc không rõ.</summary>
    Other,
}

/// <summary>Một ổ cứng vật lý cùng bảng phân vùng.</summary>
public sealed class PhysicalDiskInfo
{
    public int Number { get; init; }
    public string Name { get; init; } = "";
    public long Size { get; init; }
    public PartitionStyle Style { get; init; }

    /// <summary>Bus (NVMe, SATA, USB...) – để hiển thị.</summary>
    public string Bus { get; init; } = "";

    public bool IsOffline { get; init; }
    public bool IsReadOnly { get; init; }

    /// <summary>Ổ chứa Windows đang chạy hoặc phân vùng khởi động (EFI / System Reserved).</summary>
    public bool IsSystemDisk { get; init; }

    public bool IsRemovable { get; init; }
    public List<PartitionInfo> Partitions { get; init; } = [];

    /// <summary>Vùng trống chưa phân vùng (do PartitionLayout tính).</summary>
    public List<FreeRegion> FreeRegions { get; set; } = [];

    /// <summary>Số phân vùng Windows báo cho ổ (MSFT_Disk.NumberOfPartitions) – để phát hiện đọc thiếu.</summary>
    public int? ReportedPartitions { get; init; }

    /// <summary>
    /// Không đọc đủ bảng phân vùng (Windows báo có phân vùng nhưng không đọc được). Khi đó ứng dụng không cho thao tác gì trên ổ
    /// và không hiện vùng trống – tránh coi nhầm ổ đang có dữ liệu là ổ trống.
    /// </summary>
    public bool PartitionsUnreadable => ReportedPartitions is { } n && n > Partitions.Count;

    public long AllocatedBytes => Partitions.Where(p => p.Kind != PartitionKind.Extended).Sum(p => p.Size);
}

/// <summary>Một phân vùng và volume (nếu có) bên trong.</summary>
public sealed class PartitionInfo
{
    public int DiskNumber { get; init; }
    public int PartitionNumber { get; init; }
    public long Offset { get; init; }
    public long Size { get; init; }
    public PartitionKind Kind { get; init; }

    /// <summary>Tên loại do Windows báo (Basic, System, Reserved, Recovery, IFS, Extended, Logical...).</summary>
    public string TypeName { get; init; } = "";

    public string GptType { get; init; } = "";
    public int MbrType { get; init; }

    /// <summary>Ký tự ổ (vd 'C') hoặc null nếu không gán.</summary>
    public char? DriveLetter { get; init; }

    public string Label { get; init; } = "";
    public string FileSystem { get; init; } = "";

    /// <summary>Dung lượng còn trống của volume (null nếu không đọc được / không có volume).</summary>
    public long? FreeBytes { get; init; }

    /// <summary>Chứa Windows đang chạy (boot volume theo cách gọi của Windows).</summary>
    public bool IsWindows { get; init; }

    /// <summary>Phân vùng khởi động (EFI / System Reserved – "system partition" theo cách gọi của Windows).</summary>
    public bool IsBootLoader { get; init; }

    public bool IsActive { get; init; }
    public bool IsHidden { get; init; }
    public bool IsLogical { get; init; }

    /// <summary>Chứa tệp hoán trang (pagefile.sys).</summary>
    public bool HasPageFile { get; init; }

    /// <summary>Chứa ứng dụng PC Check đang chạy.</summary>
    public bool HasThisApp { get; init; }

    /// <summary>Đường dẫn volume (\\?\Volume{...}\) – rỗng nếu không có volume.</summary>
    public string VolumePath { get; init; } = "";

    public long End => Offset + Size;
    public long? UsedBytes => FreeBytes is { } free && Size > 0 ? Math.Max(0, Size - free) : null;
    public bool HasVolume => FileSystem.Length > 0 || VolumePath.Length > 0;
}

/// <summary>Vùng chưa cấp phát trên ổ.</summary>
public sealed record FreeRegion(int DiskNumber, long Offset, long Size)
{
    public long End => Offset + Size;
}

/// <summary>Giới hạn kích thước khi đổi cỡ phân vùng (Get-PartitionSupportedSize).</summary>
public sealed record PartitionSizeRange(long Min, long Max);

/// <summary>Kết quả một thao tác phân vùng.</summary>
public sealed record PartitionOpResult(bool Success, string Detail);
