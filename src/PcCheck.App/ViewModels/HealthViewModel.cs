using System.Collections.ObjectModel;
using System.Globalization;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Analysis;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

public sealed class SensorRow : ObservableObject
{
    private string _value = "—";
    private string _min = "—";
    private string _max = "—";
    private Severity _severity;

    public SensorRow(string id, string name, SensorKind kind)
    {
        Id = id;
        Name = name;
        Kind = kind;
    }

    public string Id { get; }
    public string Name { get; }
    public SensorKind Kind { get; }
    public string Value { get => _value; set => Set(ref _value, value); }
    public string Min { get => _min; set => Set(ref _min, value); }
    public string Max { get => _max; set => Set(ref _max, value); }
    public Severity Severity { get => _severity; set => Set(ref _severity, value); }
}

public sealed class SensorGroup(string title, string glyph)
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public ObservableCollection<SensorRow> Rows { get; } = [];
}

/// <summary>Trang nhiệt độ & sức khỏe: cảm biến trực tiếp, S.M.A.R.T ổ cứng, pin.</summary>
public sealed class HealthViewModel : PageViewModel
{
    private readonly Dictionary<string, SensorRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SensorGroup> _groups = new(StringComparer.Ordinal);
    private IReadOnlyList<InfoGroup> _disks = [];
    private InfoGroup? _battery;
    private bool _pawnIoMissing;
    private bool _sensorsUnavailable;

    public HealthViewModel(MainViewModel main) : base(main)
    {
        BatteryReportCommand = new AsyncRelayCommand(() => Main.RunToolAsync(SystemTools.BatteryReport, false));
    }

    public StatTile CpuTemp { get; } = new("", "health.cpu_temp");
    public StatTile GpuTemp { get; } = new("", "health.gpu_temp");
    public StatTile DiskTemp { get; } = new("", "health.disk_temp");
    public StatTile Fans { get; } = new("", "health.fans");

    public ObservableCollection<SensorGroup> SensorGroups { get; } = [];
    public IReadOnlyList<InfoGroup> Disks { get => _disks; private set => Set(ref _disks, value); }
    public InfoGroup? Battery { get => _battery; private set => Set(ref _battery, value); }
    public bool PawnIoMissing { get => _pawnIoMissing; private set => Set(ref _pawnIoMissing, value); }
    public bool SensorsUnavailable { get => _sensorsUnavailable; private set => Set(ref _sensorsUnavailable, value); }
    public FixAction PawnIoFix { get; } = FixCatalog.PawnIo;
    public AsyncRelayCommand BatteryReportCommand { get; }

    protected override void Rebuild()
    {
        foreach (var tile in new[] { CpuTemp, GpuTemp, DiskTemp, Fans }) tile.RefreshTitle();

        // Đổi ngôn ngữ -> dựng lại nhóm cảm biến (tên nhóm có dịch).
        _rows.Clear();
        _groups.Clear();
        SensorGroups.Clear();
        OnLive();

        var hw = State.Result?.Data.Hardware;
        if (hw is null) return;
        var c = Loc.Instance.Culture;

        Disks = hw.Disks.Select(d =>
        {
            var severity = d.Health switch
            {
                DiskHealth.Unhealthy => Severity.Critical,
                DiskHealth.Warning => Severity.Warning,
                _ when d.PredictFailure == true => Severity.Critical,
                _ when d.WearPercent >= Thresholds.SsdWearWarn => Severity.Warning,
                DiskHealth.Healthy => Severity.Ok,
                _ => Severity.Info,
            };
            var errors = (d.ReadErrorsUncorrected ?? 0) + (d.WriteErrorsUncorrected ?? 0);
            return new InfoGroup(
                Format.OrDash(d.Model),
                $"{Labels.MediaType(d)} · {Format.OrDash(d.BusType)} · {Format.MarketingSize(d.SizeBytes, c)} · {string.Join(", ", d.DriveLetters)}",
                [
                    new(Loc.T("disk.temperature"), d.TemperatureC is null ? "—" : $"{Format.Celsius(d.TemperatureC, c)} (max {Format.Celsius(d.TemperatureMaxC, c)})"),
                    new(Loc.T("disk.life"), d.WearPercent is { } w ? Format.Percent(100 - w, c) : "—"),
                    new(Loc.T("disk.power_on"), d.PowerOnHours is { } h ? Loc.F("disk.power_on_value", h, Math.Round(h / 24.0)) : "—"),
                    new(Loc.T("disk.smart"), d.PredictFailure switch { true => Loc.T("disk.smart.fail"), false => Loc.T("disk.smart.ok"), null => "—" }),
                    new(Loc.T("disk.errors"), errors > 0 ? errors.ToString("N0", c) : Loc.T("disk.no_errors")),
                ],
                severity,
                Labels.DiskHealth(d.Health));
        }).ToList();

        if (hw.Battery is { } b)
        {
            var wear = b.WearPercent;
            Battery = new InfoGroup(
                Format.OrDash(b.Name),
                Loc.F("battery.summary", Format.Percent(b.ChargePercent, c)),
                [
                    new(Loc.T("battery.design"), b.DesignCapacity > 0 ? b.DesignCapacity.ToString("N0", c) + " mWh" : "—"),
                    new(Loc.T("battery.full"), b.FullChargeCapacity > 0 ? b.FullChargeCapacity.ToString("N0", c) + " mWh" : "—"),
                    new(Loc.T("battery.wear"), Format.Percent(wear, c)),
                    new(Loc.T("battery.cycles"), b.CycleCount?.ToString(c) ?? "—"),
                ],
                wear >= Thresholds.BatteryWearWarn ? Severity.Warning : wear >= Thresholds.BatteryWearInfo ? Severity.Info : Severity.Ok,
                wear is null ? "—" : Loc.F("battery.health", Format.Percent(100 - wear, c)));
        }
        else
        {
            Battery = null;
        }
    }

