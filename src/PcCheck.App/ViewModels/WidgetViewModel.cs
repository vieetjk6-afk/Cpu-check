using System.Collections.ObjectModel;
using System.Globalization;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Analysis;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

/// <summary>Một thông số hiển thị trên widget.</summary>
public sealed class WidgetMetric : ObservableObject
{
    private readonly History _history = new(40);
    private string _value = "—";
    private string _detail = "";
    private double _percent;
    private Severity _severity;
    private IReadOnlyList<double>? _historyValues;
    private IReadOnlyList<WidgetLine> _lines = [];

    public WidgetMetric(string key, string glyph, bool hasBar, bool hasGraph)
    {
        Key = key;
        Glyph = glyph;
        HasBar = hasBar;
        HasGraph = hasGraph;
    }

    public string Key { get; }
    public string Glyph { get; }
    public bool HasBar { get; }
    public bool HasGraph { get; }
    public string Label => Loc.T("widget.metric." + Key);
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public double Percent { get => _percent; set => Set(ref _percent, value); }
    public Severity Severity { get => _severity; set => Set(ref _severity, value); }
    public IReadOnlyList<double>? History { get => _historyValues; private set => Set(ref _historyValues, value); }

    /// <summary>Danh sách con (vd nhiệt độ từng thiết bị). Rỗng với các thông số thường.</summary>
    public IReadOnlyList<WidgetLine> Lines { get => _lines; set => Set(ref _lines, value); }

    public void Push(double value) => History = _history.Push(value);

    public void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

/// <summary>Một dòng con trong thông số widget (tên thiết bị + giá trị).</summary>
public sealed record WidgetLine(string Glyph, string Name, string Value, Severity Severity);

/// <summary>Lựa chọn thông số trong trang cài đặt widget.</summary>
public sealed class MetricOption : ObservableObject
{
    private readonly WidgetViewModel _owner;
    private bool _isChecked;

    public MetricOption(string key, string glyph, bool isChecked, WidgetViewModel owner)
    {
        Key = key;
        Glyph = glyph;
        _isChecked = isChecked;
        _owner = owner;
    }

    public string Key { get; }
    public string Glyph { get; }
    public string Label => Loc.T("widget.metric." + Key);
    public string Description => Loc.T("widget.metric_desc." + Key);

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value)) _owner.OnMetricsChanged();
        }
    }

    public void RefreshLabel()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
    }
}

/// <summary>
/// Widget nổi trên màn hình: vừa là trang cài đặt "Widget" trong ứng dụng, vừa là DataContext của cửa sổ widget.
/// </summary>
public sealed class WidgetViewModel : PageViewModel
{
    /// <summary>Tất cả thông số có thể chọn (theo thứ tự hiển thị): khóa, biểu tượng, có thanh %, có biểu đồ.</summary>
    public static readonly IReadOnlyList<(string Key, string Glyph, bool Bar, bool Graph)> Catalog =
    [
        ("cpu", "", true, true),
        ("cpu_temp", "", true, false),
        ("gpu", "", true, true),
        ("gpu_temp", "", true, false),
        ("vram", "", true, false),
        ("ram", "", true, true),
        ("net", "", false, true),
        ("disk", "", true, true),
        ("disk_temp", "", true, false),
        ("devices", "", false, false),
        ("fans", "", false, false),
        ("battery", "", true, false),
        ("uptime", "", false, false),
        ("clock", "", false, false),
    ];

    private readonly NetworkMonitor _network = new();
    private readonly Dictionary<string, WidgetMetric> _all = new(StringComparer.Ordinal);
    private bool _isVisible;
    private bool _autoStart;
    private bool _autoStartBusy;

