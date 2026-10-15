using PcCheck.App.Infrastructure;
using PcCheck.Core.Analysis;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

public sealed class CategoryTile(string glyph, string title, int count, Severity severity, string status, string page)
{
    public string Glyph { get; } = glyph;
    public string Title { get; } = title;
    public int Count { get; } = count;
    public Severity Severity { get; } = severity;
    public string Status { get; } = status;
    public string Page { get; } = page;
}

public sealed class DashboardViewModel : PageViewModel
{
    private readonly History _cpuHistory = new();
    private readonly History _ramHistory = new();
    private readonly History _gpuHistory = new();
    private int _score;
    private string _grade = "";
    private string _headline = "";
    private Severity _scoreSeverity = Severity.Info;
    private IReadOnlyList<InfoItem> _systemItems = [];
    private IReadOnlyList<CategoryTile> _categories = [];
    private IReadOnlyList<IssueItem> _topIssues = [];

    public DashboardViewModel(MainViewModel main) : base(main)
    {
        Rebuild();
    }

    public StatTile CpuTile { get; } = new("", "dash.tile.cpu");
    public StatTile GpuTile { get; } = new("", "dash.tile.gpu");
    public StatTile RamTile { get; } = new("", "dash.tile.ram");
    public StatTile DiskTile { get; } = new("", "dash.tile.disk");

    public int Score { get => _score; private set => Set(ref _score, value); }
    public string Grade { get => _grade; private set => Set(ref _grade, value); }
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public Severity ScoreSeverity { get => _scoreSeverity; private set => Set(ref _scoreSeverity, value); }
    public int CriticalCount => State.Result?.Count(Severity.Critical) ?? 0;
    public int WarningCount => State.Result?.Count(Severity.Warning) ?? 0;
    public int InfoCount => State.Result?.Count(Severity.Info) ?? 0;
    public IReadOnlyList<InfoItem> SystemItems { get => _systemItems; private set => Set(ref _systemItems, value); }
    public IReadOnlyList<CategoryTile> Categories { get => _categories; private set => Set(ref _categories, value); }
    public IReadOnlyList<IssueItem> TopIssues { get => _topIssues; private set => Set(ref _topIssues, value); }
    public bool HasMoreIssues => (State.Result?.Issues.Count ?? 0) > TopIssues.Count;

