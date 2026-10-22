using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Cleanup;
using PcCheck.Core.Collectors;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

public enum UninstallStepStatus
{
    Pending,
    Running,
    Waiting,
    Removed,
    StillInstalled,
    Failed,
    Skipped,
}

/// <summary>Một phần mềm trong phiên gỡ sạch.</summary>
public sealed class UninstallStep(InstalledProgram program) : ObservableObject
{
    private UninstallStepStatus _status;
    private int _leftovers = -1;

    public InstalledProgram Program { get; } = program;
    public string Name => Program.Name;

    public UninstallStepStatus Status
    {
        get => _status;
        set
        {
            if (!Set(ref _status, value)) return;
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Severity));
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(IsActive));
        }
    }

    /// <summary>Số phần còn sót tìm thấy (-1 = chưa quét).</summary>
    public int Leftovers
    {
        get => _leftovers;
        set
        {
            if (Set(ref _leftovers, value)) OnPropertyChanged(nameof(StatusText));
        }
    }

    public bool IsActive => Status is UninstallStepStatus.Running or UninstallStepStatus.Waiting;

    public string StatusText => Status switch
    {
        UninstallStepStatus.Removed when Leftovers > 0 => Loc.F("uninstall.status.removed_leftovers", Leftovers),
        UninstallStepStatus.Removed when Leftovers == 0 => Loc.T("uninstall.status.removed_clean"),
        _ => Loc.T("uninstall.status." + Status.ToString().ToLowerInvariant()),
    };

    public Severity Severity => Status switch
    {
        UninstallStepStatus.Removed => Severity.Ok,
        UninstallStepStatus.Failed or UninstallStepStatus.StillInstalled => Severity.Warning,
        _ => Severity.Info,
    };

    public string Glyph => Status switch
    {
        UninstallStepStatus.Running or UninstallStepStatus.Waiting => "\uE895",
        UninstallStepStatus.Removed => "\uE73E",
        UninstallStepStatus.StillInstalled or UninstallStepStatus.Failed => "\uE7BA",
        UninstallStepStatus.Skipped => "\uE711",
        _ => "\uE823",
    };
}

/// <summary>Một phần còn sót hiển thị để người dùng chọn xóa.</summary>
public sealed class LeftoverItem : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;

    public LeftoverItem(Leftover leftover, Action changed)
    {
        Leftover = leftover;
        _changed = changed;
        _isSelected = leftover.HighConfidence;
        Glyph = leftover.Kind switch
        {
            LeftoverKind.Folder => "",
            LeftoverKind.File => "",
            LeftoverKind.Shortcut => "",
            LeftoverKind.RegistryKey or LeftoverKind.RegistryValue => "",
            LeftoverKind.ScheduledTask => "",
            _ => "",
        };
        KindText = Loc.T("leftover.kind." + leftover.Kind.ToString().ToLowerInvariant());
        Path = leftover.ValueName.Length > 0 ? $"{leftover.Path} → {leftover.ValueName}" : leftover.Path;
        Reason = Loc.T(leftover.ReasonKey);
        Size = leftover.Bytes > 0 ? Format.Bytes(leftover.Bytes) : "";
    }

    public Leftover Leftover { get; }
    public string Glyph { get; }
    public string KindText { get; }
    public string Path { get; }
    public string Reason { get; }
    public string Size { get; }
    public string Program => Leftover.Program;
    public bool CanOpen => Leftover.Kind is LeftoverKind.Folder or LeftoverKind.File or LeftoverKind.Shortcut;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value)) _changed();
        }
    }
}

public enum UninstallPhase
{
    Uninstalling,
    Scanning,
    Leftovers,
    Removing,
    Done,
}

