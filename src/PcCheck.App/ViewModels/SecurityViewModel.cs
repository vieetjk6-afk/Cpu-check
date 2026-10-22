using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Analysis;
using PcCheck.Core.Collectors;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

/// <summary>Nút hành động trên một mục bảo mật.</summary>
public sealed class SecurityActionItem(SecurityAction action)
{
    public SecurityAction Action { get; } = action;
    public string Label { get; } = action.Label.ToString();
}

/// <summary>Một phát hiện bảo mật đã dịch, bấm để xem cách xử lý.</summary>
public sealed class SecurityItemView : ObservableObject
{
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
    private bool _isExpanded;

    public SecurityItemView(SecurityItem item)
    {
        Item = item;
        Severity = item.Severity;
        Title = item.Title.ToString();
        Detail = item.Detail?.ToString() ?? "";
        Evidence = item.Evidence;
        Tags = item.Tags.Select(t => t.ToString()).Where(t => t.Length > 0).ToList();
        Actions = item.Actions.Select(a => new SecurityActionItem(a)).ToList();
        if (item.GuideKey is { } key)
        {
            var guide = IssueGuides.ForKey(key);
            Cause = guide.Cause.ToString();
            Steps = guide.Steps.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((text, i) => new StepItem(i + 1, text))
                .ToList();
        }

        _isExpanded = HasGuide && Expanded.Contains(item.Id);
    }

    public SecurityItem Item { get; }
    public Severity Severity { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Evidence { get; }
    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<SecurityActionItem> Actions { get; }
    public string Cause { get; } = "";
    public IReadOnlyList<StepItem> Steps { get; } = [];
    public bool HasGuide => Steps.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!Set(ref _isExpanded, value && HasGuide)) return;
            if (_isExpanded) Expanded.Add(Item.Id);
            else Expanded.Remove(Item.Id);
        }
    }
}

/// <summary>Một nhóm kiểm tra (phần mềm theo dõi, camera/micro, tự chạy...).</summary>
public sealed class SecuritySectionView : ObservableObject
{
    private bool _isExpanded;

    public SecuritySectionView(SecuritySectionKind kind, string glyph, IReadOnlyList<SecurityItemView> all, bool showAll, bool expanded)
    {
        Kind = kind;
        Glyph = glyph;
        var key = kind.ToString().ToLowerInvariant();
        Title = Loc.T($"sec.section.{key}");
        Description = Loc.T($"sec.section.{key}_desc");
        Items = showAll ? all : all.Where(i => i.Severity > Severity.Ok).ToList();
        CriticalCount = all.Count(i => i.Severity == Severity.Critical);
        WarningCount = all.Count(i => i.Severity == Severity.Warning);
        TotalCount = all.Count;
        Severity = all.Count == 0 ? Severity.Ok : all.Max(i => i.Severity);
        _isExpanded = expanded;
    }

    public SecuritySectionKind Kind { get; }
    public string Glyph { get; }
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<SecurityItemView> Items { get; }
    public int CriticalCount { get; }
    public int WarningCount { get; }
    public int TotalCount { get; }
    public Severity Severity { get; }
    public bool IsEmpty => Items.Count == 0;
    public string Summary => CriticalCount + WarningCount > 0
        ? Loc.F("sec.section.flagged", CriticalCount + WarningCount, TotalCount)
        : TotalCount > 0 ? Loc.F("sec.section.checked", TotalCount) : Loc.T("sec.section.nothing");

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
}

/// <summary>Trang Bảo mật: tìm dấu hiệu phần mềm theo dõi, điều khiển từ xa, mã độc và cấu hình bảo mật yếu.</summary>
public sealed class SecurityViewModel : PageViewModel
{
    private static readonly (SecuritySectionKind Kind, string Glyph)[] SectionGlyphs =
    [
        (SecuritySectionKind.Tools, ""),
        (SecuritySectionKind.DeviceAccess, ""),
        (SecuritySectionKind.Autostart, ""),
        (SecuritySectionKind.Processes, ""),
        (SecuritySectionKind.Network, ""),
        (SecuritySectionKind.Browser, ""),
        (SecuritySectionKind.Certificates, ""),
        (SecuritySectionKind.Defender, ""),
        (SecuritySectionKind.Access, ""),
        (SecuritySectionKind.System, ""),
    ];

