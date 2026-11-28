using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Performance;

namespace PcCheck.Core.Tests;

public sealed class PerformanceTests
{
    private const long GiB = OptimizationAnalyzer.GiB;

    private static OptimizationData Healthy() => new()
    {
        PowerScheme = OptimizationAnalyzer.HighPerformance,
        HasHighPerformanceScheme = true,
        StartupEnabled = 5,
        MemoryTotal = 16 * GiB,
        MemoryAvailable = 9 * GiB,
        MemoryModules = 2,
        SystemDriveTotal = 500 * GiB,
        SystemDriveFree = 200 * GiB,
        SystemDriveIsHdd = false,
        TrimDisabled = false,
        ScheduledOptimizeEnabled = true,
        VisualEffects = 0,
        Transparency = true,
        GameMode = true,
        BackgroundRecording = false,
        HardwareGpuScheduling = 2,
        SysMainStart = 2,
        Uptime = TimeSpan.FromHours(5),
        PhysicalCores = 8,
    };

    [Fact]
    public void HealthySystemScoresFullMarks()
    {
        var items = OptimizationAnalyzer.Analyze(Healthy());
        Assert.All(items, i => Assert.Equal(OptimizationState.Good, i.State));
        Assert.All(items, i => Assert.Null(i.Fix));
        Assert.Equal(100, OptimizationAnalyzer.Score(items));
        Assert.DoesNotContain(items, i => i.Id is "transparency" or "visual_effects"); // máy mạnh: không gợi ý tắt hiệu ứng
    }

    [Fact]
    public void SlowSystemGetsProblemsWithFixesSortedFirst()
    {
        var d = Healthy();
        d.PowerScheme = OptimizationAnalyzer.PowerSaver;
        d.StartupEnabled = 20;
        d.MemoryTotal = 4 * GiB;
        d.MemoryAvailable = (long)(0.3 * GiB);
        d.MemoryModules = 1;
        d.SystemDriveFree = 8 * GiB;
        d.SystemDriveIsHdd = true;
        d.TrimDisabled = true;
        d.ScheduledOptimizeEnabled = false;
        d.SysMainStart = 4;
        d.BackgroundRecording = true;
        d.GameMode = false;
        d.PendingReboot = true;
        d.JunkBytes = 12 * GiB;

        var items = OptimizationAnalyzer.Analyze(d);
        var byId = items.ToDictionary(i => i.Id);

        Assert.Equal(OptimizationState.Problem, byId["power_plan"].State);
        Assert.Equal("opt:power-high", byId["power_plan"].Fix!.Arg);
        Assert.Equal(OptimizationState.Problem, byId["startup"].State);
        Assert.Equal(OptimizationState.Problem, byId["ram_usage"].State);
        Assert.Equal(OptimizationState.Suggest, byId["ram_size"].State);
        Assert.Equal(OptimizationState.Problem, byId["disk_space"].State);
        Assert.Equal(OptimizationState.Problem, byId["system_hdd"].State);
        Assert.False(byId.ContainsKey("trim")); // ổ hệ thống là HDD: TRIM không áp dụng
        Assert.Equal("opt:optimize-on", byId["optimize_schedule"].Fix!.Arg);
        Assert.Equal("opt:sysmain-on", byId["sysmain"].Fix!.Arg);
        Assert.Equal("opt:bgrecord-off", byId["bg_recording"].Fix!.Arg);
        Assert.Equal("opt:gamemode-on", byId["game_mode"].Fix!.Arg);
        Assert.Equal(FixKind.Restart, byId["pending_reboot"].Fix!.Kind);
        Assert.True(byId["pending_reboot"].Fix!.RequiresConfirmation);
        Assert.False(byId.ContainsKey("uptime")); // đã có mục chờ khởi động lại
        Assert.Contains(items, i => i.Id == "transparency"); // máy yếu
        Assert.Contains(items, i => i.Id == "visual_effects");

        // Mục làm chậm nhiều nhất lên đầu, mục đã tốt xuống cuối.
        Assert.Equal(OptimizationState.Problem, items[0].State);
        Assert.Equal(OptimizationImpact.High, items[0].Impact);
        Assert.True(OptimizationAnalyzer.Score(items) < 40);
        Assert.All(items.Where(i => i.State != OptimizationState.Good), i => Assert.NotNull(i.Steps));
    }

