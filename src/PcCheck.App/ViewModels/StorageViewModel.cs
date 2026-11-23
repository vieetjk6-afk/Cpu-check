using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Cleanup;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Storage;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

/// <summary>Ổ đĩa / thư mục người dùng ở màn hình bắt đầu phân tích.</summary>
public sealed class LocationItem
{
    public LocationItem(DiskLocation location)
    {
        Location = location;
        Name = location.IsDrive
            ? string.IsNullOrWhiteSpace(location.Label) ? Loc.F("storage.drive", location.Name) : $"{location.Label} ({location.Name})"
            : location.Name;
        Glyph = location.IsDrive ? "" : "";
        if (location.IsDrive && location.TotalBytes > 0)
        {
            var used = location.TotalBytes - location.FreeBytes;
            UsedPercent = 100.0 * used / location.TotalBytes;
            Detail = Loc.F("storage.drive_usage", Format.Bytes(used), Format.Bytes(location.TotalBytes), Format.Bytes(location.FreeBytes));
        }
        else
        {
            Detail = location.Path;
        }
    }

    public DiskLocation Location { get; }
    public string Name { get; }
    public string Detail { get; }
    public string Glyph { get; }
    public double UsedPercent { get; }
    public bool IsDrive => Location.IsDrive;
    public bool IsLow => IsDrive && UsedPercent >= 90;
}

/// <summary>Một dòng (thư mục/tệp) trong kết quả phân tích.</summary>
public sealed class EntryItem
{
    public EntryItem(DiskEntry entry, long total, long largest)
    {
        Entry = entry;
        Size = entry.Status == ScanStatus.Unavailable ? Loc.T("storage.unavailable") : Format.Bytes(entry.Bytes);
        if (entry.Status == ScanStatus.Partial) Size = "≥ " + Size;
        BarPercent = largest > 0 ? 100.0 * entry.Bytes / largest : 0;
        Share = total > 0 ? (100.0 * entry.Bytes / total).ToString(entry.Bytes * 1000 / Math.Max(1, total) < 10 ? "0.0" : "0", Loc.Instance.Culture) + "%" : "";
        Files = entry.IsDirectory ? Loc.F("junk.files_count", entry.Files) : "";
        Modified = entry.LastWriteUtc is { } t ? Format.Date(t.ToLocalTime()) : "";
        Insight = entry.InsightKey is { } key ? Loc.T("storage.insight." + key) : "";
    }

    public DiskEntry Entry { get; }
    public string Name => Entry.Name;
    public string Path => Entry.Path;
    public bool IsDirectory => Entry.IsDirectory;
    public string Glyph => IsDirectory ? "" : "";
    public string Size { get; }
    public double BarPercent { get; }
    public string Share { get; }
    public string Files { get; }
    public string Modified { get; }
    public string Insight { get; }
}

public sealed class LargeFileItem(LargeFile file)
{
    public LargeFile File { get; } = file;
    public string Name => File.Name;
    public string Path => File.Path;
    public string Folder => System.IO.Path.GetDirectoryName(File.Path) ?? "";
    public string Size { get; } = Format.Bytes(file.Bytes);
    public string Modified { get; } = Format.Date(file.LastWriteUtc.ToLocalTime());
}

/// <summary>Mục có ô chọn (tệp cài đặt, thư mục build).</summary>
public abstract class SelectableItem : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;

    protected SelectableItem(bool selected, Action changed)
    {
        _isSelected = selected;
        _changed = changed;
    }

    public abstract string Path { get; }
    public abstract long Bytes { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value)) _changed();
        }
    }
}

public sealed class InstallerItem : SelectableItem
{
    public InstallerItem(InstallerFile file, DateTime nowUtc, Action changed)
        : base(InstallerFinder.IsSuggested(file, nowUtc), changed)
    {
        File = file;
        IsSuggested = IsSelected;
        Size = Format.Bytes(file.Bytes);
        Age = Loc.F("storage.age_days", file.AgeDays(nowUtc));
        KindText = Loc.T("installer.kind." + file.Kind);
        Installed = file.InstalledMatch is { } name ? Loc.F("installer.installed", name) : "";
    }

