using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Partitions;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

/// <summary>Một ô trên sơ đồ ổ đĩa: phân vùng hoặc vùng trống.</summary>
public sealed class SegmentItem : ObservableObject
{
    private bool _isSelected;

    public SegmentItem(PhysicalDiskInfo disk, PartitionInfo? partition, FreeRegion? free)
    {
        Disk = disk;
        Partition = partition;
        Free = free;
        Size = partition?.Size ?? free?.Size ?? 0;
        SizeText = Format.Bytes(Size);

        if (partition is { } p)
        {
            KindKey = p.IsWindows ? "windows" : p.Kind.ToString().ToLowerInvariant();
            KindText = Loc.T("part.kind." + p.Kind.ToString().ToLowerInvariant());
            Title = p.DriveLetter is { } l
                ? string.IsNullOrWhiteSpace(p.Label) ? $"{l}:" : $"{l}: {p.Label}"
                : string.IsNullOrWhiteSpace(p.Label) ? KindText : p.Label;
            FileSystem = p.FileSystem.Length > 0 ? p.FileSystem : p.Kind is PartitionKind.Reserved or PartitionKind.Extended ? "" : "RAW";
            if (p.UsedBytes is { } used && p.Size > 0 && p.FileSystem.Length > 0)
            {
                UsedPercent = 100.0 * used / p.Size;
                UsedText = Format.Bytes(used);
                FreeText = Format.Bytes(p.FreeBytes ?? 0);
            }

            var flags = new List<string>();
            if (p.IsWindows) flags.Add(Loc.T("part.flag.windows"));
            if (p.IsBootLoader) flags.Add(Loc.T("part.flag.boot"));
            if (p.IsActive) flags.Add(Loc.T("part.flag.active"));
            if (p.HasPageFile) flags.Add(Loc.T("part.flag.pagefile"));
            if (p.IsHidden) flags.Add(Loc.T("part.flag.hidden"));
            if (p.IsLogical) flags.Add(Loc.T("part.flag.logical"));
            Flags = string.Join(" · ", flags);
            Number = "#" + p.PartitionNumber.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            KindKey = "free";
            KindText = Loc.T("part.kind.free");
            Title = KindText;
            FileSystem = "";
            Number = "";
        }

        Short = partition?.DriveLetter is { } letter ? $"{letter}:" : FileSystem.Length > 0 ? FileSystem : partition is null ? "" : KindText;
        Tooltip = string.Join("\n", new[] { Title, KindText, FileSystem, SizeText, Flags }.Where(s => !string.IsNullOrEmpty(s)).Distinct());
    }

    public PhysicalDiskInfo Disk { get; }
    public PartitionInfo? Partition { get; }
    public FreeRegion? Free { get; }
    public bool IsFree => Free is not null;
    public long Size { get; }

    /// <summary>Trọng số chiều rộng trên sơ đồ.</summary>
    public double Weight => Math.Max(1, Size / (double)PartitionLayout.MiB);

    /// <summary>Khóa màu: windows, data, efi, reserved, recovery, extended, dynamic, other, free.</summary>
    public string KindKey { get; }

    public string KindText { get; }
    public string Title { get; }
    public string Short { get; }
    public string Number { get; }
    public string SizeText { get; }
    public string FileSystem { get; }
    public double UsedPercent { get; }
    public bool HasUsage => UsedText.Length > 0;
    public string UsedText { get; } = "";
    public string FreeText { get; } = "";
    public string Flags { get; }
    public string Tooltip { get; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool Matches(SegmentItem? other) =>
        other is not null && other.Disk.Number == Disk.Number
        && (Partition is { } p ? other.Partition?.Offset == p.Offset : Free is { } f && other.Free is { } of && Math.Abs(of.Offset - f.Offset) < 64 * PartitionLayout.MiB);
}

/// <summary>Một ổ cứng trên trang Phân vùng.</summary>
public sealed class DiskItem
{
    public DiskItem(PhysicalDiskInfo disk)
    {
        Disk = disk;
        Title = Loc.F("part.disk_title", disk.Number, disk.Name);
        var style = disk.Style switch
        {
            PartitionStyle.Gpt => "GPT",
            PartitionStyle.Mbr => "MBR",
            _ => Loc.T("part.style.raw"),
        };
        Detail = string.Join(" · ", new[] { Format.Bytes(disk.Size), style, disk.Bus }.Where(s => s.Length > 0));
        var free = disk.FreeRegions.Sum(r => r.Size);
        Summary = disk.Style == PartitionStyle.Raw
            ? Loc.T("part.raw_hint")
            : Loc.F("part.disk_summary", disk.Partitions.Count(p => p.Kind != PartitionKind.Extended), Format.Bytes(free));

        foreach (var segment in disk.Partitions.Where(p => p.Kind != PartitionKind.Extended).Select(p => new SegmentItem(disk, p, null))
                     .Concat(disk.FreeRegions.Select(r => new SegmentItem(disk, null, r)))
                     .OrderBy(s => s.Partition?.Offset ?? s.Free!.Offset))
        {
            Segments.Add(segment);
        }

        Badges = new[]
        {
            disk.IsSystemDisk ? Loc.T("part.badge.system") : null,
            disk.IsRemovable ? Loc.T("part.badge.removable") : null,
            disk.IsOffline ? Loc.T("part.badge.offline") : null,
            disk.IsReadOnly ? Loc.T("part.badge.readonly") : null,
        }.OfType<string>().ToList();
    }

