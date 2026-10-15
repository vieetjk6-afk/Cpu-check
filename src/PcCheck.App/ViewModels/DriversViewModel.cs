using System.ComponentModel;
using System.Windows.Data;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Analysis;
using PcCheck.Core.Collectors;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

public sealed class DriverProblemItem
{
    public DriverProblemItem(DriverProblem problem)
    {
        var d = problem.Device;
        Severity = problem.Severity;
        Name = d.Name;
        KindText = Labels.DriverProblem(problem.Kind);
        Detail = problem.Kind switch
        {
            DriverProblemKind.Missing or DriverProblemKind.DeviceError or DriverProblemKind.Disabled =>
                $"{DeviceErrorCodes.Describe(d.ErrorCode)} (Code {d.ErrorCode})",
            DriverProblemKind.Outdated => Loc.F("drivers.outdated_detail", Format.Date(d.DriverDate)),
            DriverProblemKind.Unsigned => Loc.T("drivers.unsigned_detail"),
            _ => Loc.T("drivers.generic_detail"),
        };
        Meta = $"{Format.OrDash(d.ClassName)} · {Format.OrDash(d.DriverProvider)} {d.DriverVersion}".Trim();
        HardwareId = d.SearchId;
        Fixes =
        [
            new FixItem(FixCatalog.SearchDriver(d)),
            new FixItem(problem.Kind == DriverProblemKind.Disabled ? FixCatalog.DeviceManager : FixCatalog.OptionalUpdates),
        ];
    }

    public Severity Severity { get; }
    public string Name { get; }
    public string KindText { get; }
    public string Detail { get; }
    public string Meta { get; }
    public string HardwareId { get; }
    public IReadOnlyList<FixItem> Fixes { get; }
}

public sealed class DeviceRow(DeviceInfo d)
{
    public string Name { get; } = d.Name;
    public string ClassName { get; } = Format.OrDash(d.ClassName);
    public string Provider { get; } = Format.OrDash(d.DriverProvider);
    public string Version { get; } = Format.OrDash(d.DriverVersion);
    public string Date { get; } = Format.Date(d.DriverDate);
    public bool HasError { get; } = d.ErrorCode != 0;
    public string SearchText { get; } = $"{d.Name} {d.ClassName} {d.DriverProvider} {d.Manufacturer}";
}

/// <summary>Trang driver: thiết bị lỗi / thiếu driver / driver cũ + danh sách toàn bộ driver.</summary>
public sealed class DriversViewModel : PageViewModel
{
    private IReadOnlyList<DriverProblemItem> _problems = [];
    private ICollectionView? _devices;
    private string _filter = "";
    private int _missing;
    private int _errors;
    private int _outdated;
    private int _total;
    private string? _gpuVendorUrl;
    private string? _oemUrl;
    private bool _isBusy;
    private string _tab = "problems";

    public DriversViewModel(MainViewModel main) : base(main)
    {
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => HasResult && !IsBusy);
        ScanHardwareCommand = new AsyncRelayCommand(async () =>
        {
            await Main.RunToolAsync(SystemTools.ScanDevices, false);
            await RescanAsync();
        }, () => !IsBusy);
    }

    public IReadOnlyList<DriverProblemItem> Problems { get => _problems; private set => Set(ref _problems, value); }
    public ICollectionView? Devices { get => _devices; private set => Set(ref _devices, value); }
    public int MissingCount { get => _missing; private set => Set(ref _missing, value); }
    public int ErrorCount { get => _errors; private set => Set(ref _errors, value); }
    public int OutdatedCount { get => _outdated; private set => Set(ref _outdated, value); }
    public int TotalCount { get => _total; private set => Set(ref _total, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public string Tab { get => _tab; set => Set(ref _tab, value); }
    public bool AllGood => HasResult && Problems.All(p => p.Severity < Severity.Warning);

    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value)) Devices?.Refresh();
        }
    }

    public FixAction OptionalUpdatesFix { get; } = FixCatalog.OptionalUpdates;
    public FixAction DeviceManagerFix { get; } = FixCatalog.DeviceManager;
    public FixAction? GpuDriverFix => _gpuVendorUrl is null ? null : FixCatalog.VendorDrivers(_gpuVendorUrl);
    public FixAction? OemDriverFix => _oemUrl is null ? null : FixCatalog.VendorDrivers(_oemUrl);
    public string GpuVendorName { get; private set; } = "";
    public string OemName { get; private set; } = "";

    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand ScanHardwareCommand { get; }

    protected override void Rebuild()
    {
        if (State.Result is not { } result)
        {
            Problems = [];
            Devices = null;
            return;
        }

        var problems = result.DriverProblems;
        Problems = problems.Select(p => new DriverProblemItem(p)).ToList();
        MissingCount = problems.Count(p => p.Kind is DriverProblemKind.Missing or DriverProblemKind.Generic && p.Severity >= Severity.Warning);
        ErrorCount = problems.Count(p => p.Kind is DriverProblemKind.DeviceError or DriverProblemKind.Unsigned);
        OutdatedCount = problems.Count(p => p.Kind == DriverProblemKind.Outdated);
        TotalCount = result.Data.Devices.Count(d => d.HasDriver);

        var rows = result.Data.Devices
            .Where(d => d.HasDriver || d.ErrorCode != 0)
            .OrderBy(d => d.ClassName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new DeviceRow(d))
            .ToList();
        var view = CollectionViewSource.GetDefaultView(rows);
        view.Filter = o => o is DeviceRow r && (_filter.Length == 0 || r.SearchText.Contains(_filter, StringComparison.CurrentCultureIgnoreCase));
        Devices = view;

        var hw = result.Data.Hardware;
        var gpu = hw.Gpus.FirstOrDefault(g => !g.IsVirtual && !g.IsBasicDriver) ?? hw.Gpus.FirstOrDefault();
        var gpuPage = gpu is null ? null : VendorSupport.GpuDriverPage(gpu.Name, gpu.Vendor);
        _gpuVendorUrl = gpuPage?.Url;
        GpuVendorName = gpuPage?.Label.ToString() ?? "";
        var oemManufacturer = hw.Computer.IsLaptop ? hw.Computer.Manufacturer : hw.Board.Manufacturer;
        _oemUrl = VendorSupport.OemDriverPage(oemManufacturer) ?? VendorSupport.OemDriverPage(hw.Computer.Manufacturer);
        OemName = Loc.F("drivers.oem_page", Format.OrDash(oemManufacturer));
        OnPropertyChanged(nameof(GpuDriverFix));
        OnPropertyChanged(nameof(OemDriverFix));
        OnPropertyChanged(nameof(GpuVendorName));
        OnPropertyChanged(nameof(OemName));
        OnPropertyChanged(nameof(AllGood));
    }

    private async Task RescanAsync()
    {
        IsBusy = true;
        try
        {
            await State.ReanalyzeAsync(async data => data.Devices = await Task.Run(DriverCollector.Collect));
            Main.ShowToast(Loc.T("drivers.rescanned"));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
