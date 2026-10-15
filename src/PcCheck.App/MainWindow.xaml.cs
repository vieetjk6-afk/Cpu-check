using System.Windows;
using System.Windows.Input;
using PcCheck.App.Infrastructure;
using PcCheck.App.ViewModels;

namespace PcCheck.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        StateChanged += (_, _) => (DataContext as MainViewModel)?.SetActive(WindowState != WindowState.Minimized);
        PreviewKeyDown += OnPreviewKeyDown;
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
