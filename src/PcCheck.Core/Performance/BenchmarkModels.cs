namespace PcCheck.Core.Performance;

/// <summary>Loại ổ của ổ đĩa được đo – để so với tốc độ mong đợi.</summary>
public enum DriveMedia
{
    Unknown,
    Hdd,
    SataSsd,
    Nvme,
    Usb,
}

/// <summary>Kết quả một lần đo tốc độ. Trường null = phần đó không đo / đo lỗi.</summary>
public sealed class BenchmarkResult
{
    public DateTime Time { get; set; } = DateTime.Now;

    /// <summary>Điểm CPU một nhân (triệu phép tính / giây trên bài kiểm tra số nguyên + số thực).</summary>
    public double? CpuSingle { get; set; }

    /// <summary>Điểm CPU tất cả các nhân.</summary>
    public double? CpuMulti { get; set; }

    public int CpuThreads { get; set; }

    /// <summary>Băng thông RAM (sao chép, GB/s).</summary>
    public double? MemoryBandwidthGBps { get; set; }

    /// <summary>Độ trễ RAM (truy cập ngẫu nhiên, nano giây).</summary>
    public double? MemoryLatencyNs { get; set; }

    public string Drive { get; set; } = "";
    public DriveMedia Media { get; set; }
    public string DriveModel { get; set; } = "";
    public double? DiskSeqReadMBps { get; set; }
    public double? DiskSeqWriteMBps { get; set; }
    public double? DiskRandReadMBps { get; set; }
    public double? DiskRandWriteMBps { get; set; }

    /// <summary>Lý do không đo được ổ đĩa (khóa dịch), null nếu đo được.</summary>
    public string? DiskError { get; set; }

    public double? InternetDownMbps { get; set; }
    public double? InternetUpMbps { get; set; }
    public double? InternetPingMs { get; set; }
    public string? InternetError { get; set; }

    /// <summary>Hệ số tăng tốc đa nhân (đa nhân / đơn nhân).</summary>
    public double? CpuScaling => CpuSingle is > 0 && CpuMulti is { } m ? m / CpuSingle.Value : null;

    /// <summary>IOPS 4K tương ứng (MB/s * 256).</summary>
    public static double Iops(double mbps) => mbps * 1024 * 1024 / 4096;
}

/// <summary>Tiến độ đo: bước hiện tại (khóa dịch "bench.stage.*") và phần trăm tổng.</summary>
public sealed record BenchmarkProgress(string Stage, double Percent);

public sealed class BenchmarkOptions
{
    /// <summary>Ổ cần đo (vd "C"), null = không đo ổ đĩa.</summary>
    public string? Drive { get; init; }

    public bool Internet { get; init; }

    /// <summary>Thời gian mỗi bài CPU / RAM (giây) – nhỏ lại trong kiểm thử.</summary>
    public double Seconds { get; init; } = 2;

    /// <summary>Kích thước tệp thử ổ đĩa tối đa.</summary>
    public long DiskFileBytes { get; init; } = 1024L * 1024 * 1024;
}
