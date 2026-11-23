using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Localization;
using PcCheck.Core.Storage;
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

        ProtectedPathList = new ObservableCollection<string>(Store.ProtectedPaths);
        ProjectFolderList = new ObservableCollection<string>(Store.ProjectFolders);
        AddProtectedCommand = new RelayCommand(() => AddFolder(ProtectedPathList, Store.ProtectedPaths, "settings.protected_pick"));
        RemoveProtectedCommand = new RelayCommand(p => RemoveFolder(ProtectedPathList, Store.ProtectedPaths, p as string));
        AddProjectFolderCommand = new RelayCommand(() => AddFolder(ProjectFolderList, Store.ProjectFolders, "settings.projects_pick"));
        RemoveProjectFolderCommand = new RelayCommand(p => RemoveFolder(ProjectFolderList, Store.ProjectFolders, p as string));
    }

    // ----- Dọn dẹp an toàn -----

    /// <summary>Thư mục được bảo vệ: mọi chức năng dọn dẹp bỏ qua.</summary>
    public ObservableCollection<string> ProtectedPathList { get; }

    /// <summary>Thư mục dự án lập trình tự thêm để tìm thư mục build.</summary>
    public ObservableCollection<string> ProjectFolderList { get; }

    public RelayCommand AddProtectedCommand { get; }
    public RelayCommand RemoveProtectedCommand { get; }
    public RelayCommand AddProjectFolderCommand { get; }
    public RelayCommand RemoveProjectFolderCommand { get; }

    /// <summary>Thêm thư mục vào danh sách bảo vệ (dùng từ trang Phân tích ổ đĩa).</summary>
    public bool AddProtected(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Store.ProtectedPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) return false;
        Store.ProtectedPaths.Add(path);
        ProtectedPathList.Add(path);
        SaveCleanupSettings();
        return true;
    }

    private void AddFolder(ObservableCollection<string> view, List<string> store, string titleKey)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T(titleKey), Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var folder in dialog.FolderNames)
        {
            if (store.Contains(folder, StringComparer.OrdinalIgnoreCase)) continue;
            store.Add(folder);
            view.Add(folder);
        }

        SaveCleanupSettings();
    }

    private void RemoveFolder(ObservableCollection<string> view, List<string> store, string? path)
    {
        if (path is null) return;
        store.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        view.Remove(path);
        SaveCleanupSettings();
    }

    private void SaveCleanupSettings()
    {
        ProtectedPaths.Set(Store.ProtectedPaths);
        Store.Save();
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