/// <summary>
/// Phiên "gỡ sạch": tạo điểm khôi phục (tùy chọn) → chạy lần lượt trình gỡ của từng phần mềm (im lặng nếu có thể) và chờ gỡ xong →
/// quét phần còn sót (thư mục, registry, lối tắt, tác vụ, dịch vụ) → người dùng chọn và xóa (Thùng rác / sao lưu registry).
/// </summary>
public sealed class UninstallSessionViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly CancellationTokenSource _session = new();
    private CancellationTokenSource? _wait;
    private UninstallPhase _phase;
    private string _resultText = "";
    private string _backupFolder = "";
    private string _progressText = "";

    public UninstallSessionViewModel(MainViewModel main, IEnumerable<InstalledProgram> programs)
    {
        _main = main;
        foreach (var program in programs) Steps.Add(new UninstallStep(program));
        SkipWaitCommand = new RelayCommand(() => _wait?.Cancel(), () => Steps.Any(s => s.IsActive));
        StopCommand = new RelayCommand(() => _session.Cancel(), () => Phase == UninstallPhase.Uninstalling && !_session.IsCancellationRequested);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => Phase == UninstallPhase.Leftovers && SelectedCount > 0);
        SkipLeftoversCommand = new RelayCommand(() => Finish(Loc.T("uninstall.leftovers_kept")), () => Phase == UninstallPhase.Leftovers);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        OpenItemCommand = new RelayCommand(p => Open(p as LeftoverItem));
        OpenBackupCommand = new RelayCommand(() =>
        {
            if (Directory.Exists(BackupFolder)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupFolder}\"") { UseShellExecute = true });
        });
        CloseCommand = new RelayCommand(() => Closed?.Invoke(this, EventArgs.Empty), () => Phase is UninstallPhase.Done);
    }

    /// <summary>Phiên kết thúc – trang Dọn dẹp làm mới danh sách phần mềm.</summary>
    public event EventHandler? Closed;

    public ObservableCollection<UninstallStep> Steps { get; } = [];
    public ObservableCollection<LeftoverItem> Leftovers { get; } = [];

    public UninstallPhase Phase
    {
        get => _phase;
        private set
        {
            if (!Set(ref _phase, value)) return;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(ShowLeftovers));
            OnPropertyChanged(nameof(IsBusy));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowLeftovers => Phase is UninstallPhase.Leftovers or UninstallPhase.Removing && Leftovers.Count > 0;
    public bool IsBusy => Phase is UninstallPhase.Uninstalling or UninstallPhase.Scanning or UninstallPhase.Removing;

    public string Title => Phase switch
    {
        UninstallPhase.Uninstalling => Loc.F("uninstall.title.running", Steps.Count(s => s.Status is not UninstallStepStatus.Pending), Steps.Count),
        UninstallPhase.Scanning => Loc.T("uninstall.title.scanning"),
        UninstallPhase.Leftovers => Loc.F("uninstall.title.leftovers", Leftovers.Count, Format.Bytes(Leftovers.Sum(l => l.Leftover.Bytes))),
        UninstallPhase.Removing => Loc.T("uninstall.title.removing"),
        _ => Loc.T("uninstall.title.done"),
    };

    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public string ResultText { get => _resultText; private set => Set(ref _resultText, value); }
    public string BackupFolder { get => _backupFolder; private set => Set(ref _backupFolder, value); }
    public int SelectedCount => Leftovers.Count(l => l.IsSelected);
    public string SelectedSize => Format.Bytes(Leftovers.Where(l => l.IsSelected).Sum(l => l.Leftover.Bytes));

    public RelayCommand SkipWaitCommand { get; }
    public RelayCommand StopCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }
    public RelayCommand SkipLeftoversCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand OpenItemCommand { get; }
    public RelayCommand OpenBackupCommand { get; }
    public RelayCommand CloseCommand { get; }

    public async Task RunAsync(bool quiet, bool restorePoint)
    {
        try
        {
            Phase = UninstallPhase.Uninstalling;
            if (restorePoint)
            {
                ProgressText = Loc.T("uninstall.restore_point");
                var ok = await FixService.TryCreateRestorePointAsync("PC Check - " + Loc.T("uninstall.restore_point_name"));
                if (!ok) _main.ShowToast(Loc.T("uninstall.restore_point_failed"));
            }

            foreach (var step in Steps)
            {
                if (_session.IsCancellationRequested)
                {
                    step.Status = UninstallStepStatus.Skipped;
                    continue;
                }

                OnPropertyChanged(nameof(Title));
                ProgressText = Loc.F("uninstall.progress", step.Name);
                step.Status = UninstallStepStatus.Running;
                _wait = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
                var stage = new Progress<string>(s =>
                {
                    if (s == "waiting" && step.Status == UninstallStepStatus.Running) step.Status = UninstallStepStatus.Waiting;
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                });
                try
                {
                    var result = await Uninstaller.RunAsync(step.Program, quiet, stage, _wait.Token);
                    step.Status = result switch
                    {
                        UninstallResult.Removed => UninstallStepStatus.Removed,
                        UninstallResult.StillInstalled => UninstallStepStatus.StillInstalled,
                        _ => UninstallStepStatus.Failed,
                    };
                }
                catch (OperationCanceledException)
                {
                    // Người dùng bấm "Tiếp tục" (thôi chờ) hoặc "Dừng": xem phần mềm đã thật sự gỡ chưa.
                    step.Status = Uninstaller.IsInstalled(step.Program) ? UninstallStepStatus.StillInstalled : UninstallStepStatus.Removed;
                }
                finally
                {
                    _wait.Dispose();
                    _wait = null;
                }

                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }

            await ScanLeftoversAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Phiên gỡ sạch lỗi", ex);
            Finish(Loc.F("error.unexpected", ex.Message));
        }
    }

    private async Task ScanLeftoversAsync()
    {
        var removed = Steps.Where(s => s.Status == UninstallStepStatus.Removed && s.Program.Scope != ProgramScope.Store).ToList();
        if (removed.Count == 0)
        {
            Finish(Loc.T(Steps.Any(s => s.Status == UninstallStepStatus.Removed) ? "uninstall.done_clean" : "uninstall.nothing_removed"));
            return;
        }

        Phase = UninstallPhase.Scanning;
        ProgressText = Loc.T("uninstall.scanning");
        var stillInstalled = await Task.Run(SoftwareCollector.Collect);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in removed)
        {
            var found = await Task.Run(() => LeftoverScanner.Scan(step.Program, stillInstalled));
            var unique = found.Where(l => seen.Add($"{l.Kind}|{l.Path}|{l.ValueName}")).ToList();
            step.Leftovers = unique.Count;
            foreach (var leftover in unique) Leftovers.Add(new LeftoverItem(leftover, OnSelectionChanged));
        }

        if (Leftovers.Count == 0)
        {
            Finish(Loc.T("uninstall.done_clean"));
            return;
        }

        Phase = UninstallPhase.Leftovers;
        ProgressText = "";
        OnSelectionChanged();
    }

    private async Task RemoveAsync()
    {
        var selected = Leftovers.Where(l => l.IsSelected).Select(l => l.Leftover).ToList();
        if (selected.Count == 0) return;
        if (!await _main.ConfirmAsync(Loc.T("uninstall.remove"), Loc.F("uninstall.remove_confirm", selected.Count, SelectedSize), Loc.T("uninstall.remove"), danger: true)) return;

        Phase = UninstallPhase.Removing;
        try
        {
            var progress = new Progress<string>(path => ProgressText = Loc.F("uninstall.removing", path));
            var result = await LeftoverRemover.RemoveAsync(selected, progress);
            BackupFolder = result.BackupFolder;
            Finish(result.Failed > 0
                ? Loc.F("uninstall.removed_some", result.Removed, Format.Bytes(result.FreedBytes), result.Failed)
                : Loc.F("uninstall.removed_all", result.Removed, Format.Bytes(result.FreedBytes)));
        }
        catch (Exception ex)
        {
            Log.Error("Xóa phần còn sót lỗi", ex);
            Finish(Loc.F("error.unexpected", ex.Message));
        }
    }

    private void Finish(string message)
    {
        ResultText = message;
        ProgressText = "";
        Phase = UninstallPhase.Done;
        _main.ShowToast(message);
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(Title));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void SetAll(bool value)
    {
        foreach (var item in Leftovers) item.IsSelected = value;
    }

    private static void Open(LeftoverItem? item)
    {
        if (item is null || !item.CanOpen) return;
        var path = item.Leftover.Path;
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
