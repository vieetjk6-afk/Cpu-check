using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Performance;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

/// <summary>Một dòng kết quả đo.</summary>
public sealed class BenchRow(string group, string name, string value, Severity severity, string change, string hint)
{
    public string Group { get; } = group;
    public string Name { get; } = name;
    public string Value { get; } = value;
    public Severity Severity { get; } = severity;
    public string Change { get; } = change;
    public bool HasChange => Change.Length > 0;
    public bool IsFaster => Change.StartsWith('+');
    public string Hint { get; } = hint;
}

public sealed class NoteItem(Severity severity, string text)
{
    public Severity Severity { get; } = severity;
    public string Text { get; } = text;
}

public sealed class DriveOption(string letter, string text, Action<DriveOption> select) : ObservableObject
{
    private bool _isSelected;

    public string Letter { get; } = letter;
    public string Text { get; } = text;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value) select(this);
        }
    }
}

/// <summary>Một mục kiểm tra tối ưu trên giao diện.</summary>
public sealed class OptItem : ObservableObject
{
    private bool _isExpanded;

    public OptItem(OptimizationItem item, RelayCommand fix)
    {
        Item = item;
        Title = item.Title.ToString();
        Detail = item.Detail.ToString();
        Steps = item.Steps?.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((s, i) => $"{i + 1}. {s}").ToList() ?? [];
        StateText = Loc.T("opt.state." + item.State.ToString().ToLowerInvariant());
        ImpactText = Loc.T("opt.impact." + item.Impact.ToString().ToLowerInvariant());
        GroupText = Loc.T("opt.group." + item.Group);
        FixLabel = item.Fix?.Label.ToString() ?? "";
        FixCommand = fix;
        _isExpanded = item.State == OptimizationState.Problem && Steps.Count > 0;
    }

    public OptimizationItem Item { get; }
    public string Title { get; }
    public string Detail { get; }
    public List<string> Steps { get; }
    public bool HasSteps => Steps.Count > 0;
    public string StateText { get; }
    public string ImpactText { get; }
    public string GroupText { get; }
    public bool IsGood => Item.State == OptimizationState.Good;
    public bool HasFix => Item.Fix is not null;
    public string FixLabel { get; }
    public RelayCommand FixCommand { get; }

    public Severity Severity => Item.State switch
    {
        OptimizationState.Problem => Severity.Critical,
        OptimizationState.Suggest => Severity.Warning,
        _ => Severity.Ok,
    };

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value && Steps.Count > 0); }
}

/// <summary>
/// Trang Hiệu năng: tốc độ trực tiếp (CPU, RAM, ổ đĩa, mạng, GPU), đo tốc độ (CPU, RAM, ổ đĩa, Internet) có so sánh lần trước,
/// và kiểm tra tối ưu kèm hướng dẫn + sửa nhanh.
/// </summary>
public sealed class PerformanceViewModel : PageViewModel
{
    private readonly NetworkMonitor _network = new();
    private readonly History _cpuHistory = new();
    private readonly History _ramHistory = new();
    private readonly History _diskHistory = new();
    private readonly History _netHistory = new();
    private readonly History _gpuHistory = new();
    private int _selectedTab;
    private bool _isBenchmarking;
    private double _benchPercent;
    private string _benchStage = "";
    private bool _includeDisk = true;
    private bool _includeInternet;
    private DriveOption? _selectedDrive;
    private CancellationTokenSource? _benchCts;
    private BenchmarkResult? _last;
    private BenchmarkResult? _previous;
    private bool _isChecking;
    private bool _checkedOnce;
    private int _score;
    private string _scoreText = "";
    private List<OptimizationItem> _optItems = [];

