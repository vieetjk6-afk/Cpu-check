using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PcCheck.App.Infrastructure;

/// <summary>Đổi giao diện sáng/tối lúc chạy và đồng bộ màu thanh tiêu đề Windows.</summary>
public static class ThemeManager
{
    public const string Dark = "dark";
    public const string Light = "light";
    public const string System = "system";

    public static bool IsDark { get; private set; } = true;

    public static event EventHandler? ThemeChanged;

    public static void Apply(string theme)
    {
        IsDark = theme switch
        {
            Light => false,
            System => SystemPrefersDark(),
            _ => true,
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var colors = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PcCheck;component/Themes/Colors.{(IsDark ? "Dark" : "Light")}.xaml", UriKind.Absolute),
        };
        if (dictionaries.Count > 0) dictionaries[0] = colors;
        else dictionaries.Add(colors);

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Thanh tiêu đề tối/sáng (Win10 20H1+) và màu trùng nền cửa sổ (Win11).</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var dark = IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref dark, sizeof(int));
            }

            if (Application.Current.TryFindResource("WindowBackgroundColor") is Color color)
            {
                var colorRef = color.R | (color.G << 8) | (color.B << 16);
                DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref colorRef, sizeof(int));
            }
        }
        catch (Exception ex)
        {
            Core.Util.Log.Warn("Không đặt được màu thanh tiêu đề", ex);
        }
    }

    private const int DwmwaUseImmersiveDarkModeOld = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;

#pragma warning disable SYSLIB1054
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
#pragma warning restore SYSLIB1054
}