    [Fact]
    public void LaptopOnBalancedIsFineButPowerSaverIsNot()
    {
        var d = Healthy();
        d.IsLaptop = true;
        d.PowerScheme = OptimizationAnalyzer.Balanced;
        Assert.Equal(OptimizationState.Good, OptimizationAnalyzer.Analyze(d).Single(i => i.Id == "power_plan").State);

        d.PowerModeOverlay = OptimizationAnalyzer.OverlayBestEfficiency;
        var item = OptimizationAnalyzer.Analyze(d).Single(i => i.Id == "power_plan");
        Assert.Equal(OptimizationState.Problem, item.State);
        Assert.Equal(FixKind.OpenUri, item.Fix!.Kind); // laptop: mở cài đặt, không ép gói Hiệu năng cao

        var desktop = Healthy();
        desktop.PowerScheme = OptimizationAnalyzer.Balanced;
        Assert.Equal(OptimizationState.Suggest, OptimizationAnalyzer.Analyze(desktop).Single(i => i.Id == "power_plan").State);
    }

    [Fact]
    public void LongUptimeSuggestsRestart()
    {
        var d = Healthy();
        d.Uptime = TimeSpan.FromDays(12);
        var item = OptimizationAnalyzer.Analyze(d).Single(i => i.Id == "uptime");
        Assert.Equal(OptimizationState.Suggest, item.State);
        Assert.Equal("12", item.Detail.Args[0]?.ToString());
    }

    [Theory]
    [InlineData("NTFS DisableDeleteNotify = 0  (Allows TRIM operations to be sent to the storage device)\r\nReFS DisableDeleteNotify = 0", false)]
    [InlineData("NTFS DisableDeleteNotify = 1  (Disallows TRIM)\r\nReFS DisableDeleteNotify = 0", true)]
    [InlineData("DisableDeleteNotify = 1", true)]
    [InlineData("ReFS DisableDeleteNotify is not currently set", null)]
    [InlineData("", null)]
    public void ParsesTrimState(string output, bool? disabled) => Assert.Equal(disabled, OptimizationProbe.ParseTrimDisabled(output));

    [Fact]
    public void ParsesScheduledTaskEnabledFromXml()
    {
        const string on = "<?xml version=\"1.0\"?><Task><Triggers /><Settings><Enabled>true</Enabled></Settings></Task>";
        const string off = "<?xml version=\"1.0\"?><Task><Triggers><TimeTrigger><Enabled>true</Enabled></TimeTrigger></Triggers><Settings><Enabled>false</Enabled></Settings></Task>";
        const string missing = "<?xml version=\"1.0\"?><Task><Settings><Hidden>true</Hidden></Settings></Task>";
        Assert.True(OptimizationProbe.ParseTaskEnabled(on));
        Assert.False(OptimizationProbe.ParseTaskEnabled(off));
        Assert.True(OptimizationProbe.ParseTaskEnabled(missing));
        Assert.Null(OptimizationProbe.ParseTaskEnabled("ERROR: The system cannot find the file specified."));
    }

    [Fact]
    public void ParsesActivePowerScheme()
    {
        Assert.Equal(OptimizationAnalyzer.Balanced,
            OptimizationProbe.ParseGuid("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)"));
        Assert.Null(OptimizationProbe.ParseGuid("Access denied"));
    }

    [Theory]
    [InlineData(17, 4, DriveMedia.Nvme)]
    [InlineData(11, 4, DriveMedia.SataSsd)]
    [InlineData(11, 3, DriveMedia.Hdd)]
    [InlineData(7, 4, DriveMedia.Usb)]
    [InlineData(11, 0, DriveMedia.Unknown)]
    public void ClassifiesDriveMedia(int bus, int media, DriveMedia expected) => Assert.Equal(expected, Benchmark.ClassifyMedia(bus, media));

