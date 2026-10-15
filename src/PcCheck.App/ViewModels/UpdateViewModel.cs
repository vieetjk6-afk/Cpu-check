using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Updates;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    NoRelease,
    Failed,
    Downloading,
    Installing,
}

/// <summary>Kiểm tra / tải / cài bản mới từ GitHub Releases (thẻ "Cập nhật" trong Cài đặt + thông báo ở thanh bên).</summary>
public sealed class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(12);

    private readonly MainViewModel _main;
    private UpdateState _state;
    private ReleaseInfo? _release;
    private string? _error;
    private double _progress;

    public UpdateViewModel(MainViewModel main)
    {
        _main = main;
        CurrentVersion = Version.TryParse(main.AppVersion, out var v) ? v : new Version(1, 0, 0);
        Flavor = DetectFlavor();
        CanSelfUpdate = string.IsNullOrEmpty(typeof(UpdateViewModel).Assembly.Location) && Environment.ProcessPath is not null;

        CheckCommand = new AsyncRelayCommand(() => CheckAsync(silent: false), () => !IsBusy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => CanInstall && !IsBusy);
        SkipCommand = new RelayCommand(Skip, () => _release is not null && State == UpdateState.Available);
        OpenPageCommand = new RelayCommand(() => FixService.OpenUri(_release?.PageUrl ?? UpdateService.ReleasesPage));
        Loc.Instance.LanguageChanged += (_, _) => Refresh();
    }

    public Version CurrentVersion { get; }

    /// <summary>"portable" (kèm .NET) hoặc "small" (cần .NET Desktop Runtime) – quyết định tệp cần tải.</summary>
    public string Flavor { get; }

    /// <summary>Đang chạy bản 1 tệp .exe (bản phát hành) – thay thế được. Bản build thử nhiều tệp thì chỉ mở trang tải.</summary>
    public bool CanSelfUpdate { get; }

    public UpdateState State
    {
        get => _state;
        private set
        {
            if (Set(ref _state, value)) Refresh();
        }
    }

    public bool IsBusy => State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
    public bool IsAvailable => State == UpdateState.Available;
    public bool IsDownloading => State is UpdateState.Downloading or UpdateState.Installing;
    public bool CanInstall => IsAvailable && CanSelfUpdate && Asset is not null;
    public bool ShowManualHint => IsAvailable && !CanInstall;

    /// <summary>Hiện thông báo ở thanh bên: có bản mới và người dùng chưa bỏ qua bản đó.</summary>
    public bool ShowBanner => IsAvailable && _release is not null && _release.Tag != Settings.SkippedVersion;

    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    public string CurrentText => Loc.F("update.current", CurrentVersion.ToString(3));
    public string LatestVersion => _release is null ? "" : "v" + _release.Version.ToString(3);
    public string ReleaseNotes => _release is null ? "" : UpdateService.ToPlainText(_release.Notes);
    public bool HasNotes => IsAvailable && ReleaseNotes.Length > 0;
    public string BannerText => Loc.F("update.banner", LatestVersion);

    public string StatusText => State switch
    {
        UpdateState.Checking => Loc.T("update.checking"),
        UpdateState.UpToDate => Loc.F("update.up_to_date", CurrentVersion.ToString(3)),
        UpdateState.Available => Loc.F("update.available", LatestVersion, PublishedText),
        UpdateState.NoRelease => Loc.T("update.no_release"),
        UpdateState.Failed => Loc.F("update.failed", ErrorText(_error)),
        UpdateState.Downloading => Loc.F("update.downloading", LatestVersion, Progress.ToString("0", CultureInfo.InvariantCulture)),
        UpdateState.Installing => Loc.T("update.installing"),
        _ => Settings.LastUpdateCheckUtc is { } last
            ? Loc.F("update.last_check", last.ToLocalTime().ToString("g", Loc.Instance.Culture))
            : Loc.T("update.never_checked"),
    };

    public string ManualHint => !CanSelfUpdate ? Loc.T("update.manual_only") : Loc.F("update.no_asset", UpdateService.AssetName(Flavor));

    public bool AutoCheck
    {
        get => Settings.AutoCheckUpdates;
        set
        {
            Settings.AutoCheckUpdates = value;
            Settings.Save();
            OnPropertyChanged();
        }
    }

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand SkipCommand { get; }
    public RelayCommand OpenPageCommand { get; }

    private AppSettings Settings => _main.State.Settings;
    private ReleaseAsset? Asset => _release is null ? null : UpdateService.PickAsset(_release, Flavor);
    private string PublishedText => _release?.Published?.ToLocalTime().ToString("d", Loc.Instance.Culture) ?? "—";

    /// <summary>Kiểm tra ngầm khi mở ứng dụng (tối đa mỗi 12 giờ), chỉ báo khi có bản mới.</summary>
    public async Task AutoCheckAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            if (!AutoCheck || IsBusy) return;

            // Lần trước đã thấy bản mới -> kiểm tra lại ngay để hiện lại thông báo; không thì tối đa mỗi 12 giờ.
            var knownUpdate = UpdateService.ParseVersion(Settings.AvailableVersion ?? "") is { } known && UpdateService.IsNewer(known, CurrentVersion);
            if (!knownUpdate && Settings.LastUpdateCheckUtc is { } last && DateTime.UtcNow - last < AutoCheckInterval) return;
            await CheckAsync(silent: true);
            if (ShowBanner) _main.ShowToast(Loc.F("update.toast", LatestVersion));
        }
        catch (Exception ex)
        {
            Log.Warn("Tự kiểm tra cập nhật lỗi", ex);
        }
    }

    public async Task CheckAsync(bool silent)
    {
        if (IsBusy) return;
        State = UpdateState.Checking;
        var result = await UpdateService.CheckAsync(CurrentVersion);
        _release = result.Release;
        _error = result.Error;

        // Chỉ ghi nhận lần kiểm tra thành công: lỗi mạng (vd vừa đăng nhập, Wi-Fi chưa có) không được hoãn lần sau 12 giờ.
        if (result.Status != UpdateStatus.Failed)
        {
            Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            Settings.AvailableVersion = result.Status == UpdateStatus.Available ? result.Release?.Tag : null;
            Settings.Save();
        }

        State = result.Status switch
        {
            UpdateStatus.Available => UpdateState.Available,
            UpdateStatus.UpToDate => UpdateState.UpToDate,
            UpdateStatus.NoRelease => UpdateState.NoRelease,
            _ => UpdateState.Failed,
        };
        if (!silent && State == UpdateState.Failed) _main.ShowToast(StatusText, isError: true);
    }

    private async Task InstallAsync()
    {
        if (_release is null || Asset is not { } asset || !CanSelfUpdate) return;
        var size = asset.Size > 0 ? Format.Bytes(asset.Size, Loc.Instance.Culture) : "—";
        if (!await _main.ConfirmAsync(Loc.T("update.confirm_title"), Loc.F("update.confirm", LatestVersion, size), Loc.T("update.install"))) return;

        State = UpdateState.Downloading;
        Progress = 0;
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress = p;
                OnPropertyChanged(nameof(StatusText));
            });
            var prepared = await UpdateService.DownloadAsync(asset, progress);

            State = UpdateState.Installing;
            var exe = Environment.ProcessPath!;

            // Chép + kiểm tra tệp lớn ở luồng nền để cửa sổ không bị đơ.
            var installed = await Task.Run(() => UpdateService.Install(prepared, exe));
            try
            {
                // Giữ khóa tệp tới khi tiến trình mới đã chạy. Tiến trình mới chờ bản này thoát rồi mới mở.
                using var started = Process.Start(new ProcessStartInfo(exe, UpdateService.AfterUpdateArgument)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe)!,
                }) ?? throw new InvalidOperationException("Process.Start returned null");
                installed.Dispose();
            }
            catch
            {
                // Không chạy được bản mới (vd phần mềm diệt virus chặn) -> trả bản cũ về chỗ cũ.
                await Task.Run(installed.Rollback);
                throw;
            }

            Log.Info($"Đã cài bản {_release.Tag}, khởi động lại");
            App.Current.ExitApplication();
        }
        catch (UpdateIntegrityException ex)
        {
            Log.Warn("Bản cập nhật không hợp lệ", ex);
            Fail(Loc.T("update.result.corrupt"));
        }
        catch (Exception ex)
        {
            Log.Error("Cập nhật lỗi", ex);
            Fail(Loc.F("update.result.failed", ex.Message));
        }
    }

    private static string ErrorText(string? code) => code switch
    {
        "rate_limit" => Loc.T("update.err.rate_limit"),
        "timeout" => Loc.T("update.err.timeout"),
        "network" => Loc.T("update.err.network"),
        "invalid" => Loc.T("update.err.invalid"),
        null or "" => Loc.T("update.err.network"),
        var other => other,
    };

    private void Fail(string message)
    {
        State = UpdateState.Available;
        _main.ShowToast(message, isError: true);
    }

    private void Skip()
    {
        if (_release is null) return;
        Settings.SkippedVersion = _release.Tag;
        Settings.Save();
        _main.ShowToast(Loc.F("update.skipped", LatestVersion));
        Refresh();
    }

    private void Refresh()
    {
        foreach (var name in new[]
                 {
                     nameof(IsBusy), nameof(IsAvailable), nameof(IsDownloading), nameof(CanInstall), nameof(ShowManualHint), nameof(ShowBanner),
                     nameof(CurrentText), nameof(LatestVersion), nameof(ReleaseNotes), nameof(HasNotes), nameof(BannerText), nameof(StatusText),
                     nameof(ManualHint),
                 })
        {
            OnPropertyChanged(name);
        }

        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private static string DetectFlavor()
    {
        var metadata = typeof(UpdateViewModel).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "PcCheckFlavor")?.Value;
        if (metadata is "portable" or "small") return metadata;

        // Bản tự chứa .NET (portable) đóng gói cả thư viện hệ thống -> không có đường dẫn tệp thật.
        return string.IsNullOrEmpty(typeof(object).Assembly.Location) ? "portable" : "small";
    }
}