    public InstallerFile File { get; }
    public override string Path => File.Path;
    public override long Bytes => File.Bytes;
    public string Name => File.Name;
    public string Folder => System.IO.Path.GetDirectoryName(File.Path) ?? "";
    public bool IsSuggested { get; }
    public string Size { get; }
    public string Age { get; }
    public string KindText { get; }
    public string Installed { get; }
}

public sealed class ArtifactItem : SelectableItem
{
    public ArtifactItem(ProjectArtifact artifact, Action changed)
        : base(!artifact.IsRecent, changed)
    {
        Artifact = artifact;
        Size = Format.Bytes(artifact.Bytes);
        Files = Loc.F("junk.files_count", artifact.Files);
        Activity = Loc.F("purge.activity", Format.Date(artifact.LastActivityUtc.ToLocalTime()));
        KindHint = Loc.T(artifact.IsDependency ? "purge.dependency" : "purge.build_output");
    }

    public ProjectArtifact Artifact { get; }
    public override string Path => Artifact.Path;
    public override long Bytes => Artifact.Bytes;
    public string Project => Artifact.ProjectName;
    public string Kind => Artifact.Kind;
    public bool IsRecent => Artifact.IsRecent;
    public string Size { get; }
    public string Files { get; }
    public string Activity { get; }
    public string KindHint { get; }
}

public sealed class HistoryItem(OperationRecord record)
{
    public string Time { get; } = record.Time.ToString("g", Loc.Instance.Culture);
    public string Source { get; } = Loc.T("history.source." + record.Source);
    public string Action { get; } = Loc.T("history.action." + record.Action);
    public string Target { get; } = record.Target;
    public string Size { get; } = record.Bytes > 0 ? Format.Bytes(record.Bytes) : "";
    public string Count { get; } = record.Count > 1 ? Loc.F("junk.files_count", record.Count) : "";
    public bool IsFailure => record.Action is OperationLog.ActionFailed or OperationLog.ActionSkipped;
}

/// <summary>
/// Trang Ổ đĩa (lấy ý tưởng từ "mo analyze / installer / purge / history" của Mole): xem thư mục nào chiếm dung lượng,
/// tệp lớn nhất, tệp cài đặt đã tải về, thư mục build của dự án lập trình và lịch sử dọn dẹp.
/// </summary>
public sealed class StorageViewModel : PageViewModel
{
    private readonly Dictionary<string, DiskReport> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _progressTimer;
    private int _selectedTab;
    private int _analyzerView;
    private DiskReport? _report;
    private bool _isAnalyzing;
    private string _progressText = "";
    private CancellationTokenSource? _cts;
    private DiskScanProgress? _progress;
    private string? _scanningPath;
    private bool _isBusy;
    private string _busyText = "";
    private bool _installersScanned;
    private bool _projectsScanned;

    public StorageViewModel(MainViewModel main) : base(main)
    {
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _progressTimer.Tick += (_, _) => UpdateProgressText();

        OpenLocationCommand = new AsyncRelayCommand(p => p is LocationItem l ? AnalyzeAsync(l.Location.Path) : Task.CompletedTask, _ => !IsAnalyzing);
        OpenEntryCommand = new AsyncRelayCommand(p => p is EntryItem { IsDirectory: true } e ? AnalyzeAsync(e.Path) : Task.CompletedTask, _ => !IsAnalyzing);
        UpCommand = new AsyncRelayCommand(UpAsync, () => !IsAnalyzing && _report is not null);
        HomeCommand = new RelayCommand(GoHome, () => !IsAnalyzing);
        RescanCommand = new AsyncRelayCommand(() => _report is { } r ? AnalyzeAsync(r.Path, force: true) : Task.CompletedTask, () => !IsAnalyzing && _report is not null);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsAnalyzing);
        RevealCommand = new RelayCommand(p => Reveal(PathOf(p)));
        DeleteEntryCommand = new AsyncRelayCommand(p => DeleteOneAsync(p), _ => !IsAnalyzing && !IsBusy);
        ProtectCommand = new RelayCommand(p => Protect(PathOf(p)));
        PickFolderCommand = new AsyncRelayCommand(PickFolderAsync, () => !IsAnalyzing);

