using System.Diagnostics;
using System.IO;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Localization;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

/// <summary>Cài đặt: ngôn ngữ, giao diện, tự quét, giám sát trực tiếp, xuất báo cáo.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(MainViewModel main) : base(main)
    {
        OpenLogsCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Directory}\"") { UseShellExecute = true });
        });
    }

    private AppSettings Store => State.Settings;

    public string Language
    {
        get => Loc.Instance.Language;
        set
        {
            if (value == Loc.Instance.Language) return;
            Store.Language = value;
            Store.Save();
            Loc.Instance.SetLanguage(value);
            OnPropertyChanged();
        }
    }

    public string Theme
    {
        get => Store.Theme;
        set
        {
            if (value == Store.Theme) return;
            Store.Theme = value;
            Store.Save();
            ThemeManager.Apply(value);
            OnPropertyChanged();
        }
    }

    public bool AutoScan
    {
        get => Store.AutoScanOnStartup;
        set
        {
            Store.AutoScanOnStartup = value;
            Store.Save();
            OnPropertyChanged();
        }
    }

    public bool LiveMonitoring
    {
        get => Store.LiveMonitoring;
        set
        {
            Store.LiveMonitoring = value;
            Store.Save();
            if (value) Main.StartLive();
            else Main.StopLive();
            OnPropertyChanged();
        }
    }

    public string Version => Main.AppVersion;

    public string AdminStatus => Main.IsAdmin ? Loc.T("settings.admin_yes") : Loc.T("settings.admin_no");

    public string DataFolder => Path.GetDirectoryName(AppSettings.FilePath) ?? "";

    public RelayCommand OpenLogsCommand { get; }

    protected override void Rebuild()
    {
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(AdminStatus));
    }
}
