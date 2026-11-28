using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PcCheck.Core.Util;

namespace PcCheck.Core.Performance;

/// <summary>
/// Đo tốc độ CPU, RAM, ổ đĩa (và Internet nếu chọn). Bài đo chạy trên luồng nền, mỗi phần có giới hạn thời gian,
/// hủy được bất cứ lúc nào. Tệp thử ổ đĩa tự xóa khi đóng (DeleteOnClose).
/// </summary>
public static class Benchmark
{
    public static async Task<BenchmarkResult> RunAsync(BenchmarkOptions options, IProgress<BenchmarkProgress>? progress = null, CancellationToken ct = default)
    {
        var result = new BenchmarkResult { CpuThreads = Environment.ProcessorCount };
        var steps = 4 + (options.Drive is null ? 0 : 4) + (options.Internet ? 3 : 0);
        var done = 0;
        void Report(string stage) => progress?.Report(new BenchmarkProgress(stage, 100.0 * done++ / steps));

        Report("cpu_single");
        result.CpuSingle = await Task.Run(() => CpuSingle(options.Seconds, ct), ct).ConfigureAwait(false);
        Report("cpu_multi");
        result.CpuMulti = await Task.Run(() => CpuMulti(options.Seconds, ct), ct).ConfigureAwait(false);
        Report("memory_bandwidth");
        result.MemoryBandwidthGBps = await Task.Run(() => MemoryBandwidth(options.Seconds, ct), ct).ConfigureAwait(false);
        Report("memory_latency");
        result.MemoryLatencyNs = await Task.Run(() => MemoryLatency(ct), ct).ConfigureAwait(false);

        if (options.Drive is { } drive)
        {
            result.Drive = DriveLetter(drive) + ":";
            (result.Media, result.DriveModel) = await Task.Run(() => DriveInfoFor(DriveLetter(drive)), ct).ConfigureAwait(false);
            try
            {
                await Task.Run(() => DiskTest(DriveLetter(drive), options.DiskFileBytes, result, Report, ct), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Đo ổ {drive} lỗi", ex);
                result.DiskError = ex is UnauthorizedAccessException ? "bench.error.disk_access" : "bench.error.disk_io";
            }
        }

        if (options.Internet)
        {
            await InternetSpeed.RunAsync(result, Report, ct).ConfigureAwait(false);
        }

        progress?.Report(new BenchmarkProgress("done", 100));
        result.Time = DateTime.Now;
        return result;
    }

    private static char DriveLetter(string drive) => char.ToUpperInvariant(drive.Trim()[0]);

    // ======================= CPU =======================

    /// <summary>
    /// Bài tính hỗn hợp: 4 chuỗi số nguyên độc lập (xorshift, nhân, cộng) + một phép số thực – tận dụng nhiều đơn vị
    /// tính trong một nhân như phần mềm thật. Trả về số vòng lặp đã chạy.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    internal static ulong CpuKernel(int iterations, ulong seed)
    {
        ulong a = seed | 1, b = seed * 31 + 7, c = seed ^ 0x9E3779B97F4A7C15UL, d = 12345;
        var f = 1.0 + (seed & 7);
        for (var i = 0; i < iterations; i++)
        {
            a ^= a << 13;
            a ^= a >> 7;
            a ^= a << 17;
            b = b * 6364136223846793005UL + 1442695040888963407UL;
            c += (b >> 32) * 2654435761UL;
            d ^= c + (d << 3);
            f = f * 1.0000001 + 0.5;
            if (f > 1e6) f -= 1e6;
        }

        return a ^ b ^ c ^ d ^ (ulong)f;
    }

    private const int CpuChunk = 200_000;

    /// <summary>Chạy kernel liên tục trong khoảng thời gian, trả về triệu vòng / giây.</summary>
    private static double CpuLoop(double seconds, CancellationToken ct, ref ulong sink)
    {
        var sw = Stopwatch.StartNew();
        long iterations = 0;
        var limit = TimeSpan.FromSeconds(seconds);
        while (sw.Elapsed < limit)
        {
            ct.ThrowIfCancellationRequested();
            sink ^= CpuKernel(CpuChunk, (ulong)iterations);
            iterations += CpuChunk;
        }

        return iterations / sw.Elapsed.TotalSeconds / 1e6;
    }

    internal static double CpuSingle(double seconds, CancellationToken ct)
    {
        ulong sink = 0;
        CpuLoop(Math.Min(0.4, seconds / 4), ct, ref sink); // khởi động JIT + xung nhịp
        var score = CpuLoop(seconds, ct, ref sink);
        GC.KeepAlive(sink);
        return score;
    }

    internal static double CpuMulti(double seconds, CancellationToken ct)
    {
        var threads = Math.Max(1, Environment.ProcessorCount);
        var scores = new double[threads];
        var workers = new Thread[threads];
        for (var t = 0; t < threads; t++)
        {
            var index = t;
            workers[t] = new Thread(() =>
            {
                ulong sink = (ulong)index;
                try
                {
                    CpuLoop(Math.Min(0.3, seconds / 4), ct, ref sink);
                    scores[index] = CpuLoop(seconds, ct, ref sink);
                }
                catch (OperationCanceledException)
                {
                }

                GC.KeepAlive(sink);
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            workers[t].Start();
        }

        foreach (var w in workers) w.Join();
        ct.ThrowIfCancellationRequested();
        return scores.Sum();
    }

    // ======================= RAM =======================

    /// <summary>Sao chép 2 vùng 256 MB song song trên nhiều luồng – GB dữ liệu chép mỗi giây.</summary>
    internal static double? MemoryBandwidth(double seconds, CancellationToken ct, int megabytes = 256)
    {
        byte[] src, dst;
        try
        {
            src = GC.AllocateUninitializedArray<byte>(megabytes * 1024 * 1024);
            dst = GC.AllocateUninitializedArray<byte>(megabytes * 1024 * 1024);
        }
        catch (OutOfMemoryException)
        {
            return null;
        }

        Random.Shared.NextBytes(src.AsSpan(0, Math.Min(src.Length, 1 << 20)));
        src.AsSpan().CopyTo(dst); // chạm vào mọi trang trước khi đo

        var threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
        var segment = src.Length / threads;
        var bytes = new long[threads];
        var sw = Stopwatch.StartNew();
        var limit = TimeSpan.FromSeconds(seconds);
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = ct }, t =>
        {
            var from = src.AsSpan(t * segment, segment);
            var to = dst.AsSpan(t * segment, segment);
            while (sw.Elapsed < limit && !ct.IsCancellationRequested)
            {
                from.CopyTo(to);
                bytes[t] += segment;
            }
        });
        ct.ThrowIfCancellationRequested();
        return bytes.Sum() / sw.Elapsed.TotalSeconds / 1e9;
    }

    /// <summary>Đi theo chuỗi con trỏ ngẫu nhiên trong 64 MB (vượt bộ nhớ đệm CPU) – thời gian mỗi lần truy cập.</summary>
    internal static double? MemoryLatency(CancellationToken ct, int entries = 16 * 1024 * 1024, int steps = 20_000_000)
    {
        int[] next;
        try
        {
            next = GC.AllocateUninitializedArray<int>(entries);
        }
        catch (OutOfMemoryException)
        {
            return null;
        }

        // Hoán vị vòng Sattolo: một chu trình duy nhất đi qua mọi phần tử.
        for (var i = 0; i < entries; i++) next[i] = i;
        var rng = new Random(42);
        for (var i = entries - 1; i > 0; i--)
        {
            var j = rng.Next(i);
            (next[i], next[j]) = (next[j], next[i]);
        }

        ct.ThrowIfCancellationRequested();
        var index = 0;
        for (var i = 0; i < 200_000; i++) index = next[index]; // khởi động
        var sw = Stopwatch.StartNew();
        index = Chase(next, index, steps);
        sw.Stop();
        GC.KeepAlive(index);
        return sw.Elapsed.TotalMilliseconds * 1e6 / steps;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static int Chase(int[] next, int index, int steps)
    {
        for (var i = 0; i < steps; i++) index = next[index];
        return index;
    }

    // ======================= Ổ đĩa =======================

    private const int Block = 1024 * 1024;
    private const int Small = 4096;
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    /// <summary>Tệp thử nằm trong thư mục tạm (ổ Windows) hoặc gốc ổ khác; bỏ qua bộ đệm Windows để đo đúng tốc độ ổ.</summary>
    internal static string TestFilePath(char letter)
    {
        var temp = Path.GetTempPath();
        return char.ToUpperInvariant(temp[0]) == letter
            ? Path.Combine(temp, $"pccheck-bench-{Environment.ProcessId}.tmp")
            : $"{letter}:\\pccheck-bench-{Environment.ProcessId}.tmp";
    }

    /// <summary>Kích thước tệp thử: tối đa <paramref name="max"/>, không quá 1/4 dung lượng trống, tối thiểu 64 MB.</summary>
    internal static long TestFileSize(long freeBytes, long max) =>
        Math.Max(64L * Block, Math.Min(max, freeBytes / 4)) / Block * Block;

    private static unsafe void DiskTest(char letter, long maxBytes, BenchmarkResult result, Action<string> report, CancellationToken ct)
    {
        var info = new DriveInfo(letter + ":\\");
        if (!info.IsReady)
        {
            result.DiskError = "bench.error.disk_not_ready";
            return;
        }

        if (info.AvailableFreeSpace < 512L * Block)
        {
            result.DiskError = "bench.error.disk_full";
            return;
        }

        var size = TestFileSize(info.AvailableFreeSpace, maxBytes);
        var path = TestFilePath(letter);
        var buffer = (byte*)NativeMemory.AlignedAlloc(Block, 4096);
        try
        {
            Random.Shared.NextBytes(new Span<byte>(buffer, Block)); // dữ liệu ngẫu nhiên – SSD không nén được
            using var handle = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                FileOptions.WriteThrough | NoBuffering | FileOptions.DeleteOnClose, size);

            report("disk_seq_write");
            (result.DiskSeqWriteMBps, var written) = Sequential(handle, buffer, size, write: true, ct);

            // Chỉ đọc phần đã ghi thật: vùng cấp phát trước chưa ghi được NTFS trả số 0 ngay, làm số đo đọc ảo.
            report("disk_seq_read");
            (result.DiskSeqReadMBps, _) = Sequential(handle, buffer, written, write: false, ct);
            report("disk_rand_read");
            result.DiskRandReadMBps = Random4K(handle, buffer, written, write: false, ct);
            report("disk_rand_write");
            result.DiskRandWriteMBps = Random4K(handle, buffer, written, write: true, ct);
        }
        finally
        {
            NativeMemory.AlignedFree(buffer);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static unsafe (double MBps, long Bytes) Sequential(SafeFileHandle handle, byte* buffer, long size, bool write, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long offset = 0;
        var limit = TimeSpan.FromSeconds(12);
        while (offset < size && sw.Elapsed < limit)
        {
            ct.ThrowIfCancellationRequested();
            if (write) RandomAccess.Write(handle, new ReadOnlySpan<byte>(buffer, Block), offset);
            else RandomAccess.Read(handle, new Span<byte>(buffer, Block), offset);
            offset += Block;
        }

        return (offset / sw.Elapsed.TotalSeconds / (1024 * 1024), offset);
    }

    /// <summary>Đọc/ghi khối 4 KB ở vị trí ngẫu nhiên, hàng đợi 1 (giống mở ứng dụng, khởi động Windows).</summary>
    private static unsafe double Random4K(SafeFileHandle handle, byte* buffer, long size, bool write, CancellationToken ct)
    {
        var blocks = size / Small;
        var rng = new Random(7);
        var sw = Stopwatch.StartNew();
        long count = 0;
        var limit = TimeSpan.FromSeconds(4);
        while (sw.Elapsed < limit)
        {
            if ((count & 63) == 0) ct.ThrowIfCancellationRequested();
            var offset = rng.NextInt64(blocks) * Small;
            if (write) RandomAccess.Write(handle, new ReadOnlySpan<byte>(buffer, Small), offset);
            else RandomAccess.Read(handle, new Span<byte>(buffer, Small), offset);
            count++;
        }

        return count * Small / sw.Elapsed.TotalSeconds / (1024 * 1024);
    }

    /// <summary>Loại ổ (HDD / SSD SATA / NVMe / USB) và tên ổ chứa ký tự ổ này.</summary>
    internal static (DriveMedia Media, string Model) DriveInfoFor(char letter)
    {
        try
        {
            var partition = Wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Wmi.Storage)
                .FirstOrDefault(p => Partitions.PartitionReader.LetterOf(p) == letter);
            if (partition?.Int("DiskNumber") is not { } number) return (DriveMedia.Unknown, "");
            var disk = Wmi.Query("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk", Wmi.Storage)
                .FirstOrDefault(d => d.Str("DeviceId") == number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (disk is null) return (DriveMedia.Unknown, "");
            return (ClassifyMedia(disk.Int("BusType") ?? 0, disk.Int("MediaType") ?? 0), disk.Str("FriendlyName"));
        }
        catch (Exception ex)
        {
            Log.Warn("Xác định loại ổ lỗi", ex);
            return (DriveMedia.Unknown, "");
        }
    }

    /// <summary>BusType: 7 USB, 17 NVMe; MediaType: 3 HDD, 4 SSD.</summary>
    internal static DriveMedia ClassifyMedia(int busType, int mediaType) => busType switch
    {
        7 or 12 or 13 => DriveMedia.Usb,
        17 => DriveMedia.Nvme,
        _ => mediaType switch
        {
            3 => DriveMedia.Hdd,
            4 => DriveMedia.SataSsd,
            _ => DriveMedia.Unknown,
        },
    };
}
