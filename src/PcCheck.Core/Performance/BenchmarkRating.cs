using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Performance;

/// <summary>Nhận xét về một kết quả đo (Ok = tốt, Info = bình thường, Warning = chậm hơn mong đợi).</summary>
public sealed record BenchmarkNote(string Id, Severity Severity, LocText Text);

/// <summary>Đánh giá kết quả đo so với tốc độ mong đợi của loại phần cứng (thuần logic, dễ kiểm thử).</summary>
public static class BenchmarkRating
{
    /// <summary>Tốc độ đọc tuần tự tối thiểu bình thường (MB/s) theo loại ổ.</summary>
    public static double? ExpectedSeqRead(DriveMedia media) => media switch
    {
        DriveMedia.Hdd => 90,
        DriveMedia.SataSsd => 350,
        DriveMedia.Nvme => 1200,
        _ => null,
    };

    /// <summary>Tốc độ đọc 4K ngẫu nhiên tối thiểu bình thường (MB/s).</summary>
    public static double? ExpectedRandRead(DriveMedia media) => media switch
    {
        DriveMedia.Hdd => 0.3,
        DriveMedia.SataSsd => 15,
        DriveMedia.Nvme => 25,
        _ => null,
    };

    public static Severity RateSeqRead(DriveMedia media, double mbps) =>
        ExpectedSeqRead(media) is not { } min ? Severity.Info : mbps >= min ? Severity.Ok : mbps >= min * 0.6 ? Severity.Info : Severity.Warning;

    public static Severity RateRandRead(DriveMedia media, double mbps) =>
        ExpectedRandRead(media) is not { } min ? Severity.Info : mbps >= min ? Severity.Ok : mbps >= min * 0.5 ? Severity.Info : Severity.Warning;

    /// <summary>Độ trễ RAM: dưới 90 ns tốt, tới 130 ns bình thường, cao hơn là chậm.</summary>
    public static Severity RateLatency(double ns) => ns <= 90 ? Severity.Ok : ns <= 130 ? Severity.Info : Severity.Warning;

    /// <summary>Băng thông sao chép RAM: dưới 6 GB/s thường là RAM chạy kênh đơn / tốc độ thấp.</summary>
    public static Severity RateBandwidth(double gbps) => gbps >= 12 ? Severity.Ok : gbps >= 6 ? Severity.Info : Severity.Warning;

    /// <summary>
    /// Hệ số tăng tốc đa nhân so với số nhân thật: thấp bất thường gợi ý CPU bị giới hạn công suất / nhiệt hoặc phần mềm khác
    /// đang chiếm CPU trong lúc đo.
    /// </summary>
    public static Severity RateScaling(double scaling, int physicalCores) =>
        physicalCores <= 1 ? Severity.Info : scaling >= physicalCores * 0.6 ? Severity.Ok : scaling >= physicalCores * 0.4 ? Severity.Info : Severity.Warning;

    public static Severity RateDownload(double mbps) => mbps >= 50 ? Severity.Ok : mbps >= 10 ? Severity.Info : Severity.Warning;

    public static Severity RatePing(double ms) => ms <= 40 ? Severity.Ok : ms <= 100 ? Severity.Info : Severity.Warning;

    /// <summary>Nhận xét và hướng xử lý cho các phần chậm hơn mong đợi.</summary>
    public static List<BenchmarkNote> Notes(BenchmarkResult r, int physicalCores, bool hasDualChannelInfo = false, bool singleChannel = false)
    {
        var notes = new List<BenchmarkNote>();

        if (r.CpuScaling is { } scaling && RateScaling(scaling, physicalCores) == Severity.Warning)
        {
            notes.Add(new("cpu_scaling", Severity.Warning, L("bench.note.cpu_scaling", scaling.ToString("0.0", Loc.Instance.Culture), physicalCores)));
        }

        if (r.MemoryBandwidthGBps is { } bw && RateBandwidth(bw) == Severity.Warning)
        {
            notes.Add(new("ram_bandwidth", Severity.Warning, hasDualChannelInfo && singleChannel ? L("bench.note.ram_single_channel") : L("bench.note.ram_bandwidth")));
        }

        if (r.MemoryLatencyNs is { } ns && RateLatency(ns) == Severity.Warning)
        {
            notes.Add(new("ram_latency", Severity.Warning, L("bench.note.ram_latency")));
        }

        if (r.DiskSeqReadMBps is { } seq && RateSeqRead(r.Media, seq) == Severity.Warning)
        {
            notes.Add(new("disk_slow", Severity.Warning, L($"bench.note.disk_slow_{r.Media.ToString().ToLowerInvariant()}", Math.Round(seq), ExpectedSeqRead(r.Media))));
        }
        else if (r.DiskRandReadMBps is { } rand && RateRandRead(r.Media, rand) == Severity.Warning)
        {
            notes.Add(new("disk_random", Severity.Warning, L("bench.note.disk_random")));
        }

        if (r.Media == DriveMedia.Hdd && r.DiskSeqReadMBps is not null)
        {
            notes.Add(new("disk_hdd", Severity.Info, L("bench.note.disk_hdd")));
        }

        if (r.InternetDownMbps is { } down && RateDownload(down) == Severity.Warning)
        {
            notes.Add(new("net_slow", Severity.Warning, L("bench.note.net_slow")));
        }

        if (r.InternetPingMs is { } ping && RatePing(ping) == Severity.Warning)
        {
            notes.Add(new("net_ping", Severity.Warning, L("bench.note.net_ping")));
        }

        if (notes.Count == 0) notes.Add(new("all_good", Severity.Ok, L("bench.note.all_good")));
        return notes;
    }

    /// <summary>% thay đổi so với lần đo trước (dương = nhanh hơn). Với độ trễ / ping, thấp hơn là tốt hơn.</summary>
    public static double? Change(double? now, double? before, bool lowerIsBetter = false)
    {
        if (now is not { } n || before is not { } b || b <= 0) return null;
        var change = (n - b) / b * 100;
        return lowerIsBetter ? -change : change;
    }
}
