using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Analysis;
using PcCheck.Core.Cleanup;
using PcCheck.Core.Collectors;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

public sealed class JunkItem : ObservableObject
{
    private bool _isSelected;
    private readonly Action _onChanged;

    public JunkItem(JunkCategory category, bool selected, Action onChanged)
    {
        Category = category;
        _isSelected = selected;
        _onChanged = onChanged;
        Name = category.Name.ToString();
        Description = category.Description.ToString();
        Size = Format.Bytes(category.Bytes);
        Files = Loc.F("junk.files_count", category.Files);
    }

    public JunkCategory Category { get; }
    public string Key => Category.Key;
    public string Name { get; }
    public string Description { get; }
    public string Size { get; }
    public string Files { get; }
    public bool IsEmpty => Category.Bytes == 0;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value)) _onChanged();
        }
    }
}

public enum ProgramTag
{
    None,
    Essential,
    Unused,
    Bloat,
    Store,
}

public sealed class ProgramItem
{
    public ProgramItem(InstalledProgram p, DateTime now)
    {
        Program = p;
        Name = p.Name;
        Publisher = Format.OrDash(p.Publisher);
        Version = p.Version;
        Size = p.SizeBytes is { } s ? Format.Bytes(s) : "—";
        SizeBytes = p.SizeBytes ?? 0;
        Installed = Format.Date(p.InstallDate);
        var days = p.DaysSinceUsed(now);
        LastUsed = days is null ? Loc.T("software.no_usage") : days <= 1 ? Loc.T("software.used_recently") : Loc.F("software.days_ago", days);

        Tag = p.BloatReasonKey is not null ? (p.Scope == ProgramScope.Store ? ProgramTag.Store : ProgramTag.Bloat)
            : CleanupAnalyzer.IsUnused(p, now) ? ProgramTag.Unused
            : p.IsEssential ? ProgramTag.Essential
            : ProgramTag.None;
        TagText = Tag switch
        {
            ProgramTag.Bloat or ProgramTag.Store => Loc.T(p.BloatReasonKey!),
            ProgramTag.Unused => Loc.F("software.unused_days", days ?? 0),
            ProgramTag.Essential => Loc.T("software.essential"),
            _ => "",
        };
        Severity = Tag switch
        {
            ProgramTag.Bloat when p.BloatReasonKey == SoftwareCatalog.ReasonCrack => Severity.Critical,
            ProgramTag.Bloat => Severity.Warning,
            ProgramTag.Store or ProgramTag.Unused => Severity.Info,
            _ => Severity.Ok,
        };
        SearchText = $"{p.Name} {p.Publisher}";
    }

    public InstalledProgram Program { get; }
    public string Name { get; }
    public string Publisher { get; }
    public string Version { get; }
    public string Size { get; }
    public long SizeBytes { get; }
    public string Installed { get; }
    public string LastUsed { get; }
    public ProgramTag Tag { get; }
    public string TagText { get; }
    public Severity Severity { get; }
    public bool IsSuggested => Tag is ProgramTag.Bloat or ProgramTag.Store or ProgramTag.Unused;
    public string SearchText { get; }
}

public sealed class StartupRow : ObservableObject
{
    private readonly CleanupViewModel _owner;
    private bool _isEnabled;

    public StartupRow(StartupItem item, CleanupViewModel owner)
    {
        Item = item;
        _owner = owner;
        _isEnabled = item.Enabled;
        Location = Labels.StartupLocation(item.Location);
    }

    public StartupItem Item { get; }
    public string Name => Item.Name;
    public string Command => Item.Command;
    public string Publisher => Format.OrDash(Item.Publisher);
    public string Location { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            if (_owner.TrySetStartup(this, value))
            {
                Set(ref _isEnabled, value);
                _owner.NotifyStartupCount();
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }
}

/// <summary>Trang dọn dẹp: tệp rác, phần mềm ít dùng / rác, chương trình khởi động.</summary>
public sealed class CleanupViewModel : PageViewModel
{
    private int _selectedTab;
    private bool _isBusy;
    private string _busyText = "";
    private string _softwareFilter = "suggested";
    private string _search = "";
    private ICollectionView? _programs;
    private int _suggestedCount;
    private string _programsSummary = "";