    [Fact]
    public void RatesDiskSpeedAgainstDriveType()
    {
        Assert.Equal(Severity.Ok, BenchmarkRating.RateSeqRead(DriveMedia.Nvme, 3000));
        Assert.Equal(Severity.Warning, BenchmarkRating.RateSeqRead(DriveMedia.Nvme, 500));
        Assert.Equal(Severity.Ok, BenchmarkRating.RateSeqRead(DriveMedia.SataSsd, 520));
        Assert.Equal(Severity.Warning, BenchmarkRating.RateSeqRead(DriveMedia.SataSsd, 180)); // SSD trên cổng SATA2 / cáp lỗi
        Assert.Equal(Severity.Ok, BenchmarkRating.RateSeqRead(DriveMedia.Hdd, 150));
        Assert.Equal(Severity.Info, BenchmarkRating.RateSeqRead(DriveMedia.Usb, 30)); // không rõ chuẩn -> không cảnh báo
    }

    [Fact]
    public void NotesExplainSlowParts()
    {
        var slow = new BenchmarkResult
        {
            CpuSingle = 1000,
            CpuMulti = 1500, // 8 nhân mà chỉ nhanh gấp 1.5 lần
            MemoryBandwidthGBps = 4,
            MemoryLatencyNs = 150,
            Media = DriveMedia.SataSsd,
            DiskSeqReadMBps = 150,
            InternetDownMbps = 5,
            InternetPingMs = 200,
        };
        var ids = BenchmarkRating.Notes(slow, 8, hasDualChannelInfo: true, singleChannel: true).Select(n => n.Id).ToList();
        Assert.Contains("cpu_scaling", ids);
        Assert.Contains("ram_bandwidth", ids);
        Assert.Contains("ram_latency", ids);
        Assert.Contains("disk_slow", ids);
        Assert.Contains("net_slow", ids);
        Assert.Contains("net_ping", ids);
        Assert.Equal("bench.note.ram_single_channel", BenchmarkRating.Notes(slow, 8, true, true).Single(n => n.Id == "ram_bandwidth").Text.Key);

        var fast = new BenchmarkResult { CpuSingle = 1000, CpuMulti = 7000, MemoryBandwidthGBps = 30, MemoryLatencyNs = 70, Media = DriveMedia.Nvme, DiskSeqReadMBps = 3000, DiskRandReadMBps = 60 };
        Assert.Equal("all_good", Assert.Single(BenchmarkRating.Notes(fast, 8)).Id);
    }

    [Fact]
    public void ChangeAgainstPreviousRun()
    {
        Assert.Equal(10, BenchmarkRating.Change(110, 100)!.Value, 3);
        Assert.Equal(20, BenchmarkRating.Change(80, 100, lowerIsBetter: true)!.Value, 3); // độ trễ giảm = tốt hơn
        Assert.Null(BenchmarkRating.Change(100, null));
        Assert.Null(BenchmarkRating.Change(null, 100));
    }

    [Fact]
    public void DiskTestFileSizeRespectsFreeSpace()
    {
        const long mb = 1024 * 1024;
        Assert.Equal(1024 * mb, Benchmark.TestFileSize(100 * GiB, 1024 * mb));
        Assert.Equal(256 * mb, Benchmark.TestFileSize(1024 * mb, 1024 * mb)); // tối đa 1/4 chỗ trống
        Assert.Equal(64 * mb, Benchmark.TestFileSize(100 * mb, 1024 * mb)); // tối thiểu 64 MB
        Assert.Equal(0, Benchmark.TestFileSize(10 * GiB + 12345, 1024 * mb) % mb); // căn theo 1 MB (yêu cầu của NoBuffering)
    }

    [Fact]
    public void CpuAndMemoryBenchmarksProduceNumbers()
    {
        Assert.Equal(Benchmark.CpuKernel(1000, 5), Benchmark.CpuKernel(1000, 5)); // tất định
        Assert.True(Benchmark.CpuSingle(0.05, CancellationToken.None) > 0);
        Assert.True(Benchmark.MemoryBandwidth(0.05, CancellationToken.None, megabytes: 8) > 0);
        Assert.True(Benchmark.MemoryLatency(CancellationToken.None, entries: 1 << 16, steps: 100_000) > 0);
    }