    public PhysicalDiskInfo Disk { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Summary { get; }
    public List<string> Badges { get; }
    public ObservableCollection<SegmentItem> Segments { get; } = [];
    public bool IsRaw => Disk.Style == PartitionStyle.Raw;
}

/// <summary>Nút thao tác trong bảng bên phải.</summary>
public sealed class PartitionAction(string key, string glyph, string label, string? blocker, bool danger, RelayCommand command)
{
    public string Key { get; } = key;
    public string Glyph { get; } = glyph;
    public string Label { get; } = label;
    public bool IsEnabled { get; } = blocker is null;
    public string Reason { get; } = blocker is null ? "" : Loc.T(blocker);
    public bool IsDanger { get; } = danger;
    public RelayCommand Command { get; } = command;
}

public sealed class LetterOption(char letter, Action<LetterOption> select) : ObservableObject
{
    private bool _isSelected;

    public char Letter { get; } = letter;
    public string Text => Letter == '\0' ? Loc.T("part.letter_auto") : $"{Letter}:";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value) && value) select(this);
        }
    }
}

public enum PartitionEditor
{
    None,
    Shrink,
    Extend,
    Create,
    Format,
    Letter,
    Label,
    Merge,
}

/// <summary>
/// Trang Phân vùng: sơ đồ từng ổ cứng, chọn phân vùng / vùng trống rồi thao tác (co, mở rộng, tạo, xóa, format, gộp,
/// đổi ký tự, đổi nhãn, chuyển NTFS, MBR→GPT...). Mọi thao tác chạy qua công cụ của Windows và đều hỏi xác nhận.
/// </summary>
public sealed class PartitionViewModel : PageViewModel
{
    private List<PhysicalDiskInfo> _disks = [];
    private SegmentItem? _selected;
    private bool _loaded;
    private bool _isLoading;
    private bool _isBusy;
    private string _busyText = "";
    private PartitionEditor _editor;
    private string _sizeText = "";
    private double _sizeGb;
    private double _sizeMinGb;
    private double _sizeMaxGb;
    private bool _syncing;
    private string _fileSystem = "NTFS";
    private string _labelText = "";
    private bool _fullFormat;
    private bool _mergeCopy;
    private LetterOption? _selectedLetter;
    private string _editorError = "";
    private string _resultText = "";
    private bool _resultIsError;
    private PartitionSizeRange? _range;
    private MergePlan? _mergePlan;
    private ExtendPlan? _extendPlan;

