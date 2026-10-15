using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Reporting;
using PcCheck.Core.Scanning;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

public sealed class NavItem : ObservableObject
{
    private int _badge;
    private Severity _badgeSeverity;

    public NavItem(string key, string titleKey, string glyph, PageViewModel page)
    {
        Key = key;
        TitleKey = titleKey;
        Glyph = glyph;
        Page = page;
    }

    public string Key { get; }
    public string TitleKey { get; }
    public string Glyph { get; }
    public PageViewModel Page { get; }

    public string Title => Loc.T(TitleKey);

    public int Badge { get => _badge; set => Set(ref _badge, value); }
    public Severity BadgeSeverity { get => _badgeSeverity; set => Set(ref _badgeSeverity, value); }

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

/// <summary>ViewModel cửa sổ chính: điều hướng, quét, hộp thoại xác nhận, thông báo, giám sát trực tiếp.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly DispatcherTimer _liveTimer;
    private readonly DispatcherTimer _toastTimer;
    private readonly ScanEngine _engine;
    private NavItem? _selectedNav;
    private bool _isScanning;
    private double _scanPercent;
    private string _scanStep = "";
    private bool _liveBusy;
    private bool _isActive = true;
    private TaskCompletionSource<bool>? _dialogTcs;

    public MainViewModel(AppState state)
    {
        State = state;
        _engine = new ScanEngine(state.Sensors);

        Dashboard = new DashboardViewModel(this);
        Hardware = new HardwareViewModel(this);
        Health = new HealthViewModel(this);
        Compatibility = new CompatibilityViewModel(this);
        Drivers = new DriversViewModel(this);
        Cleanup = new CleanupViewModel(this);
        Issues = new IssuesViewModel(this);
        Settings = new SettingsViewModel(this);

        NavItems =
        [
            new(Pages.Dashboard, "nav.dashboard", "", Dashboard),
            new(Pages.Hardware, "nav.hardware", "", Hardware),
            new(Pages.Health, "nav.health", "", Health),
            new(Pages.Compatibility, "nav.compatibility", "", Compatibility),
            new(Pages.Drivers, "nav.drivers", "", Drivers),
            new(Pages.Cleanup, "nav.cleanup", "", Cleanup),
            new(Pages.Issues, "nav.issues", "", Issues),
        ];
        SettingsNav = new NavItem(Pages.Settings, "nav.settings", "", Settings);
        _selectedNav = NavItems[0];

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? Pages.Dashboard));
        ExecuteFixCommand = new AsyncRelayCommand(ExecuteFixAsync);
        ToggleLanguageCommand = new RelayCommand(() => Settings.Language = Loc.Instance.Language == Loc.Vietnamese ? Loc.English : Loc.Vietnamese);
        ToggleThemeCommand = new RelayCommand(() => Settings.Theme = ThemeManager.IsDark ? ThemeManager.Light : ThemeManager.Dark);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync, () => State.Result is not null);
        DialogConfirmCommand = new RelayCommand(() => CloseDialog(true));
        DialogCancelCommand = new RelayCommand(() => CloseDialog(false));
        CloseToastCommand = new RelayCommand(() => IsToastVisible = false);

        State.ResultChanged += (_, _) => UpdateBadges();
        Loc.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var nav in NavItems) nav.RefreshTitle();
            SettingsNav.RefreshTitle();
            OnPropertyChanged(nameof(LastScanText));
        };

        _liveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _liveTimer.Tick += async (_, _) => await TickLiveAsync();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };
    }

    public AppState State { get; }

    public DashboardViewModel Dashboard { get; }
    public HardwareViewModel Hardware { get; }
    public HealthViewModel Health { get; }
    public CompatibilityViewModel Compatibility { get; }
    public DriversViewModel Drivers { get; }
    public CleanupViewModel Cleanup { get; }
    public IssuesViewModel Issues { get; }
    public SettingsViewModel Settings { get; }

    public ObservableCollection<NavItem> NavItems { get; }
    public NavItem SettingsNav { get; }

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is null || !Set(ref _selectedNav, value)) return;
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(IsSettingsSelected));
        }
    }

    public bool IsSettingsSelected
    {
        get => _selectedNav == SettingsNav;
        set
        {
            if (value) SelectedNav = SettingsNav;
        }
    }

    public PageViewModel CurrentPage => _selectedNav?.Page ?? Dashboard;

    public bool IsAdmin { get; } = Elevation.IsAdministrator();

    public string AppVersion { get; } = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    // ---------- Quét ----------
    public AsyncRelayCommand ScanCommand { get; }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (Set(ref _isScanning, value)) OnPropertyChanged(nameof(LastScanText));
        }
    }

    public double ScanPercent { get => _scanPercent; private set => Set(ref _scanPercent, value); }

    public string ScanStep { get => _scanStep; private set => Set(ref _scanStep, value); }

    public string LastScanText =>
        IsScanning ? Loc.T("scan.running")
        : State.Result is { } r ? Loc.F("scan.last", r.Time, r.Duration.TotalSeconds)
        : Loc.T("scan.never");

    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        ScanPercent = 0;
        ScanStep = Loc.T("scan.step.start");
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanPercent = p.Total == 0 ? 0 : 100.0 * p.Completed / p.Total;
                ScanStep = p.Completed >= p.Total ? Loc.T("scan.step.analyze") : Loc.F("scan.step.done", Loc.T(p.StepKey));
            });
            var result = await _engine.RunAsync(progress);
            State.SetResult(result);
            ShowToast(Loc.F("scan.finished", result.Issues.Count, result.Score));
        }
        finally
        {
            IsScanning = false;
            ScanPercent = 100;
        }
    }

    // ---------- Điều hướng & khắc phục ----------
    public RelayCommand NavigateCommand { get; }
    public AsyncRelayCommand ExecuteFixCommand { get; }

    public void Navigate(string page)
    {
        switch (page)
        {
            case Pages.Software:
                Cleanup.SelectedTab = 1;
                page = Pages.Cleanup;
                break;
            case Pages.Startup:
                Cleanup.SelectedTab = 2;
                page = Pages.Cleanup;
                break;
            case Pages.Cleanup:
                Cleanup.SelectedTab = 0;
                break;
        }

        SelectedNav = page == Pages.Settings ? SettingsNav : NavItems.FirstOrDefault(n => n.Key == page) ?? NavItems[0];
    }

    private async Task ExecuteFixAsync(object? parameter)
    {
        var action = parameter switch
        {
            FixItem item => item.Action,
            FixAction fix => fix,
            _ => null,
        };
        if (action is null) return;

        if (action.Kind == FixKind.Navigate)
        {
            Navigate(action.Arg ?? Pages.Dashboard);
            return;
        }

        if (action.RequiresConfirmation)
        {
            var message = action.ConfirmMessage?.ToString() ?? Loc.T("dialog.confirm_generic");
            if (!await ConfirmAsync(action.Label.ToString(), message, Loc.T("dialog.run"))) return;
        }

        var outcome = await FixService.ExecuteAsync(action);
        ShowToast(outcome.Message.ToString(), !outcome.Success);
    }

    public async Task RunToolAsync(string toolKey, bool confirm)
    {
        var action = FixCatalog.Tool(toolKey, $"tool.{toolKey}.name", confirm);
        await ExecuteFixAsync(action);
    }

    // ---------- Ngôn ngữ / giao diện / báo cáo ----------
    public RelayCommand ToggleLanguageCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public AsyncRelayCommand ExportReportCommand { get; }

    public async Task ExportReportAsync()
    {
        if (State.Result is not { } result) return;
        var dialog = new SaveFileDialog
        {
            Title = Loc.T("report.save_title"),
            Filter = "HTML (*.html)|*.html",
            FileName = $"PCCheck-{result.Data.Hardware.Os.MachineName}-{result.Time:yyyyMMdd-HHmm}.html",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog() != true) return;

        var html = await Task.Run(() => ReportBuilder.BuildHtml(result, AppVersion));
        await File.WriteAllTextAsync(dialog.FileName, html);
        ShowToast(Loc.F("report.saved", Path.GetFileName(dialog.FileName)));
        FixService.OpenUri(dialog.FileName);
    }

    // ---------- Hộp thoại xác nhận ----------
    private bool _isDialogOpen;
    private string _dialogTitle = "";
    private string _dialogMessage = "";
    private string _dialogConfirmText = "";
    private bool _dialogIsDanger;

    public bool IsDialogOpen { get => _isDialogOpen; private set => Set(ref _isDialogOpen, value); }
    public string DialogTitle { get => _dialogTitle; private set => Set(ref _dialogTitle, value); }
    public string DialogMessage { get => _dialogMessage; private set => Set(ref _dialogMessage, value); }
    public string DialogConfirmText { get => _dialogConfirmText; private set => Set(ref _dialogConfirmText, value); }
    public bool DialogIsDanger { get => _dialogIsDanger; private set => Set(ref _dialogIsDanger, value); }
    public RelayCommand DialogConfirmCommand { get; }
    public RelayCommand DialogCancelCommand { get; }

    public Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, bool danger = false)
    {
        _dialogTcs?.TrySetResult(false);
        _dialogTcs = new TaskCompletionSource<bool>();
        DialogTitle = title;
        DialogMessage = message;
        DialogConfirmText = confirmText ?? Loc.T("dialog.ok");
        DialogIsDanger = danger;
        IsDialogOpen = true;
        return _dialogTcs.Task;
    }

    private void CloseDialog(bool result)
    {
        IsDialogOpen = false;
        _dialogTcs?.TrySetResult(result);
        _dialogTcs = null;
    }

    // ---------- Thông báo nhanh ----------
    private string _toastText = "";
    private bool _isToastVisible;
    private bool _toastIsError;

    public string ToastText { get => _toastText; private set => Set(ref _toastText, value); }
    public bool IsToastVisible { get => _isToastVisible; private set => Set(ref _isToastVisible, value); }
    public bool ToastIsError { get => _toastIsError; private set => Set(ref _toastIsError, value); }
    public RelayCommand CloseToastCommand { get; }

    public void ShowToast(string text, bool isError = false)
    {
        ToastText = text;
        ToastIsError = isError;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ---------- Giám sát trực tiếp ----------
    public void StartLive()
    {
        if (!State.Settings.LiveMonitoring) return;
        _liveTimer.Start();
        _ = TickLiveAsync();
    }

    public void StopLive() => _liveTimer.Stop();

    /// <summary>Tạm dừng cập nhật khi cửa sổ thu nhỏ để tiết kiệm CPU.</summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        if (active && State.Settings.LiveMonitoring) StartLive();
        else StopLive();
    }

    private async Task TickLiveAsync()
    {
        if (_liveBusy || !_isActive) return;
        _liveBusy = true;
        try
        {
            await State.RefreshLiveAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("Cập nhật cảm biến lỗi", ex);
        }
        finally
        {
            _liveBusy = false;
        }
    }

    private void UpdateBadges()
    {
        if (State.Result is not { } result) return;
        void SetBadge(string key, params IssueCategory[] categories)
        {
            var nav = NavItems.First(n => n.Key == key);
            var issues = result.Issues.Where(i => categories.Contains(i.Category) && i.Severity >= Severity.Warning).ToList();
            nav.Badge = issues.Count;
            nav.BadgeSeverity = issues.Count == 0 ? Severity.Ok : issues.Max(i => i.Severity);
        }

        SetBadge(Pages.Health, IssueCategory.Hardware, IssueCategory.Temperature, IssueCategory.Storage);
        SetBadge(Pages.Compatibility, IssueCategory.Compatibility);
        SetBadge(Pages.Drivers, IssueCategory.Driver);
        SetBadge(Pages.Cleanup, IssueCategory.Junk, IssueCategory.Software);
        var issuesNav = NavItems.First(n => n.Key == Pages.Issues);
        issuesNav.Badge = result.Issues.Count(i => i.Severity >= Severity.Warning);
        issuesNav.BadgeSeverity = result.Issues.Count == 0 ? Severity.Ok : result.Issues.Max(i => i.Severity);
        OnPropertyChanged(nameof(LastScanText));
    }

    public static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
}
