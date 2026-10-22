using System.Windows;
using System.Windows.Input;
using PcCheck.App.Infrastructure;
using PcCheck.App.ViewModels;
using PcCheck.Core.Localization;

namespace PcCheck.App;

public partial class MainWindow : Window
{
    private bool _exitConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        StateChanged += (_, _) => (DataContext as MainViewModel)?.SetActive(WindowState != WindowState.Minimized);
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
        Closed += (_, _) => App.Current.ExitApplication();
    }

    /// <summary>
    /// Đóng cửa sổ chính khi widget đang hiện -> chỉ ẩn đi để widget (và tác vụ đang chạy) tiếp tục.
    /// Không có widget mà đang gỡ phần mềm / quét / kiểm tra ổ -> hỏi lại trước khi thoát.
    /// </summary>
    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (App.Current.IsExiting || DataContext is not MainViewModel vm) return;
        if (vm.Widget.IsVisible)
        {
            e.Cancel = true;
            Hide();
            vm.SetActive(false);
            return;
        }

        if (_exitConfirmed || vm.RunningWorkText is not { Length: > 0 } work) return;
        e.Cancel = true;
        if (await vm.ConfirmAsync(Loc.T("exit.busy_title"), Loc.F("exit.busy", work), Loc.T("exit.anyway"), danger: true))
        {
            _exitConfirmed = true;
            Close();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Esc đóng hộp thoại, F5 quét lại, Ctrl+S xuất báo cáo.
        if (e.Key == Key.Escape && vm.IsDialogOpen)
        {
            vm.DialogCancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && vm.ScanCommand.CanExecute(null))
        {
            vm.ScanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && vm.ExportReportCommand.CanExecute(null))
        {
            vm.ExportReportCommand.Execute(null);
            e.Handled = true;
        }
    }
}