    public PerformanceViewModel(MainViewModel main) : base(main)
    {
        CpuTile = new StatTile("\uE950", "perf.live.cpu");
        RamTile = new StatTile("\uE964", "perf.live.ram");
        DiskTile = new StatTile("\uEDA2", "perf.live.disk");
        NetTile = new StatTile("\uE968", "perf.live.net");
        GpuTile = new StatTile("\uE7F4", "perf.live.gpu");
        Tiles = [CpuTile, RamTile, DiskTile, NetTile, GpuTile];

        RunBenchmarkCommand = new AsyncRelayCommand(RunBenchmarkAsync, () => !IsBenchmarking);
        CancelBenchmarkCommand = new RelayCommand(() => _benchCts?.Cancel(), () => IsBenchmarking);
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsChecking);
        ToggleCommand = new RelayCommand(p =>
        {
            if (p is OptItem item) item.IsExpanded = !item.IsExpanded;
        });

        _last = BenchmarkStore.Load();
        LoadDrives();
        BuildResults();
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var tile in Tiles) tile.RefreshTitle();
        };
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!Set(ref _selectedTab, value)) return;
            if (value == 2 && !_checkedOnce && !IsChecking) _ = CheckSafeAsync();
        }
    }

    public override void OnShown()
    {
        if (SelectedTab == 2 && !_checkedOnce && !IsChecking) _ = CheckSafeAsync();
    }

    // ===================== Trực tiếp =====================

    public StatTile CpuTile { get; }
    public StatTile RamTile { get; }
    public StatTile DiskTile { get; }
    public StatTile NetTile { get; }
    public StatTile GpuTile { get; }
    public List<StatTile> Tiles { get; }

    public string SystemSummary
    {
        get
        {
            var hw = State.Result?.Data.Hardware;
            var cpu = hw?.Cpus.FirstOrDefault();
            var parts = new List<string>();
            if (cpu is not null) parts.Add($"{cpu.Name} · {Loc.F("perf.cores", hw!.Cpus.Sum(c => c.Cores), hw.Cpus.Sum(c => c.Threads))}");
            if (State.MemoryTotal > 0) parts.Add(Loc.F("perf.ram_total", Format.Bytes(State.MemoryTotal)));
            parts.Add(Loc.F("perf.uptime", Format.Duration(TimeSpan.FromMilliseconds(Environment.TickCount64))));
            return string.Join("  ·  ", parts);
        }
    }

    protected override void OnLive()
    {
        if (!ReferenceEquals(Main.CurrentPage, this)) return;
        var live = State.Live;
        var culture = Loc.Instance.Culture;

        var cpu = State.LiveCpuLoad;
        CpuTile.Value = Format.Percent(cpu, culture);
        CpuTile.Caption = live.CpuClock is { } clock ? Loc.F("perf.live.clock", Format.Mhz(clock, culture)) : "";
        CpuTile.Severity = cpu >= 90 ? Severity.Warning : Severity.Ok;
        if (cpu is { } c) CpuTile.History = _cpuHistory.Push(c);

        if (State.MemoryTotal > 0)
        {
            var used = State.MemoryTotal - State.MemoryAvailable;
            var percent = 100.0 * used / State.MemoryTotal;
            RamTile.Value = Format.Percent(percent, culture);
            RamTile.Caption = Loc.F("perf.live.ram_detail", Format.Bytes(used, culture), Format.Bytes(State.MemoryTotal, culture));
            RamTile.Severity = percent >= 90 ? Severity.Critical : percent >= 80 ? Severity.Warning : Severity.Ok;
            RamTile.History = _ramHistory.Push(percent);
        }

        DiskTile.Value = Format.Percent(live.DiskActivity, culture);
        DiskTile.Caption = Loc.F("perf.live.rw", Format.Speed(live.DiskReadRate, culture), Format.Speed(live.DiskWriteRate, culture));
        DiskTile.Severity = live.DiskActivity >= 95 ? Severity.Warning : Severity.Ok;
        if (live.DiskActivity is { } disk) DiskTile.History = _diskHistory.Push(disk);

        _network.Sample();
        NetTile.Value = Format.Speed(_network.DownloadBytesPerSecond, culture);
        NetTile.Caption = Loc.F("perf.live.up", Format.Speed(_network.UploadBytesPerSecond, culture));
        if (_network.DownloadBytesPerSecond is { } down) NetTile.History = _netHistory.Push(down);

        GpuTile.Value = Format.Percent(live.GpuLoad, culture);
        GpuTile.Caption = live.GpuTemperature is { } gt ? Format.Celsius(gt, culture) : "";
        if (live.GpuLoad is { } gpu) GpuTile.History = _gpuHistory.Push(gpu);

        OnPropertyChanged(nameof(SystemSummary));
    }

    // ===================== Đo tốc độ =====================

    public ObservableCollection<DriveOption> Drives { get; } = [];
    public ObservableCollection<BenchRow> Results { get; } = [];
    public ObservableCollection<NoteItem> Notes { get; } = [];
    public AsyncRelayCommand RunBenchmarkCommand { get; }
    public RelayCommand CancelBenchmarkCommand { get; }

    public bool IsBenchmarking
    {
        get => _isBenchmarking;
        private set
        {
            if (Set(ref _isBenchmarking, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public double BenchPercent { get => _benchPercent; private set => Set(ref _benchPercent, value); }
    public string BenchStage { get => _benchStage; private set => Set(ref _benchStage, value); }
    public bool IncludeDisk { get => _includeDisk; set => Set(ref _includeDisk, value); }
    public bool IncludeInternet { get => _includeInternet; set => Set(ref _includeInternet, value); }
    public bool HasResults => _last is not null;
    public bool NoResults => _last is null;

    public string LastRunText => _last is { } r
        ? Loc.F("bench.last_run", Format.Date(r.Time, Loc.Instance.Culture) + " " + r.Time.ToString("HH:mm", CultureInfo.InvariantCulture))
          + (_previous is { } p ? "  ·  " + Loc.F("bench.compared_to", Format.Date(p.Time, Loc.Instance.Culture)) : "")
        : "";

    private void LoadDrives()
    {
        Drives.Clear();
        var system = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\")[..1].ToUpperInvariant();
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                var letter = d.Name[..1].ToUpperInvariant();
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? $"{letter}:" : $"{letter}: {d.VolumeLabel}";
                var option = new DriveOption(letter, label, SelectDrive);
                Drives.Add(option);
                if (letter == system) option.IsSelected = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Đọc danh sách ổ lỗi", ex);
        }

        if (_selectedDrive is null && Drives.FirstOrDefault() is { } first) first.IsSelected = true;
    }

    private void SelectDrive(DriveOption option)
    {
        _selectedDrive = option;
        foreach (var other in Drives.Where(d => !ReferenceEquals(d, option))) other.IsSelected = false;
    }

    private async Task RunBenchmarkAsync()
    {
        if (IsBenchmarking) return;
        var message = Loc.T("bench.confirm") + (IncludeInternet ? " " + Loc.T("bench.confirm_internet") : "");
        if (!await Main.ConfirmAsync(Loc.T("bench.run"), message, Loc.T("bench.run"))) return;

        IsBenchmarking = true;
        BenchPercent = 0;
        _benchCts = new CancellationTokenSource();
        Main.ActivityText = Loc.T("bench.running");
        try
        {
            var progress = new Progress<BenchmarkProgress>(p =>
            {
                BenchPercent = p.Percent;
                BenchStage = Loc.T("bench.stage." + p.Stage);
            });
            var options = new BenchmarkOptions
            {
                Drive = IncludeDisk ? _selectedDrive?.Letter : null,
                Internet = IncludeInternet,
            };
            var result = await Benchmark.RunAsync(options, progress, _benchCts.Token);
            _previous = _last;
            _last = result;
            BenchmarkStore.Save(result);
            BuildResults();
            Main.ShowToast(Loc.T("bench.done"));
        }
        catch (OperationCanceledException)
        {
            Main.ShowToast(Loc.T("bench.cancelled"));
        }
        catch (Exception ex)
        {
            Log.Error("Đo tốc độ lỗi", ex);
            Main.ShowToast(Loc.F("error.unexpected", ex.Message), true);
        }
        finally
        {
            IsBenchmarking = false;
            BenchStage = "";
            Main.ActivityText = "";
            _benchCts?.Dispose();
            _benchCts = null;
        }
    }

    private void BuildResults()
    {
        Results.Clear();
        Notes.Clear();
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(NoResults));
        OnPropertyChanged(nameof(LastRunText));
        if (_last is not { } r) return;
        var p = _previous;
        var culture = Loc.Instance.Culture;
        var cores = State.Result?.Data.Hardware.Cpus.Sum(c => c.Cores) ?? Math.Max(1, Environment.ProcessorCount / 2);

        string N(double v, string format = "0") => v.ToString(format, culture);
        string Change(double? now, double? before, bool lowerIsBetter = false) =>
            BenchmarkRating.Change(now, before, lowerIsBetter) is { } c && Math.Abs(c) >= 3 ? (c > 0 ? "+" : "−") + N(Math.Abs(c)) + "%" : "";
        void Add(string group, string name, double? value, string text, Severity severity, string change, string hint = "")
        {
            if (value is null) return;
            Results.Add(new BenchRow(Loc.T(group), Loc.T(name), text, severity, change, hint));
        }

        Add("bench.group.cpu", "bench.cpu_single", r.CpuSingle, Loc.F("bench.points", N(r.CpuSingle ?? 0)), Severity.Info, Change(r.CpuSingle, p?.CpuSingle), Loc.T("bench.hint.cpu"));
        Add("bench.group.cpu", "bench.cpu_multi", r.CpuMulti, Loc.F("bench.points", N(r.CpuMulti ?? 0)),
            r.CpuScaling is { } s ? BenchmarkRating.RateScaling(s, cores) : Severity.Info, Change(r.CpuMulti, p?.CpuMulti),
            r.CpuScaling is { } sc ? Loc.F("bench.hint.scaling", N(sc, "0.0"), r.CpuThreads) : "");
        Add("bench.group.ram", "bench.ram_bandwidth", r.MemoryBandwidthGBps, N(r.MemoryBandwidthGBps ?? 0, "0.0") + " GB/s",
            BenchmarkRating.RateBandwidth(r.MemoryBandwidthGBps ?? 0), Change(r.MemoryBandwidthGBps, p?.MemoryBandwidthGBps));
        Add("bench.group.ram", "bench.ram_latency", r.MemoryLatencyNs, N(r.MemoryLatencyNs ?? 0) + " ns",
            BenchmarkRating.RateLatency(r.MemoryLatencyNs ?? 0), Change(r.MemoryLatencyNs, p?.MemoryLatencyNs, lowerIsBetter: true), Loc.T("bench.hint.latency"));

        var diskGroup = Loc.F("bench.group.disk", r.Drive, string.IsNullOrEmpty(r.DriveModel) ? Loc.T("bench.media." + r.Media.ToString().ToLowerInvariant()) : r.DriveModel);
        var sameDrive = p is not null && p.Drive == r.Drive;
        void Disk(string name, double? value, Severity severity, double? before, bool random)
        {
            if (value is null) return;
            var text = N(value.Value, value < 10 ? "0.0" : "0") + " MB/s" + (random ? "  ·  " + N(BenchmarkResult.Iops(value.Value)) + " IOPS" : "");
            Results.Add(new BenchRow(diskGroup, Loc.T(name), text, severity, sameDrive ? Change(value, before) : "", ""));
        }

        Disk("bench.disk_seq_read", r.DiskSeqReadMBps, BenchmarkRating.RateSeqRead(r.Media, r.DiskSeqReadMBps ?? 0), p?.DiskSeqReadMBps, false);
        Disk("bench.disk_seq_write", r.DiskSeqWriteMBps, BenchmarkRating.RateSeqRead(r.Media, r.DiskSeqWriteMBps ?? 0), p?.DiskSeqWriteMBps, false);
        Disk("bench.disk_rand_read", r.DiskRandReadMBps, BenchmarkRating.RateRandRead(r.Media, r.DiskRandReadMBps ?? 0), p?.DiskRandReadMBps, true);
        Disk("bench.disk_rand_write", r.DiskRandWriteMBps, Severity.Info, p?.DiskRandWriteMBps, true);
        if (r.DiskError is { } diskError) Notes.Add(new NoteItem(Severity.Warning, Loc.T(diskError)));

        Add("bench.group.net", "bench.net_down", r.InternetDownMbps, N(r.InternetDownMbps ?? 0, "0.0") + " Mbps",
            BenchmarkRating.RateDownload(r.InternetDownMbps ?? 0), Change(r.InternetDownMbps, p?.InternetDownMbps));
        Add("bench.group.net", "bench.net_up", r.InternetUpMbps, N(r.InternetUpMbps ?? 0, "0.0") + " Mbps",
            BenchmarkRating.RateDownload((r.InternetUpMbps ?? 0) * 2), Change(r.InternetUpMbps, p?.InternetUpMbps));
        Add("bench.group.net", "bench.net_ping", r.InternetPingMs, N(r.InternetPingMs ?? 0) + " ms",
            BenchmarkRating.RatePing(r.InternetPingMs ?? 0), Change(r.InternetPingMs, p?.InternetPingMs, lowerIsBetter: true));
        if (r.InternetError is { } netError) Notes.Add(new NoteItem(Severity.Warning, Loc.T(netError)));

        var modules = State.Result?.Data.Hardware.Memory.Modules.Count(m => m.CapacityBytes > 0) ?? 0;
        foreach (var note in BenchmarkRating.Notes(r, cores, modules > 0, modules == 1)) Notes.Add(new NoteItem(note.Severity, note.Text.ToString()));
    }

    // ===================== Tối ưu =====================

    public ObservableCollection<OptItem> OptItems { get; } = [];
    public AsyncRelayCommand CheckCommand { get; }
    public RelayCommand ToggleCommand { get; }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (Set(ref _isChecking, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasChecked => _checkedOnce;
    public int Score { get => _score; private set => Set(ref _score, value); }
    public string ScoreText { get => _scoreText; private set => Set(ref _scoreText, value); }

    public Severity ScoreSeverity => Score >= 85 ? Severity.Ok : Score >= 60 ? Severity.Warning : Severity.Critical;

    private async Task CheckSafeAsync()
    {
        try
        {
            await CheckAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Kiểm tra tối ưu lỗi", ex);
            Main.ShowToast(Loc.F("error.unexpected", ex.Message), true);
        }
    }

    public async Task CheckAsync()
    {
        IsChecking = true;
        try
        {
            var data = await OptimizationProbe.CollectAsync(State.Result);
            _optItems = OptimizationAnalyzer.Analyze(data);
            _checkedOnce = true;
            BuildOptItems();
        }
        finally
        {
            IsChecking = false;
        }
    }

    private void BuildOptItems()
    {
        OptItems.Clear();
        foreach (var item in _optItems)
        {
            OptItems.Add(new OptItem(item, new RelayCommand(() => _ = ApplyFixAsync(item))));
        }

        Score = OptimizationAnalyzer.Score(_optItems);
        var pending = _optItems.Count(i => i.State != OptimizationState.Good);
        ScoreText = pending == 0 ? Loc.T("opt.summary.all_good") : Loc.F("opt.summary.pending", pending, _optItems.Count);
        OnPropertyChanged(nameof(ScoreSeverity));
        OnPropertyChanged(nameof(HasChecked));
    }

    private async Task ApplyFixAsync(OptimizationItem item)
    {
        if (item.Fix is not { } fix) return;
        await Main.RunFixAsync(fix);
        if (fix.Kind == FixKind.RunTool) await CheckSafeAsync();
    }

    protected override void Rebuild()
    {
        BuildResults();
        if (_checkedOnce) BuildOptItems();
        OnPropertyChanged(nameof(SystemSummary));
    }
}