    public CleanupViewModel(MainViewModel main) : base(main)
    {
        ScanJunkCommand = new AsyncRelayCommand(ScanJunkAsync, () => !IsBusy);
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => !IsBusy && SelectedBytes > 0);
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        UninstallCommand = new AsyncRelayCommand(p => UninstallAsync(p as ProgramItem), _ => !IsBusy);
        RefreshSoftwareCommand = new AsyncRelayCommand(RefreshSoftwareAsync, () => !IsBusy && HasResult);
        OpenTaskManagerCommand = new RelayCommand(() => FixService.OpenUri("ms-settings:startupapps"));
        DiskCleanupCommand = new AsyncRelayCommand(() => Main.RunToolAsync(SystemTools.DiskCleanup, false));
    }

    public int SelectedTab { get => _selectedTab; set => Set(ref _selectedTab, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }

    // ----- Tệp rác -----
    public ObservableCollection<JunkItem> JunkItems { get; } = [];
    public long SelectedBytes => JunkItems.Where(j => j.IsSelected).Sum(j => j.Category.Bytes);
    public string SelectedSize => Format.Bytes(SelectedBytes);
    public string TotalSize => Format.Bytes(JunkItems.Sum(j => j.Category.Bytes));
    public AsyncRelayCommand ScanJunkCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public AsyncRelayCommand DiskCleanupCommand { get; }

    // ----- Phần mềm -----
    public ICollectionView? Programs { get => _programs; private set => Set(ref _programs, value); }
    public int SuggestedCount { get => _suggestedCount; private set => Set(ref _suggestedCount, value); }
    public string ProgramsSummary { get => _programsSummary; private set => Set(ref _programsSummary, value); }
    public AsyncRelayCommand UninstallCommand { get; }
    public AsyncRelayCommand RefreshSoftwareCommand { get; }

    public string SoftwareFilter
    {
        get => _softwareFilter;
        set
        {
            if (Set(ref _softwareFilter, value)) Programs?.Refresh();
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value)) Programs?.Refresh();
        }
    }

    // ----- Khởi động -----
    public ObservableCollection<StartupRow> StartupItems { get; } = [];
    public int StartupEnabledCount => StartupItems.Count(s => s.IsEnabled);
    public RelayCommand OpenTaskManagerCommand { get; }

    protected override void Rebuild()
    {
        if (State.Result is not { } result) return;
        var previous = JunkItems.ToDictionary(j => j.Key, j => j.IsSelected);
        JunkItems.Clear();
        foreach (var category in result.Data.Junk.OrderByDescending(j => j.Bytes))
        {
            var selected = previous.TryGetValue(category.Key, out var s) ? s : category.DefaultSelected;
            JunkItems.Add(new JunkItem(category, selected && category.Bytes > 0, NotifySelection));
        }

        NotifySelection();

        var now = DateTime.Now;
        var items = result.Data.Programs.Select(p => new ProgramItem(p, now)).ToList();
        SuggestedCount = items.Count(i => i.IsSuggested);
        ProgramsSummary = Loc.F("software.summary", items.Count, Format.Bytes(items.Sum(i => i.SizeBytes)), SuggestedCount);
        var view = CollectionViewSource.GetDefaultView(items);
        view.Filter = FilterProgram;
        view.SortDescriptions.Add(new SortDescription(nameof(ProgramItem.Severity), ListSortDirection.Descending));
        view.SortDescriptions.Add(new SortDescription(nameof(ProgramItem.SizeBytes), ListSortDirection.Descending));
        if (SuggestedCount == 0 && _softwareFilter == "suggested") _softwareFilter = "all";
        OnPropertyChanged(nameof(SoftwareFilter));
        Programs = view;

        StartupItems.Clear();
        foreach (var item in result.Data.Startup) StartupItems.Add(new StartupRow(item, this));
        OnPropertyChanged(nameof(StartupEnabledCount));
    }

    private bool FilterProgram(object o)
    {
        if (o is not ProgramItem p) return false;
        if (_search.Length > 0 && !p.SearchText.Contains(_search, StringComparison.CurrentCultureIgnoreCase)) return false;
        return _softwareFilter switch
        {
            "suggested" => p.IsSuggested,
            "unused" => p.Tag == ProgramTag.Unused,
            "bloat" => p.Tag is ProgramTag.Bloat or ProgramTag.Store,
            _ => true,
        };
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(TotalSize));
    }

    private void SetAll(bool value)
    {
        foreach (var item in JunkItems.Where(j => !j.IsEmpty)) item.IsSelected = value;
    }

    private async Task ScanJunkAsync()
    {
        IsBusy = true;
        BusyText = Loc.T("junk.scanning");
        try
        {
            if (State.Result is null)
            {
                await Main.ScanAsync();
                return;
            }

            await State.ReanalyzeAsync(async data => data.Junk = await JunkScanner.ScanAsync());
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CleanAsync()
    {
        var selected = JunkItems.Where(j => j.IsSelected && !j.IsEmpty).ToList();
        if (selected.Count == 0) return;
        var message = Loc.F("junk.confirm", SelectedSize, string.Join(", ", selected.Select(s => s.Name)));
        if (!await Main.ConfirmAsync(Loc.T("junk.clean"), message, Loc.T("junk.clean"), danger: true)) return;

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(name => BusyText = Loc.F("junk.cleaning", name));
            var result = await JunkScanner.CleanAsync(selected.Select(s => s.Key), progress);
            BusyText = Loc.T("junk.scanning");
            await State.ReanalyzeAsync(async data => data.Junk = await JunkScanner.ScanAsync());
            Main.ShowToast(result.SkippedFiles > 0
                ? Loc.F("junk.cleaned_skipped", Format.Bytes(result.FreedBytes), result.SkippedFiles)
                : Loc.F("junk.cleaned", Format.Bytes(result.FreedBytes)));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UninstallAsync(ProgramItem? item)
    {
        if (item is null) return;
        var message = item.Program.Scope == ProgramScope.Store
            ? Loc.F("software.confirm_store", item.Name)
            : Loc.F("software.confirm", item.Name);
        if (!await Main.ConfirmAsync(Loc.T("software.uninstall"), message, Loc.T("software.uninstall"), danger: true)) return;

        var outcome = await FixService.UninstallAsync(item.Program);
        Main.ShowToast(outcome.Message.ToString(), !outcome.Success);
        if (item.Program.Scope == ProgramScope.Store && outcome.Success) await RefreshSoftwareAsync();
    }

    private async Task RefreshSoftwareAsync()
    {
        IsBusy = true;
        BusyText = Loc.T("software.refreshing");
        try
        {
            await State.ReanalyzeAsync(async data =>
            {
                data.Programs = await Task.Run(SoftwareCollector.Collect);
                data.Startup = await Task.Run(StartupCollector.Collect);
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal void NotifyStartupCount() => OnPropertyChanged(nameof(StartupEnabledCount));

    internal bool TrySetStartup(StartupRow row, bool enabled)
    {
        try
        {
            StartupCollector.SetEnabled(row.Item, enabled);
            Main.ShowToast(Loc.F(enabled ? "startup.enabled" : "startup.disabled", row.Name));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Đổi trạng thái khởi động {row.Name} lỗi", ex);
            Main.ShowToast(Loc.F("startup.failed", row.Name), true);
            return false;
        }
    }
}
