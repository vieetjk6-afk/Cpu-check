using System.Windows;
using System.Windows.Threading;
using PcCheck.App.Infrastructure;
using PcCheck.App.ViewModels;
using PcCheck.App.Views;
using PcCheck.Core.Localization;
using PcCheck.Core.Updates;
using PcCheck.Core.Util;

namespace PcCheck.App;

public partial class App : Application
{
    private const string MutexName = @"Local\PcCheck.SingleInstance";
    private const string ActivateEventName = @"Local\PcCheck.Activate";

    private static Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private AppState? _state;
    private MainViewModel? _main;
    private MainWindow? _window;
    private WidgetWindow? _widget;
    private bool _startupComplete;

    /// <summary>Đang thoát hẳn ứng dụng (không chỉ ẩn cửa sổ chính).</summary>
    public bool IsExiting { get; private set; }

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Lỗi không xử lý", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Task lỗi không được quan sát", args.Exception);
            args.SetObserved();
        };

        // Chỉ cho chạy 1 phiên bản: lần mở thứ hai sẽ đánh thức phiên bản đang chạy rồi thoát.
        // Ngay sau khi tự cập nhật: chờ phiên bản cũ thoát hẳn (tối đa 2 phút) thay vì đánh thức nó; quá thời gian vẫn mở
        // (phiên bản cũ đang thoát sẽ bỏ qua lời đánh thức, nếu không mở thì người dùng không còn gì đang chạy).
        var afterUpdate = e.Args.Any(a => a.Equals(UpdateService.AfterUpdateArgument, StringComparison.OrdinalIgnoreCase));
        _instanceMutex = new Mutex(false, MutexName);
        bool isFirst;
        try
        {
            isFirst = _instanceMutex.WaitOne(afterUpdate ? TimeSpan.FromMinutes(2) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // Phiên bản trước thoát mà không trả mutex – coi như đã có quyền.
            isFirst = true;
        }

        if (!isFirst && afterUpdate)
        {
            Log.Warn("Phiên bản cũ chưa thoát sau 2 phút – vẫn mở bản mới");
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
        else if (!isFirst)
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ActivateEventName);
                existing.Set();
            }
            catch (Exception ex)
            {
                Log.Warn("Không đánh thức được phiên bản đang chạy", ex);
            }

            Shutdown();
            return;
        }

        var widgetOnly = e.Args.Any(a => a.Equals(AutoStart.WidgetArgument, StringComparison.OrdinalIgnoreCase));
        var settings = AppSettings.Load();
        Loc.Instance.SetLanguage(string.IsNullOrEmpty(settings.Language) ? Loc.DefaultLanguageForSystem() : settings.Language);
        ThemeManager.Apply(settings.Theme);
        Log.Info($"Khởi động PC Check, admin={Elevation.IsAdministrator()}, ngôn ngữ={Loc.Instance.Language}, widget={widgetOnly}");

        _state = new AppState(settings);
        _main = new MainViewModel(_state);
        _window = new MainWindow { DataContext = _main };
        MainWindow = _window;

        _main.Widget.VisibilityChanged += (_, _) => SyncWidgetWindow();
        _main.Widget.OpenMainRequested += (_, page) =>
        {
            _main.Navigate(page);
            ShowMainWindow();
        };
        _main.Widget.ExitRequested += (_, _) => ExitApplication();

        if (widgetOnly)
        {
            // Khởi động cùng Windows: chỉ hiện widget, cửa sổ chính mở khi người dùng cần.
            _main.SetActive(false);
            _main.Widget.IsVisible = true;
        }
        else
        {
            _window.Show();
            if (settings.Widget.Visible) _main.Widget.IsVisible = true;
        }

        SyncWidgetWindow();
        ListenForActivation();
        _startupComplete = true;
        _ = StartBackgroundAsync(_main, settings, autoScan: settings.AutoScanOnStartup && !widgetOnly);

        if (Environment.ProcessPath is { } exe) _ = UpdateService.CleanupAsync(exe);
        if (afterUpdate)
        {
            Log.Info($"Đã cập nhật lên {_main.AppVersion}");
            _main.ShowToast(Loc.F("update.done", _main.AppVersion));
        }

        _ = _main.Updates.AutoCheckAsync();
    }

    private static async Task StartBackgroundAsync(MainViewModel main, AppSettings settings, bool autoScan)
    {
        try
        {
            await main.State.Sensors.EnsureOpenAsync();
            main.UpdateLiveTimer();
            if (autoScan) await main.ScanAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Khởi động nền lỗi", ex);
            main.ShowToast(Loc.F("error.unexpected", ex.Message), isError: true);
        }
    }

    /// <summary>Hiện cửa sổ chính (từ widget hoặc khi mở PC Check lần nữa).</summary>
    public void ShowMainWindow()
    {
        if (_window is null || _main is null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
        _main.SetActive(true);
    }

    private void SyncWidgetWindow()
    {
        if (_main is null) return;
        if (_main.Widget.IsVisible)
        {
            _widget ??= new WidgetWindow { DataContext = _main.Widget };
            if (!_widget.IsVisible) _widget.Show();
        }
        else
        {
            _widget?.Hide();

            // Không để ứng dụng chạy ngầm mà không có cửa sổ nào.
            if (_window is { IsVisible: false } && !IsExiting) ShowMainWindow();
        }
    }

    public void ExitApplication()
    {
        if (IsExiting) return;
        IsExiting = true;
        _main?.SetActive(false);
        _widget?.Close();
        Shutdown();
    }

    private void ListenForActivation()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    _activateEvent.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (IsExiting) return;
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        })
        {
            IsBackground = true,
            Name = "PcCheck.Activation",
        };
        thread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting = true;
        _main?.SetActive(false);
        _state?.Dispose();
        _activateEvent?.Dispose();
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Lỗi giao diện", e.Exception);
        e.Handled = true;

        // Lỗi ngay khi khởi động: báo lỗi rồi thoát, tránh tiến trình chạy ngầm không có cửa sổ.
        if (!_startupComplete)
        {
            MessageBox.Show(e.Exception.Message, "PC Check", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ShowError(e.Exception);
    }

    public static void ShowError(Exception ex)
    {
        if (Current?._main is { } vm)
        {
            vm.ShowToast(Loc.F("error.unexpected", ex.Message), isError: true);
        }
        else
        {
            MessageBox.Show(ex.Message, "PC Check", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
