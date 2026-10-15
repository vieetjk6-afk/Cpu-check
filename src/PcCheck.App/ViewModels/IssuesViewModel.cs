using PcCheck.App.Infrastructure;
using PcCheck.Core.Analysis;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;

namespace PcCheck.App.ViewModels;

public sealed class ToolItem(SystemTool tool, AsyncRelayCommand command)
{
    public string Key { get; } = tool.Key;
    public string Name { get; } = tool.Name.ToString();
    public string Description { get; } = tool.Description.ToString();
    public string Glyph { get; } = tool.Glyph;
    public AsyncRelayCommand Command { get; } = command;
}

public sealed class EventStat(string glyph, string title, int count, Severity severity, string detail)
{
    public string Glyph { get; } = glyph;
    public string Title { get; } = title;
    public int Count { get; } = count;
    public Severity Severity { get; } = severity;
    public string Detail { get; } = detail;
}

/// <summary>Trang lỗi & khắc phục: danh sách vấn đề, thống kê sự kiện lỗi, công cụ sửa nhanh.</summary>
public sealed class IssuesViewModel : PageViewModel
{
    private IReadOnlyList<IssueItem> _all = [];
    private IReadOnlyList<IssueItem> _issues = [];
    private IReadOnlyList<ToolItem> _tools = [];
    private IReadOnlyList<EventStat> _events = [];
    private string _filter = "all";

    public IssuesViewModel(MainViewModel main) : base(main)
    {
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true), () => Issues.Any(i => i.HasGuide));
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false), () => Issues.Any(i => i.HasGuide));
        Rebuild();
    }

    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }

    private void SetAllExpanded(bool expanded)
    {
        foreach (var issue in Issues) issue.IsExpanded = expanded;
    }

    public IReadOnlyList<IssueItem> Issues { get => _issues; private set => Set(ref _issues, value); }
    public IReadOnlyList<ToolItem> Tools { get => _tools; private set => Set(ref _tools, value); }
    public IReadOnlyList<EventStat> Events { get => _events; private set => Set(ref _events, value); }
    public int AllCount => _all.Count;
    public int CriticalCount => _all.Count(i => i.Severity == Severity.Critical);
    public int WarningCount => _all.Count(i => i.Severity == Severity.Warning);
    public int InfoCount => _all.Count(i => i.Severity == Severity.Info);
    public bool NoIssues => HasResult && _all.Count == 0;
    public string EventsTitle => Loc.F("issues.events_title", State.Result?.Data.Events.Days ?? 30);

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value)) ApplyFilter();
        }
    }

    protected override void Rebuild()
    {
        Tools = SystemTools.QuickTools
            .Select(t => new ToolItem(t, new AsyncRelayCommand(() => Main.RunToolAsync(t.Key, t.RequiresConfirmation))))
            .ToList();

        _all = State.Result?.Issues.Select(i => new IssueItem(i)).ToList() ?? [];
        ApplyFilter();
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(CriticalCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(InfoCount));
        OnPropertyChanged(nameof(NoIssues));
        OnPropertyChanged(nameof(EventsTitle));
        BuildEvents();
    }

    private void ApplyFilter()
    {
        Issues = _filter switch
        {
            "critical" => _all.Where(i => i.Severity == Severity.Critical).ToList(),
            "warning" => _all.Where(i => i.Severity == Severity.Warning).ToList(),
            "info" => _all.Where(i => i.Severity == Severity.Info).ToList(),
            _ => _all,
        };
    }

    private void BuildEvents()
    {
        if (State.Result is not { } result)
        {
            Events = [];
            return;
        }

        var ev = result.Data.Events;
        var bsod = Math.Max(ev.Count(EventKind.BlueScreen), ev.MinidumpCount);
        var lastBsod = ev.Of(EventKind.BlueScreen).FirstOrDefault();
        var bug = BugChecks.Lookup(lastBsod?.Detail);
        var powerLoss = ev.Of(EventKind.UnexpectedShutdown).Count(e => e.Detail is null or "0");
        var whea = ev.Count(EventKind.HardwareError) + ev.Count(EventKind.HardwareErrorFatal);
        var disk = ev.Count(EventKind.DiskError) + ev.Count(EventKind.StorageReset) + ev.Count(EventKind.FileSystemError);
        var tdr = ev.Count(EventKind.GpuDriverCrash);
        var crashes = ev.Count(EventKind.AppCrash) + ev.Count(EventKind.AppHang);
        var topApp = ev.Of(EventKind.AppCrash).Where(e => e.Detail is not null)
            .GroupBy(e => e.Detail!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault();

        Events =
        [
            new("", Loc.T("events.bsod"), bsod, bsod >= 3 ? Severity.Critical : bsod > 0 ? Severity.Warning : Severity.Ok,
                lastBsod is null ? Loc.T("events.none") : $"{lastBsod.Detail} {bug?.Name} · {lastBsod.Time.ToString("g", Loc.Instance.Culture)}"),
            new("", Loc.T("events.power_loss"), powerLoss, powerLoss >= 5 ? Severity.Critical : powerLoss >= 2 ? Severity.Warning : Severity.Ok,
                powerLoss > 0 ? Loc.T("events.power_loss_hint") : Loc.T("events.none")),
            new("", Loc.T("events.whea"), whea, ev.Count(EventKind.HardwareErrorFatal) > 0 ? Severity.Critical : whea >= 10 ? Severity.Warning : Severity.Ok,
                whea > 0 ? Loc.T("events.whea_hint") : Loc.T("events.none")),
            new("", Loc.T("events.disk"), disk, disk >= 5 ? Severity.Critical : disk > 0 ? Severity.Warning : Severity.Ok,
                disk > 0 ? Loc.T("events.disk_hint") : Loc.T("events.none")),
            new("", Loc.T("events.tdr"), tdr, tdr >= 5 ? Severity.Critical : tdr >= 2 ? Severity.Warning : Severity.Ok,
                tdr > 0 ? Loc.T("events.tdr_hint") : Loc.T("events.none")),
            new("", Loc.T("events.app_crash"), crashes, crashes >= 20 ? Severity.Warning : crashes > 0 ? Severity.Info : Severity.Ok,
                topApp is null ? Loc.T("events.none") : Loc.F("events.top_app", topApp.Key, topApp.Count())),
        ];
    }
}