    public WidgetViewModel(MainViewModel main) : base(main)
    {
        System.Diagnostics.Debug.Assert(Catalog.Select(c => c.Key).SequenceEqual(WidgetSettings.AllMetrics), "Catalog phải khớp WidgetSettings.AllMetrics");
        foreach (var (key, glyph, bar, graph) in Catalog)
        {
            _all[key] = new WidgetMetric(key, glyph, bar, graph);
            Options.Add(new MetricOption(key, glyph, Store.Metrics.Contains(key), this));
        }

        RebuildVisible();
        ToggleCommand = new RelayCommand(() => IsVisible = !IsVisible);
        ToggleCollapsedCommand = new RelayCommand(() => Collapsed = !Collapsed);
        HideCommand = new RelayCommand(() => IsVisible = false);
        ResetPositionCommand = new RelayCommand(() =>
        {
            Store.Left = double.NaN;
            Store.Top = double.NaN;
            Save();
            ResetPositionRequested?.Invoke(this, EventArgs.Empty);
        });
        ResetMetricsCommand = new RelayCommand(() =>
        {
            foreach (var option in Options) option.IsChecked = WidgetSettings.DefaultMetrics.Contains(option.Key);
        });
        OpenMainCommand = new RelayCommand(() => OpenMainRequested?.Invoke(this, Pages.Dashboard));
        OpenSettingsCommand = new RelayCommand(() => OpenMainRequested?.Invoke(this, Pages.Widget));
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        _ = LoadAutoStartAsync();
    }

    /// <summary>Hiện cửa sổ chính (tham số = trang cần mở).</summary>
    public event EventHandler<string>? OpenMainRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler? ResetPositionRequested;

    /// <summary>Widget hiện/ẩn – App lắng nghe để mở/đóng cửa sổ widget.</summary>
    public event EventHandler? VisibilityChanged;

    public WidgetSettings Store => State.Settings.Widget;

    public ObservableCollection<WidgetMetric> Metrics { get; } = [];
    public ObservableCollection<MetricOption> Options { get; } = [];