    [Fact]
    public void CancellationStopsTheBenchmark()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Benchmark.CpuSingle(5, cts.Token));
    }

    [Fact]
    public void BenchmarkResultRoundTripsThroughStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pccheck-bench-test-{Guid.NewGuid():N}.json");
        var old = BenchmarkStore.FilePath;
        BenchmarkStore.FilePath = path;
        try
        {
            Assert.Null(BenchmarkStore.Load());
            BenchmarkStore.Save(new BenchmarkResult { CpuSingle = 1234, Drive = "C:", Media = DriveMedia.Nvme, DiskSeqReadMBps = 3100 });
            var loaded = BenchmarkStore.Load();
            Assert.Equal(1234, loaded!.CpuSingle);
            Assert.Equal(DriveMedia.Nvme, loaded.Media);
        }
        finally
        {
            BenchmarkStore.FilePath = old;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void EveryOptimizationTextExists()
    {
        var vi = Loc.Load(Loc.Vietnamese);
        var d = Healthy();
        d.PowerScheme = OptimizationAnalyzer.PowerSaver;
        d.MemoryTotal = 3 * GiB;
        d.MemoryAvailable = GiB / 10;
        d.MemoryModules = 1;
        d.MemoryBelowRated = true;
        d.SystemDriveIsHdd = true;
        d.SysMainStart = 4;
        d.JunkBytes = 5 * GiB;
        d.HardwareGpuScheduling = 1;
        d.HasDedicatedGpu = true;
        d.Uptime = TimeSpan.FromDays(30);
        var items = OptimizationAnalyzer.Analyze(d).Concat(OptimizationAnalyzer.Analyze(Healthy()));
        foreach (var item in items)
        {
            Assert.True(vi.ContainsKey(item.Title.Key), item.Title.Key);
            Assert.True(vi.ContainsKey(item.Detail.Key), item.Detail.Key);
            if (item.Steps is { } steps) Assert.True(vi.ContainsKey(steps.Key), steps.Key);
            if (item.Fix is { } fix)
            {
                Assert.True(vi.ContainsKey(fix.Label.Key), fix.Label.Key);
                if (fix.ConfirmMessage is { } c) Assert.True(vi.ContainsKey(c.Key), c.Key);
            }

            Assert.True(vi.ContainsKey($"opt.group.{item.Group}"), item.Group);
        }

        foreach (var key in OptimizationFixes.Keys)
        {
            Assert.True(vi.ContainsKey($"opt.fix.{key}"), key);
            Assert.True(vi.ContainsKey($"opt.fix.{key}.confirm"), key);
        }

        foreach (var media in Enum.GetValues<DriveMedia>()) Assert.True(vi.ContainsKey($"bench.media.{media.ToString().ToLowerInvariant()}"), media.ToString());
        foreach (var state in Enum.GetValues<OptimizationState>()) Assert.True(vi.ContainsKey($"opt.state.{state.ToString().ToLowerInvariant()}"));
        foreach (var impact in Enum.GetValues<OptimizationImpact>()) Assert.True(vi.ContainsKey($"opt.impact.{impact.ToString().ToLowerInvariant()}"));
        foreach (var stage in new[] { "cpu_single", "cpu_multi", "memory_bandwidth", "memory_latency", "disk_seq_write", "disk_seq_read", "disk_rand_read", "disk_rand_write", "net_ping", "net_down", "net_up", "done" })
        {
            Assert.True(vi.ContainsKey($"bench.stage.{stage}"), stage);
        }
    }

    /// <summary>Trên Windows (CI): đo được ổ đĩa thật – tệp thử được tạo rồi tự xóa.</summary>
    [Fact]
    public async Task DiskBenchmarkRunsOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var drive = Path.GetPathRoot(Path.GetTempPath())![..1];
        var result = await Benchmark.RunAsync(new BenchmarkOptions { Drive = drive, Seconds = 0.1, DiskFileBytes = 64L * 1024 * 1024 });
        Assert.Null(result.DiskError);
        Assert.True(result.DiskSeqWriteMBps > 0);
        Assert.True(result.DiskSeqReadMBps > 0);
        Assert.True(result.DiskRandReadMBps > 0);
        Assert.False(File.Exists(Benchmark.TestFilePath(char.ToUpperInvariant(drive[0]))));
    }
}