    protected override void Rebuild()
    {
        foreach (var tile in new[] { CpuTile, GpuTile, RamTile, DiskTile }) tile.RefreshTitle();

        var result = State.Result;
        if (result is null)
        {
            Headline = Loc.T("dash.headline.none");
            Grade = Loc.T("dash.not_scanned");
            return;
        }

        Score = result.Score;
        var grade = HealthScore.Grade(result.Score);
        Grade = Labels.Grade(grade);
        ScoreSeverity = grade switch
        {
            HealthGrade.Excellent or HealthGrade.Good => Severity.Ok,
            HealthGrade.Fair => Severity.Warning,
            _ => Severity.Critical,
        };
        var critical = result.Count(Severity.Critical);
        var warning = result.Count(Severity.Warning);
        Headline = critical > 0 ? Loc.F("dash.headline.critical", critical)
            : warning > 0 ? Loc.F("dash.headline.warning", warning)
            : Loc.T("dash.headline.good");
        OnPropertyChanged(nameof(CriticalCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(InfoCount));

        var hw = result.Data.Hardware;
        var cpu = hw.Cpus.FirstOrDefault();
        var systemDisk = hw.Disks.FirstOrDefault(d => d.IsSystemDisk);
        SystemItems =
        [
            new(Loc.T("hw.computer"), Format.OrDash($"{hw.Computer.Manufacturer} {hw.Computer.Model}".Trim())),
            new(Loc.T("hw.os"), $"{hw.Os.Caption} {hw.Os.DisplayVersion}".Trim()),
            new(Loc.T("hw.cpu"), cpu?.Name ?? "—"),
            new(Loc.T("hw.ram"), $"{Format.Bytes(hw.Memory.InstalledBytes)} {hw.Memory.Modules.FirstOrDefault()?.Type}".Trim()),
            new(Loc.T("hw.gpu"), Format.OrDash(string.Join(", ", hw.Gpus.Where(g => !g.IsVirtual).Select(g => g.Name)))),
            new(Loc.T("hw.system_disk"), systemDisk is null ? "—" : $"{systemDisk.Model} · {Labels.MediaType(systemDisk)} {Format.MarketingSize(systemDisk.SizeBytes)}"),
            new(Loc.T("hw.uptime"), Format.Duration(result.Data.System.Uptime)),
        ];

        Categories =
        [
            Tile("", "dash.cat.hardware", Pages.Health, result, IssueCategory.Hardware, IssueCategory.Temperature),
            Tile("", "dash.cat.storage", Pages.Health, result, IssueCategory.Storage),
            Tile("", "dash.cat.drivers", Pages.Drivers, result, IssueCategory.Driver),
            Tile("", "dash.cat.compatibility", Pages.Compatibility, result, IssueCategory.Compatibility),
            Tile("", "dash.cat.cleanup", Pages.Cleanup, result, IssueCategory.Junk, IssueCategory.Software),
            Tile("", "dash.cat.security", Pages.Issues, result, IssueCategory.System, IssueCategory.Security),
        ];

        TopIssues = result.Issues.Where(i => i.Severity >= Severity.Warning).Take(5).Select(i => new IssueItem(i)).ToList();
        if (TopIssues.Count == 0) TopIssues = result.Issues.Take(3).Select(i => new IssueItem(i)).ToList();
        OnPropertyChanged(nameof(HasMoreIssues));

        UpdateDiskTile();
    }

    private static CategoryTile Tile(string glyph, string titleKey, string page, ScanResult result, params IssueCategory[] categories)
    {
        var issues = result.Issues.Where(i => categories.Contains(i.Category)).ToList();
        var important = issues.Where(i => i.Severity >= Severity.Warning).ToList();
        var severity = issues.Count == 0 ? Severity.Ok : issues.Max(i => i.Severity);
        var status = important.Count > 0 ? Loc.F("dash.cat.issues", important.Count)
            : issues.Count > 0 ? Loc.F("dash.cat.notes", issues.Count)
            : Loc.T("dash.cat.ok");
        return new CategoryTile(glyph, Loc.T(titleKey), important.Count, severity, status, page);
    }

    protected override void OnLive()
    {
        var live = State.Live;
        var culture = Loc.Instance.Culture;

        var load = State.LiveCpuLoad;
        var cpuTemp = live.CpuTemperature;
        CpuTile.Value = Format.Percent(load, culture);
        CpuTile.Percent = load ?? 0;
        CpuTile.Caption = cpuTemp is null ? Loc.T("dash.no_temp")
            : live.CpuTemperatureEstimated ? Loc.F("dash.temp_estimated", Format.Celsius(cpuTemp, culture))
            : Format.Celsius(cpuTemp, culture);
        CpuTile.Severity = Thresholds.TemperatureSeverity(cpuTemp, Thresholds.CpuTempWarn, Thresholds.CpuTempCritical);
        // Nhiệt độ ước lượng (ACPI) không đủ tin cậy để báo đỏ.
        if (live.CpuTemperatureEstimated && CpuTile.Severity == Severity.Critical) CpuTile.Severity = Severity.Warning;
        if (load is not null) CpuTile.History = _cpuHistory.Push(load.Value);

        var gpuLoad = live.GpuLoad;
        var gpuTemp = live.GpuTemperature;
        GpuTile.Value = gpuLoad is null ? Format.Celsius(gpuTemp, culture) : Format.Percent(gpuLoad, culture);
        GpuTile.Percent = gpuLoad ?? 0;
        GpuTile.Caption = gpuTemp is null ? Loc.T("dash.no_temp") : Format.Celsius(gpuTemp, culture);
        GpuTile.Severity = Thresholds.TemperatureSeverity(gpuTemp, Thresholds.GpuTempWarn, Thresholds.GpuTempCritical);
        if (gpuLoad is not null) GpuTile.History = _gpuHistory.Push(gpuLoad.Value);

        if (State.MemoryTotal > 0)
        {
            var used = State.MemoryTotal - State.MemoryAvailable;
            var percent = 100.0 * used / State.MemoryTotal;
            RamTile.Value = Format.Percent(percent, culture);
            RamTile.Percent = percent;
            RamTile.Caption = $"{Format.Bytes(used, culture)} / {Format.Bytes(State.MemoryTotal, culture)}";
            RamTile.Severity = percent >= Thresholds.RamUsageWarn ? Severity.Warning : Severity.Ok;
            RamTile.History = _ramHistory.Push(percent);
        }

        UpdateDiskTile();
    }

    private void UpdateDiskTile()
    {
        var volume = State.Result?.Data.Hardware.Volumes.FirstOrDefault(v => v.IsSystem);
        var temp = State.Live.MaxStorageTemperature;
        if (volume is null)
        {
            DiskTile.Caption = temp is null ? "" : Format.Celsius(temp);
            return;
        }

        DiskTile.Value = Loc.F("dash.disk_free", Format.Bytes(volume.FreeBytes));
        DiskTile.Percent = volume.UsedPercent;
        DiskTile.Caption = temp is null ? $"{volume.Letter} · {Format.Percent(volume.UsedPercent)}" : $"{volume.Letter} · {Format.Celsius(temp)}";
        DiskTile.Severity = volume.FreePercent < Thresholds.SystemFreePercentCritical ? Severity.Critical
            : volume.FreePercent < Thresholds.SystemFreePercentWarn ? Severity.Warning
            : Severity.Ok;
    }
}
