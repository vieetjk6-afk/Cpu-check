using System.Windows;
using System.Windows.Threading;
using PcCheck.App.Infrastructure;
using PcCheck.App.ViewModels;
using PcCheck.Core.Localization;
using PcCheck.Core.Util;

namespace PcCheck.App;

public partial class App : Application
{
    private AppState? _state;

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

        var settings = AppSettings.Load();
        Loc.Instance.SetLanguage(string.IsNullOrEmpty(settings.Language) ? Loc.DefaultLanguageForSystem() : settings.Language);
        ThemeManager.Apply(settings.Theme);
        Log.Info($"Khởi động PC Check, admin={Elevation.IsAdministrator()}, ngôn ngữ={Loc.Instance.Language}");

        _state = new AppState(settings);
        var main = new MainViewModel(_state);
        var window = new MainWindow { DataContext = main };
        MainWindow = window;
        window.Show();

        // Mở bộ đọc cảm biến ở nền rồi mới quét để giao diện hiện ngay lập tức.
        _ = StartBackgroundAsync(main, settings);
    }

    private static async Task StartBackgroundAsync(MainViewModel main, AppSettings settings)
    {
        try
        {
            await main.State.Sensors.EnsureOpenAsync();
            main.StartLive();
            if (settings.AutoScanOnStartup) await main.ScanAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Khởi động nền lỗi", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _state?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Lỗi giao diện", e.Exception);
        ShowError(e.Exception);
        e.Handled = true;
    }

    public static void ShowError(Exception ex)
    {
        if (Current?.MainWindow?.DataContext is MainViewModel vm)
        {
            vm.ShowToast(Loc.F("error.unexpected", ex.Message), isError: true);
        }
        else
        {
            MessageBox.Show(ex.Message, "PC Check", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
