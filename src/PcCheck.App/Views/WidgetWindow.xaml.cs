using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using PcCheck.App.ViewModels;

namespace PcCheck.App.Views;

/// <summary>
/// Cửa sổ widget nổi: không viền, nền trong suốt, không hiện trên taskbar / Alt+Tab.
/// Kéo thả để di chuyển, tự hít vào mép màn hình, nhớ vị trí giữa các lần mở.
/// </summary>
public partial class WidgetWindow : Window
{
    /// <summary>Khoảng chừa cho bóng đổ (bằng Margin của Root).</summary>
    private const double ShadowMargin = 12;

    /// <summary>Cách mép màn hình khi hít mép / đặt mặc định.</summary>
    private const double EdgeGap = 8;

    /// <summary>Khoảng cách tới mép để tự hít vào.</summary>
    private const double SnapDistance = 20;

    private bool _loaded;

    public WidgetWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => HideFromAltTab();
        Loaded += (_, _) =>
        {
            RestorePosition();
            _loaded = true;
        };
        SizeChanged += (_, _) =>
        {
            if (_loaded) KeepOnScreen();
        };
        Closing += OnClosing;
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is WidgetViewModel oldVm) oldVm.ResetPositionRequested -= OnResetPosition;
            if (e.NewValue is WidgetViewModel newVm) newVm.ResetPositionRequested += OnResetPosition;
        };
    }

    private WidgetViewModel? Vm => DataContext as WidgetViewModel;

    private void OnRootMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Vm?.OpenMainCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (Vm?.Locked == true || e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        SnapToEdges();
        Vm?.SavePosition(Left, Top);
    }

    /// <summary>Alt+F4 hoặc đóng từ hệ thống: chỉ ẩn widget (trừ khi đang thoát ứng dụng).</summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.Current.IsExiting) return;
        e.Cancel = true;
        Vm?.HideCommand.Execute(null);
    }

    private void OnResetPosition(object? sender, EventArgs e) => PlaceDefault();

    private void RestorePosition()
    {
        var vm = Vm;
        if (vm is null || double.IsNaN(vm.Store.Left) || double.IsNaN(vm.Store.Top) || !IsMostlyOnScreen(vm.Store.Left, vm.Store.Top))
        {
            PlaceDefault();
            return;
        }

        Left = vm.Store.Left;
        Top = vm.Store.Top;
        KeepOnScreen();
    }

    /// <summary>Góc trên bên phải màn hình chính.</summary>
    private void PlaceDefault()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Right - EdgeGap - ActualWidth + ShadowMargin;
        Top = work.Top + EdgeGap - ShadowMargin;
        Vm?.SavePosition(Left, Top);
    }

    private bool IsMostlyOnScreen(double left, double top)
    {
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = new Rect(left + ShadowMargin, top + ShadowMargin,
            Math.Max(1, ActualWidth - 2 * ShadowMargin), Math.Max(1, ActualHeight - 2 * ShadowMargin));
        var overlap = Rect.Intersect(virtualScreen, visible);
        return !overlap.IsEmpty && overlap.Width >= 60 && overlap.Height >= 40;
    }

    /// <summary>Hít vào mép vùng làm việc của màn hình đang chứa widget khi thả gần mép.</summary>
    private void SnapToEdges()
    {
        var work = CurrentWorkArea();
        var visibleLeft = Left + ShadowMargin;
        var visibleTop = Top + ShadowMargin;
        var visibleRight = Left + ActualWidth - ShadowMargin;
        var visibleBottom = Top + ActualHeight - ShadowMargin;

        if (Math.Abs(visibleLeft - work.Left) < SnapDistance) Left = work.Left + EdgeGap - ShadowMargin;
        else if (Math.Abs(work.Right - visibleRight) < SnapDistance) Left = work.Right - EdgeGap - ActualWidth + ShadowMargin;

        if (Math.Abs(visibleTop - work.Top) < SnapDistance) Top = work.Top + EdgeGap - ShadowMargin;
        else if (Math.Abs(work.Bottom - visibleBottom) < SnapDistance) Top = work.Bottom - EdgeGap - ActualHeight + ShadowMargin;
    }

    /// <summary>Khi widget đổi kích thước (thêm/bớt thông số, đổi bố cục) không để tràn khỏi màn hình.</summary>
    private void KeepOnScreen()
    {
        var work = CurrentWorkArea();
        var visibleRight = Left + ActualWidth - ShadowMargin;
        var visibleBottom = Top + ActualHeight - ShadowMargin;
        if (visibleRight > work.Right) Left -= visibleRight - work.Right + EdgeGap;
        if (visibleBottom > work.Bottom) Top -= visibleBottom - work.Bottom + EdgeGap;
        if (Left + ShadowMargin < work.Left) Left = work.Left - ShadowMargin + EdgeGap;
        if (Top + ShadowMargin < work.Top) Top = work.Top - ShadowMargin + EdgeGap;
    }

    /// <summary>Vùng làm việc (trừ taskbar) của màn hình chứa widget, đổi sang đơn vị WPF.</summary>
    private Rect CurrentWorkArea()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = PresentationSource.FromVisual(this);
            if (hwnd != IntPtr.Zero && source?.CompositionTarget is not null)
            {
                var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    var transform = source.CompositionTarget.TransformFromDevice;
                    var topLeft = transform.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
                    var bottomRight = transform.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));
                    return new Rect(topLeft, bottomRight);
                }
            }
        }
        catch (Exception ex)
        {
            Core.Util.Log.Warn("Không lấy được vùng màn hình", ex);
        }

        return SystemParameters.WorkArea;
    }

    /// <summary>Kiểu cửa sổ công cụ: không hiện trong Alt+Tab.</summary>
    private void HideFromAltTab()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, style | WsExToolWindow);
    }

    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
#pragma warning restore SYSLIB1054
}