    private readonly Dictionary<SecuritySectionKind, bool> _expanded = new();
    private IReadOnlyList<SecuritySectionView> _sections = [];
    private bool _showAll;
    private bool _isRescanning;
    private Severity _verdict;
    private string _verdictTitle = "";
    private string _verdictText = "";
    private int _criticalCount;
    private int _warningCount;
    private int _infoCount;

    public SecurityViewModel(MainViewModel main) : base(main)
    {
        QuickScanCommand = new AsyncRelayCommand(() => ScanAsync(DefenderScanType.Quick), () => !IsDefenderScanning);
        FullScanCommand = new AsyncRelayCommand(() => ScanAsync(DefenderScanType.Full), () => !IsDefenderScanning);
        StopScanCommand = new RelayCommand(() => _scanCts?.Cancel(), () => IsDefenderScanning);
        _scanTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _scanTimer.Tick += (_, _) => UpdateScanElapsed();
        OfflineScanCommand = new AsyncRelayCommand(() => Main.RunToolAsync(SystemTools.DefenderOffline, true));
        OpenDefenderCommand = new RelayCommand(() => FixService.OpenUri("windowsdefender:"));
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsRescanning);
        ActionCommand = new RelayCommand(p => _ = RunActionSafeAsync(p as SecurityActionItem));
        ExpandAllCommand = new RelayCommand(() => SetAll(true));
        CollapseAllCommand = new RelayCommand(() => SetAll(false));
    }

    public IReadOnlyList<SecuritySectionView> Sections { get => _sections; private set => Set(ref _sections, value); }

    /// <summary>Hiện cả mục bình thường (đã kiểm tra, không có gì đáng ngờ).</summary>
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (Set(ref _showAll, value)) Rebuild();
        }
    }

    public bool IsRescanning
    {
        get => _isRescanning;
        private set
        {
            if (Set(ref _isRescanning, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public Severity Verdict { get => _verdict; private set => Set(ref _verdict, value); }
    public string VerdictTitle { get => _verdictTitle; private set => Set(ref _verdictTitle, value); }
    public string VerdictText { get => _verdictText; private set => Set(ref _verdictText, value); }
    public int CriticalCount { get => _criticalCount; private set => Set(ref _criticalCount, value); }
    public int WarningCount { get => _warningCount; private set => Set(ref _warningCount, value); }
    public int InfoCount { get => _infoCount; private set => Set(ref _infoCount, value); }
    public bool HasReport => State.Result?.Security.Collected == true;

    // ---------- Quét virus trong ứng dụng ----------
    private readonly System.Windows.Threading.DispatcherTimer _scanTimer;
    private CancellationTokenSource? _scanCts;
    private DateTime _scanStarted;
    private DefenderScanType _scanType;
    private bool _isDefenderScanning;
    private string _scanStatus = "";
    private string _lastScanResult = "";
    private Severity _lastScanSeverity;

    public bool IsDefenderScanning
    {
        get => _isDefenderScanning;
        private set
        {
            if (Set(ref _isDefenderScanning, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    public string ScanStatus { get => _scanStatus; private set => Set(ref _scanStatus, value); }
    public string LastScanResult { get => _lastScanResult; private set => Set(ref _lastScanResult, value); }
    public Severity LastScanSeverity { get => _lastScanSeverity; private set => Set(ref _lastScanSeverity, value); }
    public RelayCommand StopScanCommand { get; }

    private void UpdateScanElapsed()
    {
        var elapsed = DateTime.Now - _scanStarted;
        var text = elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"mm\:ss");
        ScanStatus = Loc.F(_scanType == DefenderScanType.Quick ? "sec.scan.running_quick" : "sec.scan.running_full", text);
        Main.ActivityText = ScanStatus;
    }

    /// <summary>Quét bằng Microsoft Defender ngay trong ứng dụng: hiện thời gian đã chạy, cho phép dừng, báo kết quả.</summary>
    private async Task ScanAsync(DefenderScanType type)
    {
        if (IsDefenderScanning) return;
        _scanType = type;
        _scanStarted = DateTime.Now;
        _scanCts = new CancellationTokenSource();
        IsDefenderScanning = true;
        UpdateScanElapsed();
        _scanTimer.Start();
        try
        {
            var result = await DefenderScanner.RunAsync(type, _scanCts.Token);
            var minutes = Math.Max(1, (int)Math.Round(result.Duration.TotalMinutes));
            if (!result.Started)
            {
                LastScanSeverity = Severity.Warning;
                LastScanResult = Loc.T("fix.result.no_defender");
            }
            else if (result.Cancelled)
            {
                LastScanSeverity = Severity.Info;
                LastScanResult = Loc.F("sec.scan.cancelled", minutes);
            }
            else if (result.Failed)
            {
                // Quét không chạy được (thường do phần mềm diệt virus khác đã thay Defender) – không được báo "sạch".
                LastScanSeverity = Severity.Warning;
                LastScanResult = Loc.F("sec.scan.failed", $"0x{result.ExitCode:X8}");
            }
            else if (result.ThreatsReported)
            {
                LastScanSeverity = Severity.Critical;
                LastScanResult = result.Threats.Count > 0
                    ? Loc.F("sec.scan.threats", result.Threats.Count, string.Join(", ", result.Threats.Take(3)))
                    : Loc.T("sec.scan.threats_unnamed");
                await RescanAsync();
            }
            else
            {
                LastScanSeverity = Severity.Ok;
                LastScanResult = Loc.F("sec.scan.clean", DateTime.Now.ToString("t", Loc.Instance.Culture), minutes);
            }

            Main.ShowToast(LastScanResult, LastScanSeverity >= Severity.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("Quét Defender lỗi", ex);
            Main.ShowToast(Loc.F("error.unexpected", ex.Message), true);
        }
        finally
        {
            _scanTimer.Stop();
            IsDefenderScanning = false;
            ScanStatus = "";
            Main.ActivityText = "";
            _scanCts.Dispose();
            _scanCts = null;
        }
    }

    public AsyncRelayCommand QuickScanCommand { get; }
    public AsyncRelayCommand FullScanCommand { get; }
    public AsyncRelayCommand OfflineScanCommand { get; }
    public RelayCommand OpenDefenderCommand { get; }
    public AsyncRelayCommand RescanCommand { get; }
    public RelayCommand ActionCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }

    private void SetAll(bool expanded)
    {
        foreach (var section in Sections)
        {
            section.IsExpanded = expanded;
            _expanded[section.Kind] = expanded;
        }
    }

    protected override void Rebuild()
    {
        foreach (var section in Sections) _expanded[section.Kind] = section.IsExpanded;
        OnPropertyChanged(nameof(HasReport));

        var report = State.Result?.Security ?? SecurityReport.Empty;
        CriticalCount = report.Count(Severity.Critical);
        WarningCount = report.Count(Severity.Warning);
        InfoCount = report.Count(Severity.Info);
        Verdict = CriticalCount > 0 ? Severity.Critical : WarningCount > 0 ? Severity.Warning : Severity.Ok;
        VerdictTitle = CriticalCount > 0 ? Loc.F("sec.verdict.critical", CriticalCount)
            : WarningCount > 0 ? Loc.F("sec.verdict.warning", WarningCount)
            : Loc.T("sec.verdict.ok");
        VerdictText = Loc.T(CriticalCount > 0 ? "sec.verdict.critical_desc" : WarningCount > 0 ? "sec.verdict.warning_desc" : "sec.verdict.ok_desc");

        Sections = SectionGlyphs
            .Select(s =>
            {
                var items = report.Of(s.Kind).Select(i => new SecurityItemView(i)).ToList();
                var expanded = _expanded.TryGetValue(s.Kind, out var e) ? e : items.Any(i => i.Severity >= Severity.Warning);
                return new SecuritySectionView(s.Kind, s.Glyph, items, ShowAll, expanded);
            })
            .ToList();
    }

    /// <summary>Chỉ quét lại phần bảo mật (nhanh hơn quét toàn bộ).</summary>
    private async Task RescanAsync()
    {
        if (State.Result is null)
        {
            await Main.ScanAsync();
            return;
        }

        IsRescanning = true;
        try
        {
            await State.ReanalyzeAsync(async data =>
            {
                data.Security = await Task.Run(SecurityCollector.Collect);
                data.Startup = await Task.Run(StartupCollector.Collect);
            });
            Main.ShowToast(Loc.F("sec.rescanned", CriticalCount, WarningCount));
        }
        catch (Exception ex)
        {
            Log.Error("Quét lại bảo mật lỗi", ex);
            Main.ShowToast(Loc.F("error.unexpected", ex.Message), true);
        }
        finally
        {
            IsRescanning = false;
        }
    }

    private async Task RunActionSafeAsync(SecurityActionItem? item)
    {
        if (item is null) return;
        try
        {
            await RunActionAsync(item.Action);
        }
        catch (Exception ex)
        {
            Log.Error($"Hành động bảo mật {item.Action.Kind} lỗi", ex);
            Main.ShowToast(Loc.F("error.unexpected", ex.Message), true);
        }
    }

    private async Task RunActionAsync(SecurityAction action)
    {
        switch (action.Kind)
        {
            case SecurityActionKind.OpenLocation:
                if (File.Exists(action.Arg))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{action.Arg}\"") { UseShellExecute = true });
                else if (Directory.Exists(action.Arg))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{action.Arg}\"") { UseShellExecute = true });
                else
                    Main.ShowToast(Loc.T("sec.result.missing"), true);
                break;

            case SecurityActionKind.VirusTotal:
                if (!File.Exists(action.Arg))
                {
                    Main.ShowToast(Loc.T("sec.result.missing"), true);
                    break;
                }

                // Chỉ gửi mã băm SHA-256 lên VirusTotal (qua trình duyệt) – tệp không bị tải lên.
                var hash = await Task.Run(() => TrustChecker.Sha256(action.Arg));
                FixService.OpenUri("https://www.virustotal.com/gui/file/" + hash);
                break;

            case SecurityActionKind.Uninstall:
                var program = State.Result?.Data.Programs.FirstOrDefault(p => p.Id == action.Arg);
                if (program is null) break;
                if (!await Main.ConfirmAsync(Loc.T("sec.action.uninstall"), Loc.F("sec.confirm.uninstall", program.Name), Loc.T("sec.action.uninstall"), danger: true)) break;
                var outcome = await FixService.UninstallAsync(program);
                Main.ShowToast(outcome.Message.ToString(), !outcome.Success);
                break;

            case SecurityActionKind.DisableStartup:
                var startup = StartupCollector.Collect().FirstOrDefault(s => s.Id == action.Arg);
                if (startup is null) break;
                if (!await Main.ConfirmAsync(Loc.T("sec.action.disable"), Loc.F("sec.confirm.disable", startup.Name), Loc.T("sec.action.disable"))) break;
                StartupCollector.SetEnabled(startup, false);
                Main.ShowToast(Loc.F("sec.result.disabled", startup.Name));
                await RescanAsync();
                break;

            case SecurityActionKind.DisableTask:
                if (!await Main.ConfirmAsync(Loc.T("sec.action.disable"), Loc.F("sec.confirm.disable", Path.GetFileName(action.Arg)), Loc.T("sec.action.disable"))) break;
                var result = await FixService.DisableTaskAsync(action.Arg);
                Main.ShowToast(result.Message.ToString(), !result.Success);
                if (result.Success) await RescanAsync();
                break;

            case SecurityActionKind.OpenFile:
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{action.Arg}\"") { UseShellExecute = true });
                break;

            case SecurityActionKind.OpenUri:
                FixService.OpenUri(action.Arg);
                break;
        }
    }
}
