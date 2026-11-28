using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Performance;

public enum OptimizationState
{
    /// <summary>Đã tối ưu.</summary>
    Good,

    /// <summary>Nên chỉnh để nhanh hơn.</summary>
    Suggest,

    /// <summary>Đang làm máy chậm rõ rệt.</summary>
    Problem,
}

public enum OptimizationImpact
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>Một mục kiểm tra tối ưu: trạng thái, mức ảnh hưởng, giải thích, các bước xử lý và nút sửa (nếu có).</summary>
public sealed class OptimizationItem
{
    public required string Id { get; init; }

    /// <summary>Nhóm: power, startup, storage, memory, graphics, system.</summary>
    public required string Group { get; init; }

    public required OptimizationState State { get; init; }
    public required OptimizationImpact Impact { get; init; }
    public required LocText Title { get; init; }
    public required LocText Detail { get; init; }

    /// <summary>Các bước xử lý (mỗi bước một dòng) – chỉ có ở mục chưa tối ưu.</summary>
    public LocText? Steps { get; init; }

    public FixAction? Fix { get; init; }
}

/// <summary>Số liệu hệ thống dùng để kiểm tra tối ưu (null = không đọc được, mục đó được bỏ qua).</summary>
public sealed class OptimizationData
{
    public Guid? PowerScheme { get; set; }
    public bool HasHighPerformanceScheme { get; set; }

    /// <summary>Chế độ điện năng của Windows 11 / laptop (overlay): hiệu quả nhất / cân bằng / hiệu năng cao nhất.</summary>
    public Guid? PowerModeOverlay { get; set; }

    public bool IsLaptop { get; set; }

    public int? StartupEnabled { get; set; }

    public long MemoryTotal { get; set; }
    public long MemoryAvailable { get; set; }
    public int MemoryModules { get; set; }

    /// <summary>RAM chạy thấp hơn tốc độ định mức (chưa bật XMP/EXPO).</summary>
    public bool MemoryBelowRated { get; set; }

    public int MemoryRatedMhz { get; set; }
    public int MemoryConfiguredMhz { get; set; }

    public long SystemDriveTotal { get; set; }
    public long SystemDriveFree { get; set; }
    public bool? SystemDriveIsHdd { get; set; }

    /// <summary>TRIM bị tắt (fsutil DisableDeleteNotify = 1).</summary>
    public bool? TrimDisabled { get; set; }

    /// <summary>Tác vụ tối ưu ổ đĩa định kỳ (ScheduledDefrag) đang bật.</summary>
    public bool? ScheduledOptimizeEnabled { get; set; }

    /// <summary>0 tự động, 1 đẹp nhất, 2 nhanh nhất, 3 tùy chỉnh.</summary>
    public int? VisualEffects { get; set; }

    public bool? Transparency { get; set; }
    public bool? GameMode { get; set; }

    /// <summary>Game Bar ghi hình nền liên tục ("Record what happened").</summary>
    public bool? BackgroundRecording { get; set; }

    /// <summary>Lập lịch GPU tăng tốc phần cứng: 2 bật, 1 tắt, null không có.</summary>
    public int? HardwareGpuScheduling { get; set; }

    public bool HasDedicatedGpu { get; set; }

    /// <summary>Kiểu khởi động dịch vụ SysMain (2 tự động, 3 thủ công, 4 tắt).</summary>
    public int? SysMainStart { get; set; }

    public TimeSpan Uptime { get; set; }
    public bool PendingReboot { get; set; }

    /// <summary>Tổng tệp rác (từ lần quét gần nhất), null nếu chưa quét.</summary>
    public long? JunkBytes { get; set; }

    public int PhysicalCores { get; set; }
}

/// <summary>Đánh giá mức tối ưu (thuần logic, dễ kiểm thử).</summary>
public static class OptimizationAnalyzer
{
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    public static readonly Guid OverlayBestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid OverlayBestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public const long GiB = 1024L * 1024 * 1024;

    public static List<OptimizationItem> Analyze(OptimizationData d)
    {
        var items = new List<OptimizationItem>();
        Power(d, items);
        Startup(d, items);
        Memory(d, items);
        Storage(d, items);
        Graphics(d, items);
        SystemState(d, items);
        return items
            .OrderByDescending(i => i.State)
            .ThenByDescending(i => i.Impact)
            .ToList();
    }

    /// <summary>Điểm tối ưu 0–100: trừ theo mức ảnh hưởng của từng mục chưa tối ưu.</summary>
    public static int Score(IEnumerable<OptimizationItem> items)
    {
        var penalty = items.Sum(i => i.State switch
        {
            OptimizationState.Problem => i.Impact switch { OptimizationImpact.High => 15, OptimizationImpact.Medium => 8, _ => 4 },
            OptimizationState.Suggest => i.Impact switch { OptimizationImpact.High => 8, OptimizationImpact.Medium => 4, _ => 2 },
            _ => 0,
        });
        return Math.Clamp(100 - penalty, 0, 100);
    }