    public RelayCommand ToggleCommand { get; }
    public RelayCommand ToggleCollapsedCommand { get; }
    public RelayCommand HideCommand { get; }
    public RelayCommand ResetPositionCommand { get; }
    public RelayCommand ResetMetricsCommand { get; }
    public RelayCommand OpenMainCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand ExitCommand { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (!Set(ref _isVisible, value)) return;
            Store.Visible = value;
            Save();
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool AlwaysOnTop
    {
        get => Store.AlwaysOnTop;
        set => Update(() => Store.AlwaysOnTop = value);
    }

    public bool Locked
    {
        get => Store.Locked;
        set => Update(() => Store.Locked = value);
    }

    public bool ShowGraphs
    {
        get => Store.ShowGraphs;
        set => Update(() => Store.ShowGraphs = value);
    }

    /// <summary>Thu gọn widget: chỉ còn lưới biểu tượng + giá trị.</summary>
    public bool Collapsed
    {
        get => Store.Collapsed;
        set
        {
            Update(() => Store.Collapsed = value);
            OnLayoutChanged();
        }
    }

    /// <summary>Bố cục dọc đầy đủ.</summary>
    public bool ShowFull => !IsHorizontal && !Collapsed;

    /// <summary>Bố cục dọc thu gọn.</summary>
    public bool ShowCompact => !IsHorizontal && Collapsed;

    private void OnLayoutChanged()
    {
        OnPropertyChanged(nameof(ShowFull));
        OnPropertyChanged(nameof(ShowCompact));
    }

    /// <summary>Độ đục dạng chuỗi phần trăm để bind với nút phân đoạn: "100", "90", "75", "60".</summary>
    public string OpacityPercent
    {
        get => ((int)Math.Round(Store.Opacity * 100)).ToString(CultureInfo.InvariantCulture);
        set
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)) return;
            Update(() => Store.Opacity = Math.Clamp(percent, 40, 100) / 100.0);
            OnPropertyChanged(nameof(WindowOpacity));
        }
    }

    public double WindowOpacity => Math.Clamp(Store.Opacity, 0.4, 1);

    /// <summary>"small", "medium", "large".</summary>
    public string Size
    {
        get => Store.Scale switch { < 0.95 => "small", > 1.1 => "large", _ => "medium" };
        set
        {
            Update(() => Store.Scale = value switch { "small" => 0.85, "large" => 1.2, _ => 1.0 });
            OnPropertyChanged(nameof(Scale));
        }
    }

    public double Scale => Store.Scale is > 0.5 and < 2 ? Store.Scale : 1;

    public string Layout
    {
        get => Store.Layout == "horizontal" ? "horizontal" : "vertical";
        set
        {
            Update(() => Store.Layout = value == "horizontal" ? "horizontal" : "vertical");
            OnPropertyChanged(nameof(Layout));
            OnPropertyChanged(nameof(IsHorizontal));
            OnLayoutChanged();
        }
    }

    public bool IsHorizontal
    {
        get => Layout == "horizontal";
        set => Layout = value ? "horizontal" : "vertical";
    }

    public string Interval
    {
        get => State.Settings.LiveIntervalSeconds.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)) return;
            State.Settings.LiveIntervalSeconds = Math.Clamp(seconds, 1, 10);
            Save();
            OnPropertyChanged();
            Main.UpdateLiveTimer();
        }
    }

    public bool AutoStartBusy { get => _autoStartBusy; private set => Set(ref _autoStartBusy, value); }

    /// <summary>Khởi động widget cùng Windows (Task Scheduler).</summary>
    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (_autoStart == value || AutoStartBusy) return;
            _ = SetAutoStartAsync(value);
        }
    }

    public bool HasMetrics => Metrics.Count > 0;

    internal void OnMetricsChanged()
    {
        Store.Metrics = Options.Where(o => o.IsChecked).Select(o => o.Key).ToList();
        Save();
        RebuildVisible();
        OnLive();
    }

    private void RebuildVisible()
    {
        Metrics.Clear();
        foreach (var (key, _, _, _) in Catalog)
        {
            if (Store.Metrics.Contains(key)) Metrics.Add(_all[key]);
        }

        OnPropertyChanged(nameof(HasMetrics));
    }

    private void Update(Action change, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        change();
        Save();
        OnPropertyChanged(name);
    }

    private void Save() => State.Settings.Save();

    protected override void Rebuild()
    {
        foreach (var metric in _all.Values) metric.RefreshLabel();
        foreach (var option in Options) option.RefreshLabel();
        OnLive();
    }

    /// <summary>Vị trí cửa sổ widget sau khi kéo thả.</summary>
    public void SavePosition(double left, double top)
    {
        Store.Left = Math.Round(left);
        Store.Top = Math.Round(top);
        Save();
    }

    protected override void OnLive()
    {
        if (!IsVisible && !ReferenceEquals(Main.CurrentPage, this)) return;

        var live = State.Live;
        var c = Loc.Instance.Culture;
        if (Metrics.Any(m => m.Key == "net")) _network.Sample();

        foreach (var metric in Metrics)
        {
            try
            {
                UpdateMetric(metric, live, c);
            }
            catch (Exception ex)
            {
                Log.Warn($"Cập nhật widget {metric.Key} lỗi", ex);
            }
        }
    }

    private void UpdateMetric(WidgetMetric m, SensorSnapshot live, CultureInfo c)
    {
        switch (m.Key)
        {
            case "cpu":
            {
                var load = State.LiveCpuLoad;
                m.Value = Format.Percent(load, c);
                m.Percent = load ?? 0;
                m.Detail = live.CpuClock is { } clock ? Format.Mhz(clock, c) : "";
                m.Severity = load >= 90 ? Severity.Warning : Severity.Ok;
                if (load is not null) m.Push(load.Value);
                break;
            }

            case "cpu_temp":
            {
                var t = live.CpuTemperature;
                SetTemperature(m, t, Thresholds.CpuTempWarn, Thresholds.CpuTempCritical, c);
                if (live.CpuTemperatureEstimated && m.Severity == Severity.Critical) m.Severity = Severity.Warning;
                m.Detail = live.CpuTemperatureEstimated ? Loc.T("health.estimated")
                    : live.CpuPower is { } w ? w.ToString("0", c) + " W"
                    : t is null ? Loc.T("widget.no_sensor") : "";
                break;
            }

            case "gpu":
            {
                var load = live.GpuLoad;
                m.Value = Format.Percent(load, c);
                m.Percent = load ?? 0;
                m.Detail = live.GpuPower is { } w ? w.ToString("0", c) + " W" : "";
                m.Severity = Severity.Ok;
                if (load is not null) m.Push(load.Value);
                break;
            }

            case "gpu_temp":
            {
                SetTemperature(m, live.GpuTemperature, Thresholds.GpuTempWarn, Thresholds.GpuTempCritical, c);
                m.Detail = live.GpuHotSpot is { } hot ? Loc.F("widget.hotspot", hot.ToString("0", c)) : "";
                break;
            }

            case "vram":
            {
                var used = live.GpuMemoryUsed;
                var total = live.GpuMemoryTotal;
                m.Value = used is null ? "—" : Format.Bytes((long)(used.Value * 1024 * 1024), c);
                m.Percent = used is not null && total > 0 ? 100.0 * used.Value / total.Value : 0;
                m.Detail = total > 0 ? "/ " + Format.Bytes((long)(total.Value * 1024 * 1024), c) : "";
                m.Severity = m.Percent >= 95 ? Severity.Warning : Severity.Ok;
                break;
            }

            case "ram":
            {
                if (State.MemoryTotal <= 0) break;
                var used = State.MemoryTotal - State.MemoryAvailable;
                var percent = 100.0 * used / State.MemoryTotal;
                m.Value = Format.Percent(percent, c);
                m.Percent = percent;
                m.Detail = $"{Format.Bytes(used, c)} / {Format.Bytes(State.MemoryTotal, c)}";
                m.Severity = percent >= Thresholds.RamUsageWarn ? Severity.Warning : Severity.Ok;
                m.Push(percent);
                break;
            }

            case "net":
            {
                var down = _network.DownloadBytesPerSecond;
                var up = _network.UploadBytesPerSecond;
                m.Value = "↓ " + Format.Speed(down, c);
                m.Detail = "↑ " + Format.Speed(up, c);
                m.Severity = Severity.Ok;
                if (down is not null) m.Push(down.Value);
                break;
            }

            case "disk":
            {
                var activity = live.DiskActivity;
                m.Value = Format.Percent(activity, c);
                m.Percent = activity ?? 0;
                var read = live.DiskReadRate;
                var write = live.DiskWriteRate;
                m.Detail = read is null && write is null ? "" : $"R {Format.Speed(read, c)} · W {Format.Speed(write, c)}";
                m.Severity = Severity.Ok;
                if (activity is not null) m.Push(activity.Value);
                break;
            }

            case "disk_temp":
                SetTemperature(m, live.MaxStorageTemperature, Thresholds.SsdTempWarn, Thresholds.SsdTempCritical, c);
                m.Detail = "";
                break;

            case "devices":
            {
                var devices = live.DeviceTemperatures();
                var lines = devices.Select(d =>
                {
                    var (warn, critical) = TemperatureLimits(d.Kind);
                    var severity = Thresholds.TemperatureSeverity(d.Celsius, warn, critical);
                    if (d.Estimated && severity == Severity.Critical) severity = Severity.Warning;
                    var value = Format.Celsius(d.Celsius, c) + (d.Estimated ? "*" : "");
                    return new WidgetLine(DeviceGlyph(d.Kind), ShortName(d.Name), value, severity);
                }).ToList();
                m.Lines = lines;
                m.Value = devices.Count == 0 ? "—" : Format.Celsius(devices.Max(d => d.Celsius), c);
                m.Severity = lines.Count == 0 ? Severity.Ok : lines.Max(l => l.Severity);
                m.Detail = devices.Count == 0 ? Loc.T("widget.no_sensor") : devices.Any(d => d.Estimated) ? Loc.T("widget.estimated_note") : "";
                break;
            }

            case "fans":
                m.Value = live.MaxFanRpm is { } rpm ? rpm.ToString("0", c) + " RPM" : "—";
                m.Detail = Loc.F("health.fans_count", live.FanCount);
                m.Severity = Severity.Ok;
                break;

            case "battery":
            {
                var battery = State.Result?.Data.Hardware.Battery;
                var level = live.BatteryLevel ?? battery?.ChargePercent;
                m.Value = Format.Percent(level, c);
                m.Percent = level ?? 0;
                m.Detail = level is null ? Loc.T("widget.no_battery") : battery?.IsCharging == true ? Loc.T("battery.charging") : "";
                m.Severity = level is < 15 ? Severity.Warning : Severity.Ok;
                break;
            }

            case "uptime":
                m.Value = Format.Duration(TimeSpan.FromMilliseconds(Environment.TickCount64));
                m.Detail = "";
                m.Severity = Severity.Ok;
                break;

            case "clock":
                m.Value = DateTime.Now.ToString("t", c);
                m.Detail = DateTime.Now.ToString("ddd, d MMM", c);
                m.Severity = Severity.Ok;
                break;
        }
    }

    private static (double Warn, double Critical) TemperatureLimits(SensorHardwareKind kind) => kind switch
    {
        SensorHardwareKind.Cpu => (Thresholds.CpuTempWarn, Thresholds.CpuTempCritical),
        SensorHardwareKind.Gpu => (Thresholds.GpuTempWarn, Thresholds.GpuTempCritical),
        SensorHardwareKind.Storage => (Thresholds.SsdTempWarn, Thresholds.SsdTempCritical),
        _ => (80, 95),
    };

    private static string DeviceGlyph(SensorHardwareKind kind) => kind switch
    {
        SensorHardwareKind.Cpu => "\uE950",
        SensorHardwareKind.Gpu => "\uE7F4",
        SensorHardwareKind.Storage => "\uEDA2",
        SensorHardwareKind.Battery => "\uE83F",
        _ => "\uE964",
    };

    /// <summary>Rút gọn tên thiết bị cho vừa widget: bỏ hãng / chữ thừa, tối đa ~22 ký tự.</summary>
    internal static string ShortName(string name)
    {
        var text = name;
        foreach (var noise in new[] { "(R)", "(TM)", "®", "™", "Intel ", "AMD ", "NVIDIA ", "GeForce ", "Radeon ", "Samsung SSD ", "CPU ", "Processor" })
            text = text.Replace(noise, " ", StringComparison.OrdinalIgnoreCase);
        var at = text.IndexOf('@');
        if (at > 0) text = text[..at];
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0) text = name;
        return text.Length <= 22 ? text : text[..21].TrimEnd() + "…";
    }

    private static void SetTemperature(WidgetMetric m, float? value, double warn, double critical, CultureInfo c)
    {
        m.Value = Format.Celsius(value, c);
        m.Percent = value ?? 0;
        m.Severity = Thresholds.TemperatureSeverity(value, warn, critical);
    }

    private async Task LoadAutoStartAsync()
    {
        try
        {
            _autoStart = await Task.Run(Core.Util.AutoStart.IsEnabled);
            OnPropertyChanged(nameof(AutoStart));
        }
        catch (Exception ex)
        {
            Log.Warn("Kiểm tra khởi động cùng Windows lỗi", ex);
        }
    }

    private async Task SetAutoStartAsync(bool enable)
    {
        AutoStartBusy = true;
        try
        {
            var exe = Environment.ProcessPath ?? "";
            var ok = await Task.Run(() => enable ? Core.Util.AutoStart.Enable(exe) : Core.Util.AutoStart.Disable());
            if (ok) _autoStart = enable;
            Main.ShowToast(ok ? Loc.T(enable ? "widget.autostart_on" : "widget.autostart_off") : Loc.T("widget.autostart_failed"), !ok);
        }
        finally
        {
            AutoStartBusy = false;
            OnPropertyChanged(nameof(AutoStart));
        }
    }
}