    public PartitionViewModel(MainViewModel main) : base(main)
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading && !IsBusy);
        SelectCommand = new RelayCommand(p => Select(p as SegmentItem));
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => !IsBusy && Editor != PartitionEditor.None);
        CancelEditCommand = new RelayCommand(CloseEditor);
        SetFileSystemCommand = new RelayCommand(p => FileSystem = p as string ?? "NTFS");
        MaxSizeCommand = new RelayCommand(() => SizeGb = SizeMaxGb);
        MinSizeCommand = new RelayCommand(() => SizeGb = SizeMinGb);
        InitGptCommand = new AsyncRelayCommand(p => InitializeAsync(p as DiskItem, PartitionStyle.Gpt), _ => !IsBusy);
        InitMbrCommand = new AsyncRelayCommand(p => InitializeAsync(p as DiskItem, PartitionStyle.Mbr), _ => !IsBusy);
        OpenDiskManagementCommand = new RelayCommand(() => FixService.OpenUri("diskmgmt.msc"));
    }

    public ObservableCollection<DiskItem> Disks { get; } = [];
    public ObservableCollection<PartitionAction> Actions { get; } = [];
    public ObservableCollection<PartitionAction> DiskActions { get; } = [];
    public ObservableCollection<LetterOption> Letters { get; } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand SelectCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public RelayCommand CancelEditCommand { get; }
    public RelayCommand SetFileSystemCommand { get; }
    public RelayCommand MaxSizeCommand { get; }
    public RelayCommand MinSizeCommand { get; }
    public AsyncRelayCommand InitGptCommand { get; }
    public AsyncRelayCommand InitMbrCommand { get; }
    public RelayCommand OpenDiskManagementCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (Set(ref _isLoading, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(ShowActions));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }

    public bool IsAdmin => Main.IsAdmin;

    // ---------------- Lựa chọn ----------------

    public SegmentItem? Selected
    {
        get => _selected;
        private set
        {
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;
            if (_selected is not null) _selected.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(NoSelection));
            OnPropertyChanged(nameof(SelectedDetails));
        }
    }

    public bool HasSelection => _selected is not null;
    public bool NoSelection => _selected is null;
    public bool ShowActions => Editor == PartitionEditor.None && !IsBusy;

    /// <summary>Các dòng thông tin của mục đang chọn (nhãn – giá trị).</summary>
    public List<KeyValuePair<string, string>> SelectedDetails
    {
        get
        {
            var rows = new List<KeyValuePair<string, string>>();
            if (_selected is not { } s) return rows;
            void Add(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) rows.Add(new(Loc.T(key), value));
            }

            Add("part.detail.disk", Loc.F("part.disk_short", s.Disk.Number));
            Add("part.detail.type", s.KindText);
            Add("part.detail.size", s.SizeText);
            if (s.Partition is { } p)
            {
                Add("part.detail.number", p.PartitionNumber.ToString(CultureInfo.InvariantCulture));
                Add("part.detail.fs", s.FileSystem);
                Add("part.detail.used", s.UsedText);
                Add("part.detail.free", s.FreeText);
                Add("part.detail.flags", s.Flags);
            }

            var offset = s.Partition?.Offset ?? s.Free?.Offset ?? 0;
            Add("part.detail.offset", Format.Bytes(offset));
            return rows;
        }
    }

    private void Select(SegmentItem? segment)
    {
        if (segment is null || IsBusy) return;
        CloseEditor();
        ResultText = "";
        Selected = segment;
        BuildActions();
    }

    // ---------------- Tải dữ liệu ----------------

    public override void OnShown()
    {
        if (!_loaded && !IsLoading) _ = LoadSafeAsync();
    }

    private async Task LoadSafeAsync()
    {
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Đọc phân vùng lỗi", ex);
            Main.ShowToast(Loc.F("error.unexpected", ex.Message), true);
        }
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            _disks = await PartitionService.ReadAsync();
            _loaded = true;
            Rebuild();
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected override void Rebuild()
    {
        var previous = _selected;
        Disks.Clear();
        SegmentItem? reselect = null;
        foreach (var disk in _disks)
        {
            var item = new DiskItem(disk);
            Disks.Add(item);
            reselect ??= item.Segments.FirstOrDefault(s => s.Matches(previous));
        }

        OnPropertyChanged(nameof(IsEmpty));
        CloseEditor();
        Selected = reselect;
        BuildActions();
    }

    public bool IsEmpty => _loaded && Disks.Count == 0;

    // ---------------- Danh sách thao tác ----------------

    private void BuildActions()
    {
        Actions.Clear();
        DiskActions.Clear();
        if (_selected is not { } s) return;
        var disk = s.Disk;

        if (s.Partition is { } p)
        {
            Add(Actions, "shrink", "", PartitionOp.Shrink, false, () => _ = OpenResizeAsync(shrink: true));
            Add(Actions, "extend", "", PartitionOp.Extend, false, () => _ = OpenResizeAsync(shrink: false));
            Add(Actions, "merge", "", PartitionOp.Merge, true, OpenMerge);
            if (PartitionLayout.PlanMerge(disk, p, out var plan) is null && plan is not null)
            {
                var last = Actions[^1];
                Actions[^1] = new PartitionAction(last.Key, last.Glyph, Loc.F("part.action.merge_into", Name(plan.Removed), Name(plan.Keep)), null, true, last.Command);
            }

            Add(Actions, "letter", "", PartitionOp.ChangeLetter, false, OpenLetter);
            Add(Actions, "remove_letter", "", PartitionOp.RemoveLetter, false, () => _ = RemoveLetterAsync());
            Add(Actions, "label", "", PartitionOp.Label, false, OpenLabel);
            Add(Actions, "convert_ntfs", "", PartitionOp.ConvertToNtfs, false, () => _ = ConvertNtfsAsync());
            Add(Actions, "check", "", PartitionOp.CheckFileSystem, false, () => _ = Main.CheckDiskAsync(p.DriveLetter?.ToString()));
            if (p.DriveLetter is { } letter) Actions.Add(new PartitionAction("open", "", Loc.T("part.action.open"), null, false, new RelayCommand(() => FixService.OpenUri($"{letter}:\\"))));
            Add(Actions, "format", "", PartitionOp.Format, true, OpenFormat);
            Add(Actions, "delete", "", PartitionOp.Delete, true, () => _ = DeleteAsync());

            void Add(ObservableCollection<PartitionAction> list, string key, string glyph, PartitionOp op, bool danger, Action run) =>
                list.Add(new PartitionAction(key, glyph, Loc.T("part.action." + key), PartitionLayout.Blocker(disk, p, op), danger, new RelayCommand(run)));
        }
        else if (s.Free is not null)
        {
            var blocker = disk.IsOffline ? "part.block.offline" : disk.IsReadOnly ? "part.block.readonly" : null;
            Actions.Add(new PartitionAction("create", "", Loc.T("part.action.create"), blocker, false, new RelayCommand(OpenCreate)));
        }

        // Thao tác cấp ổ đĩa
        if (disk.IsOffline || disk.IsReadOnly)
        {
            DiskActions.Add(new PartitionAction("online", "", Loc.T("part.action.online"), null, false, new RelayCommand(() => _ = SetOnlineAsync(disk))));
        }

        if (disk.Style != PartitionStyle.Raw && disk.Partitions.Count == 0)
        {
            var target = disk.Style == PartitionStyle.Gpt ? PartitionStyle.Mbr : PartitionStyle.Gpt;
            DiskActions.Add(new PartitionAction("convert_style", "", Loc.F("part.action.convert_style", target.ToString().ToUpperInvariant()), null, false,
                new RelayCommand(() => _ = ConvertEmptyAsync(disk, target))));
        }
        else if (disk.Style == PartitionStyle.Mbr)
        {
            var blocker = disk.IsSystemDisk ? null : "part.block.mbr2gpt_data";
            DiskActions.Add(new PartitionAction("mbr2gpt", "", Loc.T("part.action.mbr2gpt"), blocker, true, new RelayCommand(() => _ = Mbr2GptAsync(disk))));
        }

        if (disk.IsSystemDisk)
        {
            DiskActions.Add(new PartitionAction("repair_boot", "", Loc.T("part.action.repair_boot"), null, false, new RelayCommand(() => _ = RepairBootAsync())));
        }
    }

    // ---------------- Trình chỉnh sửa (nhập kích thước, hệ thống tệp, nhãn...) ----------------

    public PartitionEditor Editor
    {
        get => _editor;
        private set
        {
            if (!Set(ref _editor, value)) return;
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(ShowActions));
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(EditorHint));
            OnPropertyChanged(nameof(ShowSize));
            OnPropertyChanged(nameof(ShowFileSystem));
            OnPropertyChanged(nameof(ShowLabel));
            OnPropertyChanged(nameof(ShowLetters));
            OnPropertyChanged(nameof(ShowFullFormat));
            OnPropertyChanged(nameof(ShowMerge));
            OnPropertyChanged(nameof(ApplyIsDanger));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsEditing => Editor != PartitionEditor.None;
    public bool ShowSize => Editor is PartitionEditor.Shrink or PartitionEditor.Extend or PartitionEditor.Create;
    public bool ShowFileSystem => Editor is PartitionEditor.Create or PartitionEditor.Format;
    public bool ShowLabel => Editor is PartitionEditor.Create or PartitionEditor.Format or PartitionEditor.Label;
    public bool ShowLetters => Editor is PartitionEditor.Create or PartitionEditor.Letter;
    public bool ShowFullFormat => Editor is PartitionEditor.Create or PartitionEditor.Format;
    public bool ShowMerge => Editor == PartitionEditor.Merge;
    public bool ApplyIsDanger => Editor is PartitionEditor.Format or PartitionEditor.Merge;

    public string EditorTitle => Editor == PartitionEditor.None ? "" : Loc.T("part.action." + Editor.ToString().ToLowerInvariant());

    public string EditorHint => Editor switch
    {
        PartitionEditor.Shrink => Loc.T("part.hint.shrink"),
        PartitionEditor.Extend => _extendPlan is { Recoveries.Count: > 0 } ? Loc.T("part.hint.extend_recovery") : Loc.T("part.hint.extend"),
        PartitionEditor.Create => Loc.T("part.hint.create"),
        PartitionEditor.Format => Loc.T("part.hint.format"),
        PartitionEditor.Letter => Loc.T("part.hint.letter"),
        PartitionEditor.Label => "",
        PartitionEditor.Merge => MergeHint,
        _ => "",
    };

    public string SizeText
    {
        get => _sizeText;
        set
        {
            if (!Set(ref _sizeText, value) || _syncing) return;
            if (PartitionLayout.ParseGigabytes(value) is { } bytes)
            {
                _syncing = true;
                SizeGb = bytes / (double)PartitionLayout.GiB;
                _syncing = false;
            }

            OnPropertyChanged(nameof(SizeChangeText));
        }
    }

    public double SizeGb
    {
        get => _sizeGb;
        set
        {
            if (!Set(ref _sizeGb, value)) return;
            if (!_syncing)
            {
                _syncing = true;
                SizeText = Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
                _syncing = false;
            }

            OnPropertyChanged(nameof(SizeChangeText));
        }
    }

    public double SizeMinGb { get => _sizeMinGb; private set => Set(ref _sizeMinGb, value); }
    public double SizeMaxGb { get => _sizeMaxGb; private set => Set(ref _sizeMaxGb, value); }
    public string SizeRangeText => Loc.F("part.size_range", Format.Bytes(Bytes(SizeMinGb)), Format.Bytes(Bytes(SizeMaxGb)));

    /// <summary>"Giải phóng 50 GB" / "Thêm 20 GB" khi đổi cỡ.</summary>
    public string SizeChangeText
    {
        get
        {
            if (Editor is not (PartitionEditor.Shrink or PartitionEditor.Extend) || _selected?.Partition is not { } p) return "";
            var target = PartitionLayout.ParseGigabytes(SizeText) ?? 0;
            var diff = target - p.Size;
            return diff switch
            {
                < 0 => Loc.F("part.size_freed", Format.Bytes(-diff)),
                > 0 => Loc.F("part.size_added", Format.Bytes(diff)),
                _ => "",
            };
        }
    }

    public string FileSystem
    {
        get => _fileSystem;
        set
        {
            if (Set(ref _fileSystem, value)) OnPropertyChanged(nameof(LabelMax));
        }
    }

    public IReadOnlyList<string> FileSystems => PartitionLayout.FileSystems;
    public string LabelText { get => _labelText; set => Set(ref _labelText, value); }
    public int LabelMax => PartitionLayout.MaxLabelLength(Editor == PartitionEditor.Label ? _selected?.Partition?.FileSystem ?? "NTFS" : FileSystem);
    public bool FullFormat { get => _fullFormat; set => Set(ref _fullFormat, value); }
    public bool MergeCopy { get => _mergeCopy; set => Set(ref _mergeCopy, value); }
    public bool MergeCanCopy { get; private set; }
    public string EditorError { get => _editorError; private set => Set(ref _editorError, value); }

    /// <summary>Kết quả thao tác gần nhất (giữ lại sau khi đọc lại bảng phân vùng, để xem lỗi chi tiết).</summary>
    public string ResultText { get => _resultText; private set => Set(ref _resultText, value); }

    public bool ResultIsError { get => _resultIsError; private set => Set(ref _resultIsError, value); }

    private string MergeHint
    {
        get
        {
            if (_mergePlan is not { } plan) return "";
            var hint = Loc.F("part.hint.merge", Name(plan.Removed), Name(plan.Keep), Format.Bytes(plan.Removed.Size));
            return plan.Recoveries.Count > 0 ? hint + " " + Loc.F("part.hint.merge_recovery", Format.Bytes(plan.Recoveries.Sum(r => r.Size))) : hint;
        }
    }

    public string MergeCopyText => _mergePlan is { } plan
        ? Loc.F("part.merge_copy", Name(plan.Removed), PartitionCommands.MergeFolderName(plan.Removed.DriveLetter ?? 'X', Loc.T("part.merge_folder")), Name(plan.Keep))
        : "";

    public string MergeNoCopyText => _mergePlan is { } plan && !MergeCanCopy ? Loc.F("part.merge_no_copy", Name(plan.Removed)) : "";

    private static string Name(PartitionInfo p) =>
        p.DriveLetter is { } l ? $"{l}:" : Loc.F("part.partition_n", p.PartitionNumber);

    private static long Bytes(double gb) => PartitionLayout.AlignDown((long)(gb * PartitionLayout.GiB));

    private void SetRange(long min, long max, long value)
    {
        SizeMinGb = min / (double)PartitionLayout.GiB;
        SizeMaxGb = max / (double)PartitionLayout.GiB;
        SizeGb = value / (double)PartitionLayout.GiB;
        OnPropertyChanged(nameof(SizeRangeText));
    }

    private void CloseEditor()
    {
        EditorError = "";
        Editor = PartitionEditor.None;
    }

    private async Task OpenResizeAsync(bool shrink)
    {
        if (_selected?.Partition is not { } p || !EnsureAdmin()) return;
        _extendPlan = shrink ? null : PartitionLayout.PlanExtend(_selected.Disk, p);

        // Phân vùng khôi phục chắn phía sau: Windows chưa cho mở rộng nên tự tính giới hạn theo vùng trống sau nó.
        if (_extendPlan is { Recoveries.Count: > 0 } over)
        {
            var now = PartitionLayout.AlignDown(p.Size);
            var limit = over.MaxSize(p);
            SetRange(now, limit, limit);
            EditorError = "";
            Editor = PartitionEditor.Extend;
            OnPropertyChanged(nameof(EditorHint));
            return;
        }

        BeginBusy(Loc.T("part.busy.size"));
        try
        {
            _range = await PartitionService.GetSupportedSizeAsync(p.DiskNumber, p.PartitionNumber);
        }
        finally
        {
            EndBusy();
        }

        if (_range is not { } r)
        {
            Main.ShowToast(Loc.T("part.error.size_query"), true);
            return;
        }

        var current = PartitionLayout.AlignDown(p.Size);
        if (shrink)
        {
            var min = PartitionLayout.AlignUp(r.Min);
            if (current - min < PartitionLayout.MinNewPartition)
            {
                Main.ShowToast(Loc.T("part.error.cannot_shrink"), true);
                return;
            }

            SetRange(min, current, Math.Max(min, current - (current - min) / 2));
        }
        else
        {
            var max = PartitionLayout.AlignDown(r.Max);
            if (max - current < PartitionLayout.MiB)
            {
                Main.ShowToast(Loc.T("part.block.no_space_after"), true);
                return;
            }

            SetRange(current, max, max);
        }

        EditorError = "";
        Editor = shrink ? PartitionEditor.Shrink : PartitionEditor.Extend;
        OnPropertyChanged(nameof(EditorHint));
    }

    private void OpenCreate()
    {
        if (_selected?.Free is not { } free) return;
        SetRange(PartitionLayout.MinNewPartition, free.Size, free.Size);
        FileSystem = _selected.Disk.IsRemovable && free.Size <= PartitionLayout.Fat32MaxSize ? "FAT32" : _selected.Disk.IsRemovable ? "exFAT" : "NTFS";
        LabelText = "";
        FullFormat = false;
        BuildLetters(null, includeAuto: true);
        EditorError = "";
        Editor = PartitionEditor.Create;
    }

    private void OpenFormat()
    {
        if (_selected?.Partition is not { } p) return;
        FileSystem = PartitionLayout.FileSystems.FirstOrDefault(f => f.Equals(p.FileSystem, StringComparison.OrdinalIgnoreCase)) ?? "NTFS";
        if (PartitionLayout.FileSystemBlocker(FileSystem, p.Size) is not null) FileSystem = "NTFS";
        LabelText = p.Label;
        FullFormat = false;
        EditorError = "";
        Editor = PartitionEditor.Format;
    }

    private void OpenLetter()
    {
        if (_selected?.Partition is not { } p) return;
        BuildLetters(p.DriveLetter, includeAuto: false);
        EditorError = "";
        Editor = PartitionEditor.Letter;
    }

    private void OpenLabel()
    {
        if (_selected?.Partition is not { } p) return;
        LabelText = p.Label;
        EditorError = "";
        Editor = PartitionEditor.Label;
        OnPropertyChanged(nameof(LabelMax));
    }

    private void OpenMerge()
    {
        if (_selected?.Partition is not { } p) return;
        if (PartitionLayout.PlanMerge(_selected.Disk, p, out var plan) is not null || plan is null) return;
        _mergePlan = plan;
        MergeCanCopy = PartitionLayout.CanCopyForMerge(plan.Keep, plan.Removed);
        MergeCopy = MergeCanCopy;
        OnPropertyChanged(nameof(MergeCanCopy));
        OnPropertyChanged(nameof(MergeCopyText));
        OnPropertyChanged(nameof(MergeNoCopyText));
        EditorError = "";
        Editor = PartitionEditor.Merge;
        OnPropertyChanged(nameof(EditorHint));
    }

    private void BuildLetters(char? current, bool includeAuto)
    {
        Letters.Clear();
        _selectedLetter = null;
        var used = new List<char>();
        try
        {
            used.AddRange(DriveInfo.GetDrives().Select(d => d.Name[0]));
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc danh sách ký tự ổ lỗi", ex);
        }

        used.AddRange(_disks.SelectMany(d => d.Partitions).Select(p => p.DriveLetter).OfType<char>());
        if (includeAuto) Letters.Add(new LetterOption('\0', SelectLetter));
        foreach (var letter in PartitionLayout.FreeLetters(used)) Letters.Add(new LetterOption(letter, SelectLetter));
        var first = Letters.FirstOrDefault(l => l.Letter != current);
        if (first is not null) first.IsSelected = true;
    }

    private void SelectLetter(LetterOption option)
    {
        _selectedLetter = option;
        foreach (var other in Letters.Where(l => !ReferenceEquals(l, option))) other.IsSelected = false;
    }

    // ---------------- Thực hiện ----------------

    private async Task ApplyAsync()
    {
        if (_selected is not { } s) return;
        EditorError = "";
        switch (Editor)
        {
            case PartitionEditor.Shrink:
            case PartitionEditor.Extend:
                await ApplyResizeAsync(s);
                break;
            case PartitionEditor.Create:
                await ApplyCreateAsync(s);
                break;
            case PartitionEditor.Format:
                await ApplyFormatAsync(s);
                break;
            case PartitionEditor.Letter:
                await ApplyLetterAsync(s);
                break;
            case PartitionEditor.Label:
                await ApplyLabelAsync(s);
                break;
            case PartitionEditor.Merge:
                await ApplyMergeAsync(s);
                break;
        }
    }

    private long? ValidatedSize()
    {
        var bytes = PartitionLayout.ParseGigabytes(SizeText);
        var min = Bytes(SizeMinGb);
        var max = PartitionLayout.AlignDown((long)Math.Round(SizeMaxGb * PartitionLayout.GiB));
        if (bytes is null)
        {
            EditorError = Loc.T("part.error.size_invalid");
            return null;
        }

        // Cho phép sai số làm tròn khi người dùng nhập đúng số tối đa/tối thiểu đang hiện.
        if (bytes > max && bytes - max < 16 * PartitionLayout.MiB) bytes = max;
        if (bytes < min && min - bytes < 16 * PartitionLayout.MiB) bytes = min;
        if (bytes < min || bytes > max)
        {
            EditorError = SizeRangeText;
            return null;
        }

        return bytes;
    }

    private async Task ApplyResizeAsync(SegmentItem s)
    {
        if (s.Partition is not { } p || ValidatedSize() is not { } size) return;
        if (Math.Abs(size - p.Size) < PartitionLayout.MiB)
        {
            CloseEditor();
            return;
        }

        var shrink = size < p.Size;
        if (!shrink && _extendPlan is { Recoveries.Count: > 0 } over)
        {
            var warn = Loc.F("part.confirm.extend_recovery", Name(p), Format.Bytes(p.Size), Format.Bytes(size));
            if (!await Main.ConfirmAsync(EditorTitle, warn, Loc.T("part.apply"), danger: true)) return;
            var stages = new Progress<string>(stage => BusyText = Loc.T($"part.busy.merge_{stage}"));
            await RunAsync(Loc.T("part.busy.merge_recovery"), () => PartitionService.ExtendOverRecoveryAsync(p, over.Recoveries, size, stages), Loc.T("part.done.resize"));
            return;
        }

        var message = shrink
            ? Loc.F("part.confirm.shrink", Name(p), Format.Bytes(p.Size), Format.Bytes(size))
            : Loc.F("part.confirm.extend", Name(p), Format.Bytes(p.Size), Format.Bytes(size));
        if (!await Main.ConfirmAsync(EditorTitle, message, Loc.T("part.apply"))) return;
        await RunAsync(Loc.T(shrink ? "part.busy.shrink" : "part.busy.extend"), () => PartitionService.ResizeAsync(p.DiskNumber, p.PartitionNumber, size), Loc.T("part.done.resize"));
    }

    private async Task ApplyCreateAsync(SegmentItem s)
    {
        if (s.Free is not { } free || ValidatedSize() is not { } size) return;
        if (PartitionLayout.FileSystemBlocker(FileSystem, size) is { } fsError)
        {
            EditorError = Loc.T(fsError);
            return;
        }

        var letter = _selectedLetter is { Letter: not '\0' } l ? l.Letter : (char?)null;
        var label = PartitionLayout.SanitizeLabel(LabelText, FileSystem);
        var message = Loc.F("part.confirm.create", Format.Bytes(size), FileSystem, Loc.F("part.disk_short", s.Disk.Number));
        if (!await Main.ConfirmAsync(EditorTitle, message, Loc.T("part.apply"))) return;
        var offset = PartitionLayout.AlignUp(free.Offset);
        size = Math.Min(size, PartitionLayout.AlignDown(free.End - offset));
        await RunAsync(Loc.T("part.busy.create"), () => PartitionService.CreateAsync(s.Disk.Number, offset, size, letter, FileSystem, label, FullFormat), Loc.T("part.done.create"));
    }

    private async Task ApplyFormatAsync(SegmentItem s)
    {
        if (s.Partition is not { } p) return;
        if (PartitionLayout.FileSystemBlocker(FileSystem, p.Size) is { } fsError)
        {
            EditorError = Loc.T(fsError);
            return;
        }

        var label = PartitionLayout.SanitizeLabel(LabelText, FileSystem);
        var message = Loc.F("part.confirm.format", Name(p), FileSystem, UsageText(p));
        if (!await Main.ConfirmAsync(EditorTitle, message, Loc.T("part.action.format"), danger: true)) return;
        await RunAsync(Loc.T("part.busy.format"), () => PartitionService.FormatAsync(p.DiskNumber, p.PartitionNumber, FileSystem, label, FullFormat), Loc.T("part.done.format"));
    }

    private async Task ApplyLetterAsync(SegmentItem s)
    {
        if (s.Partition is not { } p || _selectedLetter is not { Letter: not '\0' } option) return;
        var message = Loc.F("part.confirm.letter", Name(p), $"{option.Letter}:");
        if (!await Main.ConfirmAsync(EditorTitle, message, Loc.T("part.apply"))) return;
        await RunAsync(Loc.T("part.busy.generic"), () => PartitionService.ChangeLetterAsync(p.DiskNumber, p.PartitionNumber, p.DriveLetter, option.Letter), Loc.T("part.done.generic"));
    }

    private async Task ApplyLabelAsync(SegmentItem s)
    {
        if (s.Partition is not { } p) return;
        var label = PartitionLayout.SanitizeLabel(LabelText, p.FileSystem);
        await RunAsync(Loc.T("part.busy.generic"), () => PartitionService.SetLabelAsync(p.DiskNumber, p.PartitionNumber, p.FileSystem, label), Loc.T("part.done.generic"));
    }

    private async Task ApplyMergeAsync(SegmentItem s)
    {
        if (s.Partition is null || _mergePlan is not { } plan) return;
        var keep = plan.Keep;
        var next = plan.Removed;
        var copy = MergeCopy && MergeCanCopy;
        var message = copy
            ? Loc.F("part.confirm.merge_copy", Name(next), Name(keep), UsageText(next))
            : Loc.F("part.confirm.merge", Name(next), Name(keep), UsageText(next));
        if (plan.Recoveries.Count > 0) message += " " + Loc.T("part.confirm.recovery_note");
        if (!await Main.ConfirmAsync(EditorTitle, message, Loc.T("part.action.merge"), danger: true)) return;

        var progress = new Progress<string>(stage => BusyText = Loc.T($"part.busy.merge_{stage}"));
        await RunAsync(Loc.T("part.busy.merge_copy"),
            () => PartitionService.MergeAsync(plan, copy, Loc.T("part.merge_folder"), progress),
            Loc.T("part.done.merge"));
    }

    private static string UsageText(PartitionInfo p) =>
        p.UsedBytes is { } used ? Loc.F("part.usage", Format.Bytes(used)) : Loc.T("part.usage_unknown");

    private async Task DeleteAsync()
    {
        if (_selected?.Partition is not { } p || !EnsureAdmin()) return;
        var key = p.Kind == PartitionKind.Recovery ? "part.confirm.delete_recovery"
            : p.Kind is PartitionKind.Efi or PartitionKind.Reserved ? "part.confirm.delete_special"
            : "part.confirm.delete";
        if (!await Main.ConfirmAsync(Loc.T("part.action.delete"), Loc.F(key, Name(p), Format.Bytes(p.Size), UsageText(p)), Loc.T("part.action.delete"), danger: true)) return;
        await RunAsync(Loc.T("part.busy.delete"), () => PartitionService.DeleteAsync(p.DiskNumber, p.PartitionNumber), Loc.T("part.done.delete"));
    }

    private async Task RemoveLetterAsync()
    {
        if (_selected?.Partition is not { DriveLetter: { } letter } p || !EnsureAdmin()) return;
        if (!await Main.ConfirmAsync(Loc.T("part.action.remove_letter"), Loc.F("part.confirm.remove_letter", $"{letter}:"), Loc.T("part.apply"))) return;
        await RunAsync(Loc.T("part.busy.generic"), () => PartitionService.RemoveLetterAsync(p.DiskNumber, p.PartitionNumber, letter), Loc.T("part.done.generic"));
    }

    private async Task ConvertNtfsAsync()
    {
        if (_selected?.Partition is not { DriveLetter: { } letter } p || !EnsureAdmin()) return;
        if (!await Main.ConfirmAsync(Loc.T("part.action.convert_ntfs"), Loc.F("part.confirm.convert_ntfs", $"{letter}:", p.FileSystem), Loc.T("part.apply"))) return;
        await RunAsync(Loc.T("part.busy.convert_ntfs"), () => PartitionService.ConvertToNtfsAsync(letter, p.Label), Loc.T("part.done.convert_ntfs"));
    }

    private async Task InitializeAsync(DiskItem? item, PartitionStyle style)
    {
        if (item is null || !EnsureAdmin()) return;
        var disk = item.Disk;
        if (!await Main.ConfirmAsync(Loc.T("part.action.initialize"), Loc.F("part.confirm.initialize", disk.Number, disk.Name, style.ToString().ToUpperInvariant()), Loc.T("part.apply"))) return;
        await RunAsync(Loc.T("part.busy.generic"), () => PartitionService.InitializeAsync(disk.Number, style), Loc.T("part.done.initialize"));
    }

    private async Task ConvertEmptyAsync(PhysicalDiskInfo disk, PartitionStyle style)
    {
        if (!EnsureAdmin()) return;
        var name = style.ToString().ToUpperInvariant();
        if (!await Main.ConfirmAsync(Loc.F("part.action.convert_style", name), Loc.F("part.confirm.convert_style", disk.Number, name), Loc.T("part.apply"))) return;
        await RunAsync(Loc.T("part.busy.generic"), () => PartitionService.ConvertEmptyAsync(disk.Number, style), Loc.T("part.done.generic"));
    }

    private async Task SetOnlineAsync(PhysicalDiskInfo disk)
    {
        if (!EnsureAdmin()) return;
        await RunAsync(Loc.T("part.busy.generic"), () => PartitionService.SetOnlineAsync(disk.Number), Loc.T("part.done.generic"));
    }

    private async Task Mbr2GptAsync(PhysicalDiskInfo disk)
    {
        if (!EnsureAdmin()) return;
        if (!await Main.ConfirmAsync(Loc.T("part.action.mbr2gpt"), Loc.F("part.confirm.mbr2gpt_validate", disk.Number), Loc.T("part.validate"))) return;

        BeginBusy(Loc.T("part.busy.mbr2gpt_validate"));
        PartitionOpResult validate;
        try
        {
            validate = await PartitionService.Mbr2GptValidateAsync(disk.Number);
        }
        finally
        {
            EndBusy();
        }

        if (!validate.Success)
        {
            ResultText = ErrorText(validate, "part.error.mbr2gpt_validate");
            ResultIsError = true;
            Main.ShowToast(ResultText, true);
            return;
        }

        if (!await Main.ConfirmAsync(Loc.T("part.action.mbr2gpt"), Loc.F("part.confirm.mbr2gpt", disk.Number), Loc.T("part.convert"), danger: true)) return;
        await RunAsync(Loc.T("part.busy.mbr2gpt"), () => PartitionService.Mbr2GptConvertAsync(disk.Number), Loc.T("part.done.mbr2gpt"));
    }

    private async Task RepairBootAsync()
    {
        if (!EnsureAdmin()) return;
        if (!await Main.ConfirmAsync(Loc.T("part.action.repair_boot"), Loc.T("part.confirm.repair_boot"), Loc.T("part.apply"))) return;
        await RunAsync(Loc.T("part.busy.repair_boot"), () => PartitionService.RepairBootAsync(), Loc.T("part.done.repair_boot"));
    }

    private bool EnsureAdmin()
    {
        if (Main.IsAdmin) return true;
        Main.ShowToast(Loc.T("part.need_admin"), true);
        return false;
    }

    private void BeginBusy(string text)
    {
        BusyText = text;
        IsBusy = true;
        Main.ActivityText = text;
    }

    private void EndBusy()
    {
        IsBusy = false;
        BusyText = "";
        Main.ActivityText = "";
    }

    /// <summary>Chạy thao tác, báo kết quả rồi đọc lại bảng phân vùng.</summary>
    private async Task RunAsync(string busyText, Func<Task<PartitionOpResult>> work, string doneText)
    {
        if (!EnsureAdmin()) return;
        BeginBusy(busyText);
        PartitionOpResult result;
        try
        {
            result = await work();
        }
        catch (Exception ex)
        {
            Log.Error("Thao tác phân vùng lỗi", ex);
            result = new PartitionOpResult(false, ex.Message);
        }
        finally
        {
            EndBusy();
        }

        var message = !result.Success ? ErrorText(result, "part.failed")
            : result.Detail == PartitionService.WinReNotRestored ? doneText + " " + Loc.T("part.winre_not_restored")
            : doneText;
        Main.ShowToast(message, !result.Success);
        await LoadSafeAsync();
        ResultText = message;
        ResultIsError = !result.Success;
    }

    private static string ErrorText(PartitionOpResult result, string key) => result.Detail switch
    {
        "busy" => Loc.T("part.busy"),
        "label" => Loc.T("part.error.label_console"),
        "cancelled" => Loc.T("part.cancelled"),
        _ => Loc.F(key, string.IsNullOrWhiteSpace(result.Detail) ? "?" : result.Detail),
    };
}