    private static OptimizationItem Item(string id, string group, OptimizationState state, OptimizationImpact impact, LocText detail, FixAction? fix = null) => new()
    {
        Id = id,
        Group = group,
        State = state,
        Impact = impact,
        Title = L($"opt.{id}.title"),
        Detail = detail,
        Steps = state == OptimizationState.Good ? null : L($"opt.{id}.steps"),
        Fix = state == OptimizationState.Good ? null : fix,
    };

    private static FixAction Tool(string key) =>
        new(FixKind.RunTool, OptimizationFixes.Prefix + key, L($"opt.fix.{key}"), true) { ConfirmMessage = L($"opt.fix.{key}.confirm") };

    private static void Power(OptimizationData d, List<OptimizationItem> items)
    {
        if (d.PowerScheme is not { } scheme) return;

        if (scheme == PowerSaver || d.PowerModeOverlay == OverlayBestEfficiency)
        {
            items.Add(Item("power_plan", "power", OptimizationState.Problem, OptimizationImpact.High, L("opt.power_plan.saver"),
                d.HasHighPerformanceScheme && !d.IsLaptop ? Tool("power-high") : FixCatalog.Uri("ms-settings:powersleep", "opt.fix.power-settings")));
        }
        else if (scheme == HighPerformance || scheme == UltimatePerformance || d.PowerModeOverlay == OverlayBestPerformance)
        {
            items.Add(Item("power_plan", "power", OptimizationState.Good, OptimizationImpact.High, L("opt.power_plan.good")));
        }
        else if (d.IsLaptop)
        {
            // Laptop: "Cân bằng" là hợp lý (pin, nhiệt) – chỉ gợi ý khi cắm sạc muốn nhanh hơn.
            items.Add(Item("power_plan", "power", OptimizationState.Good, OptimizationImpact.High, L("opt.power_plan.laptop_balanced")));
        }
        else
        {
            items.Add(Item("power_plan", "power", OptimizationState.Suggest, OptimizationImpact.Medium, L("opt.power_plan.balanced"),
                d.HasHighPerformanceScheme ? Tool("power-high") : FixCatalog.Uri("ms-settings:powersleep", "opt.fix.power-settings")));
        }
    }

    private static void Startup(OptimizationData d, List<OptimizationItem> items)
    {
        if (d.StartupEnabled is not { } count) return;
        var state = count > 15 ? OptimizationState.Problem : count > 8 ? OptimizationState.Suggest : OptimizationState.Good;
        items.Add(Item("startup", "startup", state, OptimizationImpact.Medium, L("opt.startup.detail", count), FixCatalog.StartupPage));
    }

    private static void Memory(OptimizationData d, List<OptimizationItem> items)
    {
        if (d.MemoryTotal > 0)
        {
            var usedPercent = 100.0 * (d.MemoryTotal - d.MemoryAvailable) / d.MemoryTotal;
            var state = usedPercent >= 90 ? OptimizationState.Problem : usedPercent >= 80 ? OptimizationState.Suggest : OptimizationState.Good;
            items.Add(Item("ram_usage", "memory", state, OptimizationImpact.Medium, L("opt.ram_usage.detail", Math.Round(usedPercent), new ByteSize(d.MemoryAvailable))));

            var totalGb = d.MemoryTotal / (double)GiB;
            if (totalGb < 7.5)
            {
                items.Add(Item("ram_size", "memory", totalGb < 3.8 ? OptimizationState.Problem : OptimizationState.Suggest, OptimizationImpact.High,
                    L("opt.ram_size.detail", new ByteSize(d.MemoryTotal))));
            }
        }

        if (d.MemoryBelowRated)
        {
            items.Add(Item("ram_xmp", "memory", OptimizationState.Suggest, OptimizationImpact.Medium,
                L("opt.ram_xmp.detail", d.MemoryConfiguredMhz, d.MemoryRatedMhz), FixCatalog.CompatibilityPage));
        }

        if (d.MemoryModules == 1 && !d.IsLaptop)
        {
            items.Add(Item("ram_single", "memory", OptimizationState.Suggest, OptimizationImpact.Medium, L("opt.ram_single.detail")));
        }
    }

