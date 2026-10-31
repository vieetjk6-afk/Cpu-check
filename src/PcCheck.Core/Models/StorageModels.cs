namespace PcCheck.Core.Models;

/// <summary>Kết quả đo kích thước: đủ, thiếu một phần (có thư mục không đọc được – con số là cận dưới) hoặc không đọc được.</summary>
public enum ScanStatus
{
    Complete,
    Partial,
    Unavailable,
}

/// <summary>Một mục con (thư mục hoặc tệp) trong thư mục đang phân tích.</summary>
public sealed class DiskEntry
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool IsDirectory { get; init; }
    public long Bytes { get; set; }
    public long Files { get; set; }
    public DateTime? LastWriteUtc { get; set; }
    public ScanStatus Status { get; set; } = ScanStatus.Complete;

    /// <summary>Khóa gợi ý (vd "node_modules", "downloads") nếu là vị trí hay chiếm nhiều dung lượng và có thể dọn – null nếu không.</summary>
    public string? InsightKey { get; set; }
}

public sealed record LargeFile(string Name, string Path, long Bytes, DateTime LastWriteUtc);

/// <summary>Kết quả phân tích một thư mục: các mục con xếp theo dung lượng giảm dần và các tệp lớn nhất bên trong.</summary>
public sealed class DiskReport
{
    public required string Path { get; init; }
    public List<DiskEntry> Entries { get; init; } = [];
    public List<LargeFile> LargeFiles { get; init; } = [];
    public long TotalBytes { get; set; }
    public long TotalFiles { get; set; }
    public ScanStatus Status { get; set; } = ScanStatus.Complete;
    public TimeSpan Duration { get; set; }
}

/// <summary>Ổ đĩa hoặc thư mục người dùng hiện ở màn hình tổng quan của trang Phân tích ổ đĩa.</summary>
public sealed class DiskLocation
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public bool IsDrive { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public string Label { get; init; } = "";
    public string Format { get; init; } = "";
}

/// <summary>Tệp cài đặt / ảnh đĩa đã tải về (Downloads, Desktop...) – thường không còn cần sau khi cài xong.</summary>
public sealed class InstallerFile
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public long Bytes { get; init; }
    public DateTime LastWriteUtc { get; init; }

    /// <summary>"installer" (.exe/.msi/.msix...), "image" (.iso/.img) hoặc "archive" (.zip/.rar/.7z).</summary>
    public required string Kind { get; init; }

    /// <summary>Phần mềm cùng tên đã được cài trên máy – tệp cài gần như chắc chắn không còn cần.</summary>
    public string? InstalledMatch { get; set; }

    public int AgeDays(DateTime nowUtc) => Math.Max(0, (int)(nowUtc - LastWriteUtc).TotalDays);
}

/// <summary>Thư mục build / thư viện tải về của một dự án lập trình (node_modules, bin/obj, target, .venv...).</summary>
public sealed class ProjectArtifact
{
    public required string ProjectPath { get; init; }
    public required string ProjectName { get; init; }
    public required string Path { get; init; }

    /// <summary>Tên loại (node_modules, target, bin/obj...) – dùng làm khóa dịch "purge.kind.*".</summary>
    public required string Kind { get; init; }

    /// <summary>Thư viện tải về (cần mạng để khôi phục) thay vì kết quả build (build lại offline được).</summary>
    public bool IsDependency { get; init; }

    public long Bytes { get; set; }
    public long Files { get; set; }

    /// <summary>Lần cuối dự án được sửa (tệp mã nguồn mới nhất ở cấp ngoài cùng).</summary>
    public DateTime LastActivityUtc { get; set; }

    /// <summary>Dự án mới sửa gần đây: không chọn sẵn để tránh làm gián đoạn công việc đang làm.</summary>
    public bool IsRecent { get; set; }
}

/// <summary>Một dòng trong lịch sử dọn dẹp.</summary>
public sealed class OperationRecord
{
    public DateTime Time { get; set; }

    /// <summary>Nguồn: junk, leftovers, disk, installers, purge.</summary>
    public string Source { get; set; } = "";

    /// <summary>removed (xóa vĩnh viễn), recycled (vào Thùng rác), skipped, failed.</summary>
    public string Action { get; set; } = "";

    /// <summary>Đường dẫn hoặc tên hạng mục.</summary>
    public string Target { get; set; } = "";

    public long Bytes { get; set; }

    public int Count { get; set; } = 1;

    public string? Detail { get; set; }
}