        ScanInstallersCommand = new AsyncRelayCommand(ScanInstallersAsync, () => !IsBusy);
        DeleteInstallersCommand = new AsyncRelayCommand(DeleteInstallersAsync, () => !IsBusy && Installers.Any(i => i.IsSelected));
        SelectSuggestedInstallersCommand = new RelayCommand(() =>
        {
            foreach (var item in Installers) item.IsSelected = item.IsSuggested;
        });
        SelectNoInstallersCommand = new RelayCommand(() =>
        {
            foreach (var item in Installers) item.IsSelected = false;
        });

        ScanProjectsCommand = new AsyncRelayCommand(ScanProjectsAsync, () => !IsBusy);
        DeleteArtifactsCommand = new AsyncRelayCommand(DeleteArtifactsAsync, () => !IsBusy && Artifacts.Any(i => i.IsSelected));
        SelectOldArtifactsCommand = new RelayCommand(() =>
        {
            foreach (var item in Artifacts) item.IsSelected = !item.IsRecent;
        });
        SelectNoArtifactsCommand = new RelayCommand(() =>
        {
            foreach (var item in Artifacts) item.IsSelected = false;
        });

        RefreshHistoryCommand = new RelayCommand(LoadHistory);
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync, () => History.Count > 0);
        OpenHistoryFileCommand = new RelayCommand(() => Reveal(OperationLog.FilePath));
        OperationLog.Changed += (_, _) => MainViewModel.RunOnUi(LoadHistory);

        LoadLocations();
        LoadHistory();
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!Set(ref _selectedTab, value)) return;

            // Quét lần đầu khi mở tab (không quét sẵn khi khởi động để không tốn tài nguyên).
            if (value == 1 && !_installersScanned && !IsBusy) _ = RunSafeAsync(ScanInstallersAsync);
            if (value == 2 && !_projectsScanned && !IsBusy) _ = RunSafeAsync(ScanProjectsAsync);
            if (value == 3) LoadHistory();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }

    // ================= Phân tích =================

    public ObservableCollection<LocationItem> Locations { get; } = [];
    public ObservableCollection<EntryItem> Entries { get; } = [];
    public ObservableCollection<LargeFileItem> LargeFiles { get; } = [];

    /// <summary>0 = thư mục con, 1 = tệp lớn nhất.</summary>
    public int AnalyzerView { get => _analyzerView; set => Set(ref _analyzerView, value); }

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (!Set(ref _isAnalyzing, value)) return;
            OnPropertyChanged(nameof(ShowOverview));
            OnPropertyChanged(nameof(ShowReport));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowOverview => _report is null && !IsAnalyzing;
    public bool ShowReport => _report is not null && !IsAnalyzing;

    public string CurrentPath => _report?.Path ?? "";
    public string ReportSummary { get; private set; } = "";
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public string ScanningPath => _scanningPath ?? "";

    public AsyncRelayCommand OpenLocationCommand { get; }
    public AsyncRelayCommand OpenEntryCommand { get; }
    public AsyncRelayCommand UpCommand { get; }
    public RelayCommand HomeCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RevealCommand { get; }
    public AsyncRelayCommand DeleteEntryCommand { get; }
    public RelayCommand ProtectCommand { get; }
    public AsyncRelayCommand PickFolderCommand { get; }

    private void LoadLocations()
    {
        Locations.Clear();
        try
        {
            foreach (var location in DiskAnalyzer.Overview()) Locations.Add(new LocationItem(location));
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc danh sách ổ đĩa lỗi", ex);
        }
    }

    private void GoHome()
    {
        _report = null;
        Entries.Clear();
        LargeFiles.Clear();
        LoadLocations();
        NotifyReport();
    }

    private async Task UpAsync()
    {
        if (_report is null) return;
        var parent = Path.GetDirectoryName(_report.Path.TrimEnd('\\'));
        if (string.IsNullOrEmpty(parent) || _report.Path.TrimEnd('\\').Length <= 2)
        {
            GoHome();
            return;
        }

        await AnalyzeAsync(parent.Length == 2 ? parent + "\\" : parent);
    }

    private async Task PickFolderAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("storage.pick_folder") };
        if (dialog.ShowDialog() == true) await AnalyzeAsync(dialog.FolderName);
    }

    public async Task AnalyzeAsync(string path, bool force = false)
    {
        if (IsAnalyzing) return;
        if (!force && _cache.TryGetValue(path, out var cached))
        {
            ShowReportData(cached);
            return;
        }

        _cts = new CancellationTokenSource();
        _progress = new DiskScanProgress();
        _scanningPath = path;
        OnPropertyChanged(nameof(ScanningPath));
        UpdateProgressText();
        IsAnalyzing = true;
        _progressTimer.Start();
        try
        {
            var report = await DiskAnalyzer.ScanAsync(path, _progress, _cts.Token);
            if (report.Status == ScanStatus.Unavailable)
            {
                Main.ShowToast(Loc.F("storage.cannot_read", path), true);
                return;
            }

            _cache[path] = report;
            ShowReportData(report);
        }
        catch (OperationCanceledException)
        {
            Main.ShowToast(Loc.T("storage.cancelled"));
        }
        catch (Exception ex)
        {
            Log.Error($"Phân tích {path} lỗi", ex);
            Main.ShowToast(Loc.F("error.unexpected", ex.Message), true);
        }
        finally
        {
            _progressTimer.Stop();
            IsAnalyzing = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void UpdateProgressText()
    {
        var p = _progress;
        ProgressText = p is null ? "" : Loc.F("storage.progress", p.Files, Format.Bytes(p.Bytes));
    }

    private void ShowReportData(DiskReport report)
    {
        _report = report;
        Entries.Clear();
        var largest = report.Entries.Count > 0 ? report.Entries.Max(e => e.Bytes) : 0;
        foreach (var entry in report.Entries.Take(500)) Entries.Add(new EntryItem(entry, report.TotalBytes, largest));
        LargeFiles.Clear();
        foreach (var file in report.LargeFiles) LargeFiles.Add(new LargeFileItem(file));
        NotifyReport();
    }

    private void NotifyReport()
    {
        ReportSummary = _report is null
            ? ""
            : Loc.F(_report.Status == ScanStatus.Partial ? "storage.summary_partial" : "storage.summary",
                Format.Bytes(_report.TotalBytes), _report.TotalFiles, _report.Duration.TotalSeconds);
        OnPropertyChanged(nameof(ReportSummary));
        OnPropertyChanged(nameof(CurrentPath));
        OnPropertyChanged(nameof(ShowOverview));
        OnPropertyChanged(nameof(ShowReport));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private static string? PathOf(object? item) => item switch
    {
        EntryItem e => e.Path,
        LargeFileItem f => f.Path,
        SelectableItem s => s.Path,
        HistoryItem h => h.Target,
        string s => s,
        _ => null,
    };

    private static long BytesOf(object? item) => item switch
    {
        EntryItem e => e.Entry.Bytes,
        LargeFileItem f => f.File.Bytes,
        SelectableItem s => s.Bytes,
        _ => 0,
    };

    private void Reveal(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Mở {path} lỗi", ex);
        }
    }

    private void Protect(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Main.ShowToast(Main.Settings.AddProtected(path) ? Loc.F("storage.protected_added", path) : Loc.T("storage.protected_exists"));
    }

    /// <summary>Lý do không cho xóa, hoặc null nếu xóa được.</summary>
    private static string? BlockReason(string path) => PathGuard.Check(path) switch
    {
        DeleteBlock.System => Loc.T("storage.block.system"),
        DeleteBlock.SystemFolder => Loc.T("storage.block.system_folder"),
        DeleteBlock.Protected => Loc.T("storage.block.protected"),
        _ => null,
    };

    private async Task DeleteOneAsync(object? item)
    {
        var path = PathOf(item);
        if (string.IsNullOrEmpty(path)) return;
        if (BlockReason(path) is { } reason)
        {
            Main.ShowToast(reason, true);
            return;
        }

        var bytes = BytesOf(item);
        var message = Loc.F("storage.delete_confirm", path, Format.Bytes(bytes));
        if (PathGuard.IsInsideProgramFiles(path)) message += "\n\n" + Loc.T("storage.delete_program_files");
        if (!await Main.ConfirmAsync(Loc.T("storage.delete"), message, Loc.T("storage.delete"), danger: true)) return;

        IsBusy = true;
        try
        {
            var (ok, _) = await RemoveAsync([(path, bytes)], OperationLog.SourceDisk);
            if (ok.Count == 0) return;
            Main.ShowToast(Loc.F("storage.deleted", Path.GetFileName(path.TrimEnd('\\')), Format.Bytes(bytes)));
            RemoveFromReport(path, bytes);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Bỏ mục đã xóa khỏi kết quả đang xem và xóa bộ đệm của các thư mục cha (số liệu đã cũ).</summary>
    private void RemoveFromReport(string path, long bytes)
    {
        foreach (var key in _cache.Keys.Where(k => LeftoverRules.IsUnder(path, k) || LeftoverRules.SamePath(path, k) || LeftoverRules.IsUnder(k, path)).ToList())
        {
            if (_report is not null && key.Equals(_report.Path, StringComparison.OrdinalIgnoreCase)) continue;
            _cache.Remove(key);
        }

        if (_report is null) return;
        var entry = _report.Entries.FirstOrDefault(e => LeftoverRules.SamePath(e.Path, path));
        if (entry is not null)
        {
            _report.Entries.Remove(entry);
        }
        else if (_report.Entries.FirstOrDefault(e => e.IsDirectory && LeftoverRules.IsUnder(path, e.Path)) is { } parent)
        {
            parent.Bytes = Math.Max(0, parent.Bytes - bytes);
            parent.Files = Math.Max(0, parent.Files - 1);
        }

        _report.LargeFiles.RemoveAll(f => LeftoverRules.SamePath(f.Path, path) || LeftoverRules.IsUnder(f.Path, path));
        _report.TotalBytes = Math.Max(0, _report.TotalBytes - bytes);
        _report.Entries.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        ShowReportData(_report);
    }

    /// <summary>
    /// Chuyển vào Thùng rác; mục Thùng rác không giữ được (quá lớn, ổ USB...) thì hỏi có xóa vĩnh viễn không.
    /// Trả về các đường dẫn đã xóa và các đường dẫn còn lại.
    /// </summary>
    private async Task<(List<string> Ok, List<string> Failed)> RemoveAsync(IReadOnlyList<(string Path, long Bytes)> items, string source)
    {
        var ok = new List<string>();
        var failed = new List<(string Path, long Bytes)>();
        var history = new List<OperationRecord>();
        foreach (var (path, bytes) in items)
        {
            BusyText = Loc.F("storage.deleting", Path.GetFileName(path.TrimEnd('\\')));
            if (BlockReason(path) is not null)
            {
                failed.Add((path, bytes));
                history.Add(Record(source, OperationLog.ActionSkipped, path, 0, "protected"));
                continue;
            }

            bool moved;
            try
            {
                moved = await RecycleBin.MoveAsync(path, bytes);
            }
            catch (Exception ex)
            {
                Log.Warn($"Chuyển {path} vào Thùng rác lỗi", ex);
                moved = false;
            }

            if (moved)
            {
                ok.Add(path);
                history.Add(Record(source, OperationLog.ActionRecycled, path, bytes));
            }
            else
            {
                failed.Add((path, bytes));
            }
        }

        var permanent = failed.Where(f => (File.Exists(f.Path) || Directory.Exists(f.Path)) && BlockReason(f.Path) is null).ToList();
        if (permanent.Count > 0
            && await Main.ConfirmAsync(Loc.T("storage.permanent_title"),
                Loc.F("storage.permanent_confirm", permanent.Count, Format.Bytes(permanent.Sum(p => p.Bytes)), string.Join("\n", permanent.Take(5).Select(p => p.Path))),
                Loc.T("storage.permanent_delete"), danger: true))
        {
            foreach (var (path, bytes) in permanent)
            {
                BusyText = Loc.F("storage.deleting", Path.GetFileName(path.TrimEnd('\\')));
                var result = await Task.Run(() => FileDeleter.DeleteTree(path));
                if (!File.Exists(path) && !Directory.Exists(path))
                {
                    ok.Add(path);
                    failed.RemoveAll(f => f.Path == path);
                    history.Add(Record(source, OperationLog.ActionRemoved, path, result.FreedBytes));
                }
                else
                {
                    history.Add(Record(source, OperationLog.ActionFailed, path, result.FreedBytes, $"{result.FailedFiles} locked"));
                }
            }
        }
        else
        {
            history.AddRange(permanent.Select(p => Record(source, OperationLog.ActionSkipped, p.Path, 0, "recycle bin")));
        }

        OperationLog.Record(history);
        if (failed.Count > 0) Main.ShowToast(Loc.F("storage.delete_failed", failed.Count), true);
        return (ok, failed.Select(f => f.Path).ToList());
    }

    private static OperationRecord Record(string source, string action, string target, long bytes, string? detail = null) =>
        new() { Time = DateTime.Now, Source = source, Action = action, Target = target, Bytes = bytes, Detail = detail };

    // ================= Tệp cài đặt =================

    public ObservableCollection<InstallerItem> Installers { get; } = [];
    public string InstallersSummary { get; private set; } = "";
    public string InstallersSelected => Loc.F("storage.selected", Installers.Count(i => i.IsSelected), Format.Bytes(Installers.Where(i => i.IsSelected).Sum(i => i.Bytes)));
    public AsyncRelayCommand ScanInstallersCommand { get; }
    public AsyncRelayCommand DeleteInstallersCommand { get; }
    public RelayCommand SelectSuggestedInstallersCommand { get; }
    public RelayCommand SelectNoInstallersCommand { get; }

    private async Task ScanInstallersAsync()
    {
        IsBusy = true;
        BusyText = Loc.T("installer.scanning");
        try
        {
            var programs = State.Result?.Data.Programs ?? [];
            var files = await InstallerFinder.ScanAsync(programs);
            var now = DateTime.UtcNow;
            Installers.Clear();
            foreach (var file in files) Installers.Add(new InstallerItem(file, now, NotifyInstallers));
            _installersScanned = true;
            NotifyInstallers();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyInstallers()
    {
        InstallersSummary = !_installersScanned ? "" : Installers.Count == 0 ? Loc.T("installer.none")
            : Loc.F("installer.summary", Installers.Count, Format.Bytes(Installers.Sum(f => f.Bytes)));
        OnPropertyChanged(nameof(InstallersSummary));
        OnPropertyChanged(nameof(InstallersSelected));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task DeleteInstallersAsync()
    {
        var selected = Installers.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;
        var size = Format.Bytes(selected.Sum(i => i.Bytes));
        if (!await Main.ConfirmAsync(Loc.T("installer.delete"), Loc.F("installer.confirm", selected.Count, size), Loc.T("installer.delete"), danger: true)) return;

        IsBusy = true;
        try
        {
            var (ok, _) = await RemoveAsync(selected.Select(i => (i.Path, i.Bytes)).ToList(), OperationLog.SourceInstallers);
            var removed = selected.Where(i => ok.Contains(i.Path)).ToList();
            foreach (var item in removed) Installers.Remove(item);
            if (removed.Count > 0) Main.ShowToast(Loc.F("installer.deleted", removed.Count, Format.Bytes(removed.Sum(r => r.Bytes))));
            NotifyInstallers();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================= Thư mục build dự án =================

    public ObservableCollection<ArtifactItem> Artifacts { get; } = [];
    public string ArtifactsSummary { get; private set; } = "";
    public string ProjectRootsText { get; private set; } = "";
    public string ArtifactsSelected => Loc.F("storage.selected", Artifacts.Count(i => i.IsSelected), Format.Bytes(Artifacts.Where(i => i.IsSelected).Sum(i => i.Bytes)));
    public AsyncRelayCommand ScanProjectsCommand { get; }
    public AsyncRelayCommand DeleteArtifactsCommand { get; }
    public RelayCommand SelectOldArtifactsCommand { get; }
    public RelayCommand SelectNoArtifactsCommand { get; }

    private async Task ScanProjectsAsync()
    {
        IsBusy = true;
        BusyText = Loc.T("purge.scanning");
        try
        {
            var roots = await Task.Run(() => ProjectPurge.SearchRoots(State.Settings.ProjectFolders));
            _projectRoots = roots;
            UpdateRootsText();
            var progress = new Progress<string>(root => BusyText = Loc.F("purge.scanning_in", root));
            var artifacts = await ProjectPurge.ScanAsync(roots, progress);
            Artifacts.Clear();
            foreach (var artifact in artifacts) Artifacts.Add(new ArtifactItem(artifact, NotifyArtifacts));
            _projectsScanned = true;
            NotifyArtifacts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private List<string>? _projectRoots;

    private void UpdateRootsText()
    {
        ProjectRootsText = _projectRoots is null ? "" : _projectRoots.Count == 0 ? Loc.T("purge.no_roots") : Loc.F("purge.roots", string.Join(" · ", _projectRoots));
        OnPropertyChanged(nameof(ProjectRootsText));
    }

    private void NotifyArtifacts()
    {
        ArtifactsSummary = !_projectsScanned ? "" : Artifacts.Count == 0 ? Loc.T("purge.none")
            : Loc.F("purge.summary", Artifacts.Count, Format.Bytes(Artifacts.Sum(a => a.Bytes)));
        OnPropertyChanged(nameof(ArtifactsSummary));
        OnPropertyChanged(nameof(ArtifactsSelected));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private async Task DeleteArtifactsAsync()
    {
        var selected = Artifacts.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;
        var size = Format.Bytes(selected.Sum(i => i.Bytes));
        var message = Loc.F("purge.confirm", selected.Count, size);
        if (selected.Any(s => s.IsRecent)) message += "\n\n" + Loc.T("purge.confirm_recent");
        if (!await Main.ConfirmAsync(Loc.T("purge.delete"), message, Loc.T("purge.delete"), danger: true)) return;

        IsBusy = true;
        long freed = 0;
        var history = new List<OperationRecord>();
        try
        {
            foreach (var item in selected)
            {
                BusyText = Loc.F("storage.deleting", $"{item.Project}\\{item.Kind}");
                var result = await ProjectPurge.DeleteAsync(item.Artifact);
                freed += result.FreedBytes;
                var gone = !Directory.Exists(item.Path);
                history.Add(Record(OperationLog.SourcePurge, gone ? OperationLog.ActionRemoved : OperationLog.ActionFailed, item.Path, result.FreedBytes,
                    gone ? null : $"{result.FailedFiles} locked"));
                if (gone) Artifacts.Remove(item);
            }

            OperationLog.Record(history);
            var failed = history.Count(h => h.Action == OperationLog.ActionFailed);
            Main.ShowToast(failed > 0 ? Loc.F("purge.deleted_partial", Format.Bytes(freed), failed) : Loc.F("purge.deleted", Format.Bytes(freed)), failed > 0);
            NotifyArtifacts();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ================= Lịch sử =================

    public ObservableCollection<HistoryItem> History { get; } = [];
    public string HistorySummary { get; private set; } = "";
    public RelayCommand RefreshHistoryCommand { get; }
    public AsyncRelayCommand ClearHistoryCommand { get; }
    public RelayCommand OpenHistoryFileCommand { get; }

    private void LoadHistory()
    {
        var records = OperationLog.Read();
        History.Clear();
        foreach (var record in records) History.Add(new HistoryItem(record));
        var freed = records.Where(r => r.Action is OperationLog.ActionRemoved or OperationLog.ActionRecycled).Sum(r => r.Bytes);
        HistorySummary = records.Count == 0 ? Loc.T("history.empty") : Loc.F("history.summary", records.Count, Format.Bytes(freed));
        OnPropertyChanged(nameof(HistorySummary));
    }

    private async Task ClearHistoryAsync()
    {
        if (!await Main.ConfirmAsync(Loc.T("history.clear"), Loc.T("history.clear_confirm"), Loc.T("history.clear"))) return;
        OperationLog.Clear();
    }

    // ================= Chung =================

    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log.Error("Thao tác trang Ổ đĩa lỗi", ex);
            Main.ShowToast(Loc.F("error.unexpected", ex.Message), true);
        }
    }

    protected override void Rebuild()
    {
        // Đổi ngôn ngữ / có kết quả quét mới: dựng lại các chuỗi hiển thị.
        LoadLocations();
        if (_report is not null) ShowReportData(_report);
        var now = DateTime.UtcNow;
        var installers = Installers.Select(i => (i.File, i.IsSelected)).ToList();
        Installers.Clear();
        foreach (var (file, selected) in installers) Installers.Add(new InstallerItem(file, now, NotifyInstallers) { IsSelected = selected });
        var artifacts = Artifacts.Select(a => (a.Artifact, a.IsSelected)).ToList();
        Artifacts.Clear();
        foreach (var (artifact, selected) in artifacts) Artifacts.Add(new ArtifactItem(artifact, NotifyArtifacts) { IsSelected = selected });
        LoadHistory();
        UpdateRootsText();
        NotifyInstallers();
        NotifyArtifacts();
    }
}