    private static void Storage(OptimizationData d, List<OptimizationItem> items)
    {
        if (d.SystemDriveTotal > 0)
        {
            var freePercent = 100.0 * d.SystemDriveFree / d.SystemDriveTotal;
            var state = freePercent < 10 || d.SystemDriveFree < 10 * GiB ? OptimizationState.Problem
                : freePercent < 20 ? OptimizationState.Suggest
                : OptimizationState.Good;
            items.Add(Item("disk_space", "storage", state, OptimizationImpact.High, L("opt.disk_space.detail", new ByteSize(d.SystemDriveFree), Math.Round(freePercent)),
                FixCatalog.CleanupPage));
        }

        if (d.SystemDriveIsHdd == true)
        {
            items.Add(Item("system_hdd", "storage", OptimizationState.Problem, OptimizationImpact.High, L("opt.system_hdd.detail")));
        }

        if (d.TrimDisabled is { } trimOff && d.SystemDriveIsHdd != true)
        {
            items.Add(Item("trim", "storage", trimOff ? OptimizationState.Problem : OptimizationState.Good, OptimizationImpact.Medium,
                L(trimOff ? "opt.trim.off" : "opt.trim.on"), Tool("trim-on")));
        }

        if (d.ScheduledOptimizeEnabled is { } scheduled)
        {
            items.Add(Item("optimize_schedule", "storage", scheduled ? OptimizationState.Good : OptimizationState.Suggest, OptimizationImpact.Low,
                L(scheduled ? "opt.optimize_schedule.on" : "opt.optimize_schedule.off"), Tool("optimize-on")));
        }

        if (d.SysMainStart == 4 && d.SystemDriveIsHdd == true)
        {
            items.Add(Item("sysmain", "storage", OptimizationState.Suggest, OptimizationImpact.Medium, L("opt.sysmain.detail"), Tool("sysmain-on")));
        }

        if (d.JunkBytes is { } junk && junk > 2 * GiB)
        {
            items.Add(Item("junk", "storage", junk > 10 * GiB ? OptimizationState.Problem : OptimizationState.Suggest, OptimizationImpact.Low,
                L("opt.junk.detail", new ByteSize(junk)), FixCatalog.CleanupPage));
        }
    }

    private static bool LowEnd(OptimizationData d) =>
        (d.MemoryTotal > 0 && d.MemoryTotal < 7.5 * GiB) || (d.PhysicalCores is > 0 and <= 2);

    private static void Graphics(OptimizationData d, List<OptimizationItem> items)
    {
        if (d.GameMode is { } gameMode)
        {
            items.Add(Item("game_mode", "graphics", gameMode ? OptimizationState.Good : OptimizationState.Suggest, OptimizationImpact.Low,
                L(gameMode ? "opt.game_mode.on" : "opt.game_mode.off"), Tool("gamemode-on")));
        }

        if (d.BackgroundRecording is { } recording)
        {
            items.Add(Item("bg_recording", "graphics", recording ? OptimizationState.Suggest : OptimizationState.Good, OptimizationImpact.Medium,
                L(recording ? "opt.bg_recording.on" : "opt.bg_recording.off"), Tool("bgrecord-off")));
        }

        if (d.HardwareGpuScheduling == 1 && d.HasDedicatedGpu)
        {
            items.Add(Item("hags", "graphics", OptimizationState.Suggest, OptimizationImpact.Low, L("opt.hags.detail"),
                FixCatalog.Uri("ms-settings:display-advancedgraphics", "opt.fix.graphics-settings")));
        }

        if (LowEnd(d))
        {
            if (d.Transparency == true)
            {
                items.Add(Item("transparency", "graphics", OptimizationState.Suggest, OptimizationImpact.Low, L("opt.transparency.detail"), Tool("transparency-off")));
            }

            if (d.VisualEffects is not 2)
            {
                items.Add(Item("visual_effects", "graphics", OptimizationState.Suggest, OptimizationImpact.Low, L("opt.visual_effects.detail"),
                    FixCatalog.Tool(SystemTools.PerformanceOptions, "opt.fix.visual-effects")));
            }
        }
    }

    private static void SystemState(OptimizationData d, List<OptimizationItem> items)
    {
        if (d.PendingReboot)
        {
            items.Add(Item("pending_reboot", "system", OptimizationState.Suggest, OptimizationImpact.Medium, L("opt.pending_reboot.detail"),
                new FixAction(FixKind.Restart, null, L("opt.fix.restart"), true) { ConfirmMessage = L("opt.fix.restart.confirm") }));
        }
        else if (d.Uptime.TotalDays >= 7)
        {
            items.Add(Item("uptime", "system", OptimizationState.Suggest, OptimizationImpact.Low, L("opt.uptime.detail", (int)d.Uptime.TotalDays),
                new FixAction(FixKind.Restart, null, L("opt.fix.restart"), true) { ConfirmMessage = L("opt.fix.restart.confirm") }));
        }
    }
}