    protected override void OnLive()
    {
        var live = State.Live;
        var c = Loc.Instance.Culture;
        PawnIoMissing = live.Available && live.PawnIoInstalled == false && live.CpuTemperature is null;
        SensorsUnavailable = !live.Available && !ReferenceEquals(live, SensorSnapshot.Empty);

        SetTemp(CpuTemp, live.CpuTemperature, Thresholds.CpuTempWarn, Thresholds.CpuTempCritical, Format.Percent(live.CpuLoad, c));
        SetTemp(GpuTemp, live.GpuTemperature, Thresholds.GpuTempWarn, Thresholds.GpuTempCritical, Format.Percent(live.GpuLoad, c));
        var anyHdd = State.Result?.Data.Hardware.Disks.Any(d => !d.IsSolidState) ?? false;
        SetTemp(DiskTemp, live.MaxStorageTemperature,
            anyHdd ? Thresholds.HddTempWarn : Thresholds.SsdTempWarn,
            anyHdd ? Thresholds.HddTempCritical : Thresholds.SsdTempCritical, "");

        var fans = live.Readings.Where(r => r.Kind == SensorKind.Fan && r.Value > 0).ToList();
        Fans.Value = fans.Count == 0 ? "—" : $"{fans.Max(f => f.Value):0} RPM";
        Fans.Caption = Loc.F("health.fans_count", fans.Count);
        Fans.Severity = Severity.Ok;

        foreach (var reading in live.Readings)
        {
            if (reading.Kind is SensorKind.Data or SensorKind.SmallData or SensorKind.Other) continue;
            if (!_rows.TryGetValue(reading.Id, out var row))
            {
                var groupKey = reading.Hardware;
                if (!_groups.TryGetValue(groupKey, out var group))
                {
                    group = new SensorGroup(reading.Hardware, GlyphFor(reading.HardwareKind));
                    _groups[groupKey] = group;
                    SensorGroups.Add(group);
                }

                row = new SensorRow(reading.Id, $"{reading.Name} · {KindLabel(reading.Kind)}", reading.Kind);
                _rows[reading.Id] = row;
                group.Rows.Add(row);
            }

            row.Value = FormatValue(reading.Kind, reading.Value, c);
            row.Min = FormatValue(reading.Kind, reading.Min, c);
            row.Max = FormatValue(reading.Kind, reading.Max, c);
            row.Severity = reading.Kind == SensorKind.Temperature ? TempSeverity(reading) : Severity.Ok;
        }
    }

    private static void SetTemp(StatTile tile, float? value, double warn, double critical, string caption)
    {
        tile.Value = Format.Celsius(value);
        tile.Percent = value ?? 0;
        tile.Severity = Thresholds.TemperatureSeverity(value, warn, critical);
        tile.Caption = value is null ? Loc.T("dash.no_temp")
            : tile.Severity == Severity.Ok ? Loc.T("health.temp_ok") + (caption.Length > 0 ? " · " + caption : "")
            : Loc.T("health.temp_high");
    }

    private Severity TempSeverity(SensorReading r)
    {
        var (warn, critical) = r.HardwareKind switch
        {
            SensorHardwareKind.Cpu => (Thresholds.CpuTempWarn, Thresholds.CpuTempCritical),
            SensorHardwareKind.Gpu => (Thresholds.GpuTempWarn, Thresholds.GpuTempCritical),
            SensorHardwareKind.Storage => (Thresholds.SsdTempWarn, Thresholds.SsdTempCritical),
            _ => (80d, 95d),
        };
        return Thresholds.TemperatureSeverity(r.Value, warn, critical);
    }

    private static string GlyphFor(SensorHardwareKind kind) => kind switch
    {
        SensorHardwareKind.Cpu => "",
        SensorHardwareKind.Gpu => "",
        SensorHardwareKind.Memory => "",
        SensorHardwareKind.Storage => "",
        SensorHardwareKind.Battery => "",
        SensorHardwareKind.Network => "",
        _ => "",
    };

    private static string KindLabel(SensorKind kind) => Loc.T(kind switch
    {
        SensorKind.Temperature => "sensor.kind.temperature",
        SensorKind.Load => "sensor.kind.load",
        SensorKind.Clock => "sensor.kind.clock",
        SensorKind.Fan => "sensor.kind.fan",
        SensorKind.Voltage => "sensor.kind.voltage",
        SensorKind.Power => "sensor.kind.power",
        SensorKind.Level => "sensor.kind.level",
        SensorKind.Throughput => "sensor.kind.throughput",
        _ => "sensor.kind.other",
    });

    internal static string FormatValue(SensorKind kind, float? value, CultureInfo c)
    {
        if (value is null) return "—";
        var v = value.Value;
        return kind switch
        {
            SensorKind.Temperature => v.ToString("0", c) + " °C",
            SensorKind.Load or SensorKind.Level => v.ToString("0", c) + "%",
            SensorKind.Clock => v.ToString("0", c) + " MHz",
            SensorKind.Fan => v.ToString("0", c) + " RPM",
            SensorKind.Voltage => v.ToString("0.000", c) + " V",
            SensorKind.Power => v.ToString("0.0", c) + " W",
            SensorKind.Throughput => Format.Speed(v, c),
            _ => v.ToString("0.##", c),
        };
    }
}
