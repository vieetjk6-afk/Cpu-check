using System.Collections.ObjectModel;
using System.Globalization;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Analysis;
using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Sensors;
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

/// <summary>Nhóm cảm biến cùng loại trong thẻ thiết bị (Nhiệt độ, Tải, Xung nhịp...).</summary>
public sealed class SensorSection(SensorKind kind, string title)
{
    public SensorKind Kind { get; } = kind;
    public string Title { get; } = title;
    public ObservableCollection<SensorRow> Rows { get; } = [];
}

/// <summary>Thẻ một thiết bị: tiêu đề tóm tắt (nhiệt độ chính) + phần chi tiết thu gọn được.</summary>
public sealed class DeviceCard : ObservableObject
{
    private readonly Dictionary<string, SensorRow> _rows = new(StringComparer.Ordinal);
    private string _primary = "—";
    private string _primaryLabel = "";
    private Severity _primarySeverity;
    private string _secondary = "";
    private string _statusText = "";
    private Severity _status;
    private bool _isExpanded;
    private IReadOnlyList<InfoItem> _info = [];
    private string _type = "";

    public DeviceCard(string key, string name, SensorHardwareKind kind, int order, string glyph)
    {
        Key = key;
        Name = name;
        Kind = kind;
        Order = order;
        Glyph = glyph;
    }

    public string Key { get; }
    public string Name { get; }
    public SensorHardwareKind Kind { get; }
    public int Order { get; }
    public string Glyph { get; }

    public string TypeText { get => _type; set => Set(ref _type, value); }
    public string Primary { get => _primary; set => Set(ref _primary, value); }
    public string PrimaryLabel { get => _primaryLabel; set => Set(ref _primaryLabel, value); }
    public Severity PrimarySeverity { get => _primarySeverity; set => Set(ref _primarySeverity, value); }
    public string Secondary { get => _secondary; set => Set(ref _secondary, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public Severity Status { get => _status; set => Set(ref _status, value); }
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    /// <summary>Thông tin thiết bị. Chỉ thay khi nội dung đổi để danh sách không bị vẽ lại mỗi lần cập nhật.</summary>
    public IReadOnlyList<InfoItem> Info
    {
        get => _info;
        set
        {
            if (!_info.SequenceEqual(value)) Set(ref _info, value);
        }
    }

    /// <summary>Số ổ vật lý (thẻ ổ cứng lấy từ cảm biến), dùng để ghép với kết quả quét khi có nhiều ổ cùng mẫu.</summary>
    public string? DriveNumber { get; init; }
    public ObservableCollection<SensorSection> Sections { get; } = [];
    public bool HasSensors => Sections.Count > 0;

    /// <summary>Cập nhật/thêm dòng cảm biến (giữ nguyên đối tượng để giao diện không nháy).</summary>
    public void Apply(SensorReading reading, CultureInfo c, Func<SensorReading, Severity> severity)
    {
        if (!_rows.TryGetValue(reading.Id, out var row))
        {
            var section = Sections.FirstOrDefault(s => s.Kind == reading.Kind);
            if (section is null)
            {
                section = new SensorSection(reading.Kind, HealthViewModel.KindTitle(reading.Kind));
                var index = 0;
                while (index < Sections.Count && Sections[index].Kind < reading.Kind) index++;
                Sections.Insert(index, section);
                OnPropertyChanged(nameof(HasSensors));
            }

            row = new SensorRow(reading.Id, reading.Name, reading.Kind);
            _rows[reading.Id] = row;
            section.Rows.Add(row);
        }

        row.Value = HealthViewModel.FormatValue(reading.Kind, reading.Value, c);
        row.Min = HealthViewModel.FormatValue(reading.Kind, reading.Min, c);
        row.Max = HealthViewModel.FormatValue(reading.Kind, reading.Max, c);
        row.Severity = reading.Kind == SensorKind.Temperature ? severity(reading) : Severity.Ok;
    }
}

/// <summary>Trang nhiệt độ & sức khỏe: thẻ riêng cho từng thiết bị (thu gọn / mở rộng), cập nhật trực tiếp.</summary>
public sealed class HealthViewModel : PageViewModel
{
    private readonly Dictionary<string, DeviceCard> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private bool _cpuSensorMissing;
    private bool _cpuEstimated;
    private bool _sensorsUnavailable;
    private bool _hasBattery;
    private string _cpuSensorText = "";

    public HealthViewModel(MainViewModel main) : base(main)
    {
        BatteryReportCommand = new AsyncRelayCommand(() => Main.RunToolAsync(SystemTools.BatteryReport, false));
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false));
        _expanded["cpu"] = true;
    }

    public StatTile CpuTemp { get; } = new("", "health.cpu_temp");
    public StatTile GpuTemp { get; } = new("", "health.gpu_temp");
    public StatTile DiskTemp { get; } = new("", "health.disk_temp");
    public StatTile Fans { get; } = new("", "health.fans");

    public ObservableCollection<DeviceCard> Devices { get; } = [];

    /// <summary>Chưa có cảm biến nhiệt độ CPU thật và có thể bật bằng PawnIO.</summary>
    public bool CpuSensorMissing { get => _cpuSensorMissing; private set => Set(ref _cpuSensorMissing, value); }

    /// <summary>Đang hiển thị nhiệt độ CPU ước lượng (ACPI).</summary>
    public bool CpuEstimated { get => _cpuEstimated; private set => Set(ref _cpuEstimated, value); }

    public string CpuSensorText { get => _cpuSensorText; private set => Set(ref _cpuSensorText, value); }

    public bool SensorsUnavailable { get => _sensorsUnavailable; private set => Set(ref _sensorsUnavailable, value); }
    public bool HasBattery { get => _hasBattery; private set => Set(ref _hasBattery, value); }
    public FixAction EnableCpuSensorFix { get; } = FixCatalog.EnableCpuSensor;
    public AsyncRelayCommand BatteryReportCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }

    private void SetAllExpanded(bool expanded)
    {
        foreach (var card in Devices)
        {
            card.IsExpanded = expanded;
            _expanded[card.Key] = expanded;
        }
    }

    protected override void Rebuild()
    {
        foreach (var tile in new[] { CpuTemp, GpuTemp, DiskTemp, Fans }) tile.RefreshTitle();

        // Đổi ngôn ngữ / kết quả quét mới -> dựng lại thẻ (giữ trạng thái mở rộng).
        foreach (var card in Devices) _expanded[card.Key] = card.IsExpanded;
        _cards.Clear();
        Devices.Clear();
        OnLive();
    }

    protected override void OnLive()
    {
        var live = State.Live;
        var c = Loc.Instance.Culture;
        CpuSensorMissing = !live.HasCpuSensor && live.PawnIoInstalled == false;
        CpuEstimated = live.CpuTemperatureEstimated;
        CpuSensorText = CpuEstimated
            ? Loc.F("health.cpu_sensor.estimated", Format.Celsius(live.CpuTemperature, c))
            : Loc.T("health.cpu_sensor.none");
        HasBattery = State.Result?.Data.Hardware.Battery is not null || live.BatteryLevel is not null;
        SensorsUnavailable = !live.Available && !ReferenceEquals(live, SensorSnapshot.Empty);

        SetTemp(CpuTemp, live.CpuTemperature, Thresholds.CpuTempWarn, Thresholds.CpuTempCritical,
            live.CpuTemperatureEstimated ? Loc.T("health.estimated") : Format.Percent(live.CpuLoad, c));
        // Nhiệt độ ước lượng (ACPI) không đủ tin cậy để báo đỏ.
        if (live.CpuTemperatureEstimated && CpuTemp.Severity == Severity.Critical) CpuTemp.Severity = Severity.Warning;
        SetTemp(GpuTemp, live.GpuTemperature, Thresholds.GpuTempWarn, Thresholds.GpuTempCritical, Format.Percent(live.GpuLoad, c));
        var anyHdd = State.Result?.Data.Hardware.Disks.Any(d => !d.IsSolidState) ?? false;
        SetTemp(DiskTemp, live.MaxStorageTemperature,
            anyHdd ? Thresholds.HddTempWarn : Thresholds.SsdTempWarn,
            anyHdd ? Thresholds.HddTempCritical : Thresholds.SsdTempCritical, "");
        Fans.Value = live.MaxFanRpm is { } rpm ? rpm.ToString("0", c) + " RPM" : "—";
        Fans.Caption = Loc.F("health.fans_count", live.FanCount);
        Fans.Severity = Severity.Ok;

        try
        {
            UpdateDevices(live, c);
        }
        catch (Exception ex)
        {
            Log.Warn("Cập nhật thẻ thiết bị lỗi", ex);
        }
    }

    private void UpdateDevices(SensorSnapshot live, CultureInfo c)
    {
        var hw = State.Result?.Data.Hardware;

        // Gom cảm biến theo thiết bị. CPU / RAM / bo mạch có thể gồm nhiều "hardware" con (vd chip SuperIO) -> gộp một thẻ.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in live.Readings.GroupBy(CardKey))
        {
            var first = group.First();
            var isAcpi = group.Key == "acpi";
            var kind = isAcpi ? SensorHardwareKind.Other : first.HardwareKind;
            seen.Add(group.Key);
            var card = GetOrCreate(group.Key, CardName(group.Key, first.Hardware, hw, c), kind, isAcpi, first.DriveNumber);
            foreach (var reading in group)
            {
                if (reading.Kind == SensorKind.Other) continue;
                card.Apply(reading, c, TempSeverity);
            }

            UpdateSummary(card, group.ToList(), live, hw, c);
        }

        // Thiết bị không còn cảm biến (vd vùng nhiệt ACPI sau khi đã có cảm biến CPU thật, ổ USB đã rút) -> bỏ thẻ cũ.
        if (live.Readings.Count > 0)
        {
            foreach (var key in _cards.Keys.Where(k => (k == "acpi" || k.StartsWith("hw:", StringComparison.Ordinal)) && !seen.Contains(k)).ToList())
                Remove(key);
        }

        if (hw is null) return;

        // Thiết bị có trong kết quả quét nhưng (chưa) có cảm biến trực tiếp, vd ổ USB hoặc khi bộ đọc cảm biến chưa sẵn sàng.
        // Khi thẻ cảm biến thật xuất hiện thì bỏ thẻ tạm để không bị trùng.
        foreach (var disk in hw.Disks)
        {
            var key = "disk:" + disk.Number;
            if (FindStorageCard(disk, hw) is not null)
            {
                Remove(key);
                continue;
            }

            var card = GetOrCreate(key, Format.OrDash(disk.Model), SensorHardwareKind.Storage, false, disk.Number);
            UpdateSummary(card, [], live, hw, c);
        }

        if (_cards.Values.Any(d => d.Kind == SensorHardwareKind.Battery && d.Key != "battery"))
        {
            Remove("battery");
        }
        else if (hw.Battery is not null)
        {
            var card = GetOrCreate("battery", Format.OrDash(hw.Battery.Name), SensorHardwareKind.Battery, false);
            UpdateSummary(card, [], live, hw, c);
        }

        if (!_cards.ContainsKey("cpu") && hw.Cpus.FirstOrDefault() is { } cpu)
        {
            var card = GetOrCreate("cpu", cpu.Name, SensorHardwareKind.Cpu, false);
            UpdateSummary(card, [], live, hw, c);
        }
    }

    private static string CardKey(SensorReading r)
    {
        if (r.Id.StartsWith(ThermalZoneReader.IdPrefix, StringComparison.Ordinal)) return "acpi";
        return r.HardwareKind switch
        {
            SensorHardwareKind.Cpu => "cpu",
            SensorHardwareKind.Memory => "memory",
            SensorHardwareKind.Motherboard => "board",
            _ => "hw:" + r.DeviceKey,
        };
    }

    /// <summary>Tên thẻ: ưu tiên tên đầy đủ từ kết quả quét (vd "16 GB DDR4", tên bo mạch) thay cho tên chip cảm biến.</summary>
    private static string CardName(string key, string sensorName, HardwareInfo? hw, CultureInfo c)
    {
        switch (key)
        {
            case "cpu" when hw?.Cpus.FirstOrDefault() is { } cpu && !string.IsNullOrWhiteSpace(cpu.Name):
                return cpu.Name;
            case "memory" when hw is { Memory.InstalledBytes: > 0 }:
            {
                var type = hw.Memory.Modules.FirstOrDefault()?.Type;
                return $"{Format.Bytes(hw.Memory.InstalledBytes, c)} {type}".Trim();
            }

            case "board" when hw is not null && !string.IsNullOrWhiteSpace(hw.Board.Product):
                return $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim();
            default:
                return sensorName;
        }
    }

    private void Remove(string key)
    {
        if (!_cards.Remove(key, out var card)) return;
        Devices.Remove(card);
    }

    private DeviceCard GetOrCreate(string key, string name, SensorHardwareKind kind, bool isAcpi, string? driveNumber = null)
    {
        if (_cards.TryGetValue(key, out var existing)) return existing;
        var order = isAcpi ? 7 : kind switch
        {
            SensorHardwareKind.Cpu => 0,
            SensorHardwareKind.Gpu => 1,
            SensorHardwareKind.Memory => 2,
            SensorHardwareKind.Motherboard => 3,
            SensorHardwareKind.Storage => 4,
            SensorHardwareKind.Battery => 5,
            SensorHardwareKind.Network => 6,
            _ => 8,
        };
        var displayName = isAcpi ? Loc.T("device.acpi_name") : name;
        var card = new DeviceCard(key, displayName, kind, order, isAcpi ? "" : GlyphFor(kind))
        {
            DriveNumber = driveNumber,
            IsExpanded = _expanded.TryGetValue(key, out var e) && e,
            TypeText = Loc.T(isAcpi ? "device.type.acpi" : kind switch
            {
                SensorHardwareKind.Cpu => "device.type.cpu",
                SensorHardwareKind.Gpu => "device.type.gpu",
                SensorHardwareKind.Memory => "device.type.memory",
                SensorHardwareKind.Motherboard => "device.type.motherboard",
                SensorHardwareKind.Storage => "device.type.storage",
                SensorHardwareKind.Battery => "device.type.battery",
                SensorHardwareKind.Network => "device.type.network",
                _ => "device.type.other",
            }),
        };
        if (driveNumber is not null) card.TypeText += " · " + Loc.F("device.disk_number", driveNumber);
        card.PropertyChanged += (_, e2) =>
        {
            if (e2.PropertyName == nameof(DeviceCard.IsExpanded)) _expanded[card.Key] = card.IsExpanded;
        };
        _cards[key] = card;

        var index = 0;
        while (index < Devices.Count && (Devices[index].Order < order || (Devices[index].Order == order && string.CompareOrdinal(Devices[index].Name, displayName) < 0))) index++;
        Devices.Insert(index, card);
        return card;
    }

    /// <summary>Thẻ cảm biến của một ổ: ghép theo số ổ vật lý; chỉ ghép theo tên mẫu khi không có số và tên là duy nhất.</summary>
    private DeviceCard? FindStorageCard(DiskInfo disk, HardwareInfo hw)
    {
        var storage = _cards.Values.Where(card => card.Kind == SensorHardwareKind.Storage && card.Key.StartsWith("hw:", StringComparison.Ordinal)).ToList();
        var byNumber = storage.FirstOrDefault(card => card.DriveNumber is not null && card.DriveNumber == disk.Number);
        if (byNumber is not null) return byNumber;

        var model = Normalize(disk.Model);
        if (model.Length == 0 || hw.Disks.Count(d => Normalize(d.Model) == model) > 1) return null;
        return storage.FirstOrDefault(card => card.DriveNumber is null && SameModel(card.Name, model));
    }

    private static string Normalize(string text) =>
        new(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool SameModel(string name, string normalizedModel)
    {
        var n = Normalize(name);
        return n.Length > 0 && normalizedModel.Length > 0
               && (n.Contains(normalizedModel, StringComparison.Ordinal) || normalizedModel.Contains(n, StringComparison.Ordinal));
    }

    private static DiskInfo? FindDisk(DeviceCard card, HardwareInfo? hw)
    {
        if (hw is null) return null;
        if (card.Key.StartsWith("disk:", StringComparison.Ordinal)) return hw.Disks.FirstOrDefault(d => "disk:" + d.Number == card.Key);
        if (card.DriveNumber is not null && hw.Disks.FirstOrDefault(d => d.Number == card.DriveNumber) is { } byNumber) return byNumber;

        var matches = hw.Disks.Where(d => SameModel(card.Name, Normalize(d.Model))).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Giá trị tóm tắt ở tiêu đề thẻ + phần thông tin thiết bị lấy từ kết quả quét.</summary>
    private void UpdateSummary(DeviceCard card, IReadOnlyList<SensorReading> readings, SensorSnapshot live, HardwareInfo? hw, CultureInfo c)
    {
        float? MaxTemp() => readings.Where(r => r.Kind == SensorKind.Temperature && r.Value is > 0 and < 150).Select(r => r.Value).DefaultIfEmpty(null).Max();
        float? Find(SensorKind kind, params string[] names) => SensorSnapshot.Pick(readings, card.Kind, kind, names);

        switch (card.Key == "acpi" ? (SensorHardwareKind?)null : card.Kind)
        {
            case SensorHardwareKind.Cpu:
            {
                var temp = SensorSnapshot.PickCpuTemperature(readings);
                var estimated = temp is null && live.AcpiTemperature is not null;
                temp ??= live.AcpiTemperature;
                SetPrimaryTemp(card, temp, Thresholds.CpuTempWarn, Thresholds.CpuTempCritical, c);
                if (estimated && card.PrimarySeverity == Severity.Critical) card.PrimarySeverity = Severity.Warning;
                card.PrimaryLabel = estimated ? Loc.T("health.estimated") : "";
                var load = Find(SensorKind.Load, "CPU Total") ?? State.LiveCpuLoad.ToFloat();
                var clock = readings.Where(r => r.Kind == SensorKind.Clock && r.Value > 0 && !r.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase)).Select(r => r.Value).DefaultIfEmpty(null).Max();
                card.Secondary = Join(load is null ? null : Loc.F("device.load", Format.Percent(load, c)), clock is null ? null : Format.Mhz(clock.Value, c));
                if (hw?.Cpus.FirstOrDefault() is { } cpu)
                {
                    card.Info =
                    [
                        new(Loc.T("hw.cpu.cores"), Loc.F("hw.cpu.cores_value", cpu.Cores, cpu.Threads)),
                        new(Loc.T("hw.cpu.clock"), cpu.MaxClockMhz > 0 ? Format.Mhz(cpu.MaxClockMhz, c) : "—"),
                        new(Loc.T("hw.cpu.cache"), $"L2 {Format.Bytes(cpu.L2CacheKb * 1024L, c)} · L3 {Format.Bytes(cpu.L3CacheKb * 1024L, c)}"),
                        new(Loc.T("hw.cpu.socket"), Format.OrDash(cpu.Socket)),
                        new(Loc.T("hw.cpu.virtualization"), Labels.YesNo(cpu.VirtualizationEnabled == true || hw.Computer.HypervisorPresent ? true : cpu.VirtualizationEnabled)),
                        new(Loc.T("device.temp_source"), estimated ? Loc.T("device.source_acpi") : temp is null ? "—" : Loc.T("device.source_sensor")),
                    ];
                }

                break;
            }

            case SensorHardwareKind.Gpu:
            {
                SetPrimaryTemp(card, Find(SensorKind.Temperature, "GPU Core", "Core"), Thresholds.GpuTempWarn, Thresholds.GpuTempCritical, c);
                var load = Find(SensorKind.Load, "GPU Core", "D3D 3D", "Core");
                var used = Find(SensorKind.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
                card.Secondary = Join(load is null ? null : Loc.F("device.load", Format.Percent(load, c)),
                    used is null ? null : "VRAM " + Format.Bytes((long)(used.Value * 1024 * 1024), c));
                var gpu = hw?.Gpus.FirstOrDefault(g => Normalize(g.Name).Contains(Normalize(card.Name), StringComparison.Ordinal) || Normalize(card.Name).Contains(Normalize(g.Name), StringComparison.Ordinal))
                          ?? hw?.Gpus.FirstOrDefault(g => !g.IsVirtual);
                if (gpu is not null)
                {
                    card.Info =
                    [
                        new(Loc.T("hw.board.vendor"), Format.OrDash(gpu.Vendor)),
                        new(Loc.T("gpu.vram"), gpu.VramBytes > 0 ? Format.Bytes(gpu.VramBytes, c) : "—"),
                        new(Loc.T("gpu.driver"), Format.OrDash(gpu.DriverVersion)),
                        new(Loc.T("gpu.driver_date"), Format.Date(gpu.DriverDate, c)),
                        new(Loc.T("gpu.mode"), gpu.ResolutionX > 0 ? $"{gpu.ResolutionX}×{gpu.ResolutionY} @ {gpu.RefreshRate} Hz" : "—"),
                    ];
                }

                break;
            }

            case SensorHardwareKind.Memory:
            {
                var percent = State.MemoryTotal > 0 ? 100.0 * (State.MemoryTotal - State.MemoryAvailable) / State.MemoryTotal : (double?)Find(SensorKind.Load, "Memory");
                card.Primary = Format.Percent(percent, c);
                card.PrimarySeverity = percent >= Thresholds.RamUsageWarn ? Severity.Warning : Severity.Ok;
                card.PrimaryLabel = Loc.T("device.in_use");
                card.Secondary = State.MemoryTotal > 0 ? $"{Format.Bytes(State.MemoryTotal - State.MemoryAvailable, c)} / {Format.Bytes(State.MemoryTotal, c)}" : "";
                if (hw is not null)
                {
                    var module = hw.Memory.Modules.FirstOrDefault();
                    card.Info =
                    [
                        new(Loc.T("ram.capacity"), Format.Bytes(hw.Memory.InstalledBytes, c)),
                        new(Loc.T("hw.ram.slots"), hw.Memory.SlotCount > 0 ? Loc.F("hw.ram.slots_value", hw.Memory.Modules.Count, hw.Memory.SlotCount) : hw.Memory.Modules.Count.ToString(c)),
                        new(Loc.T("ram.type"), module is null ? "—" : $"{module.Type} {module.FormFactor}".Trim()),
                        new(Loc.T("ram.speed"), module is null ? "—" : $"{module.ConfiguredSpeedMhz} / {module.SpeedMhz} MHz"),
                    ];
                }

                break;
            }

            case SensorHardwareKind.Motherboard:
            {
                SetPrimaryTemp(card, MaxTemp(), 80, 95, c);
                var fans = readings.Where(r => r.Kind == SensorKind.Fan && r.Value > 0).ToList();
                card.Secondary = fans.Count == 0 ? "" : Loc.F("device.fans", fans.Count, fans.Max(f => f.Value)!.Value.ToString("0", c));
                if (hw is not null)
                {
                    card.Info =
                    [
                        new(Loc.T("hw.board.model"), $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim()),
                        new(Loc.T("hw.bios"), $"{hw.Board.BiosVersion} ({Format.Date(hw.Board.BiosDate, c)})"),
                        new(Loc.T("hw.firmware"), $"{(hw.Firmware.IsUefi == true ? "UEFI" : hw.Firmware.IsUefi == false ? "Legacy BIOS" : "—")} · TPM {Format.OrDash(hw.Firmware.TpmVersion)}"),
                    ];
                }

                break;
            }

            case SensorHardwareKind.Storage:
            {
                var disk = FindDisk(card, hw);
                var solid = disk?.IsSolidState ?? true;
                var temp = MaxTemp() ?? disk?.TemperatureC;
                SetPrimaryTemp(card, temp, solid ? Thresholds.SsdTempWarn : Thresholds.HddTempWarn, solid ? Thresholds.SsdTempCritical : Thresholds.HddTempCritical, c);
                var life = Find(SensorKind.Level, "Remaining Life", "Available Spare") ?? (disk?.WearPercent is { } w ? 100 - w : null);
                var activity = Find(SensorKind.Load, "Total Activity");
                card.Secondary = Join(life is null ? null : Loc.F("device.life", Format.Percent(life, c)), activity is null ? null : Loc.F("device.busy", Format.Percent(activity, c)));
                if (disk is not null)
                {
                    card.StatusText = Labels.DiskHealth(disk.Health);
                    card.Status = disk.Health switch
                    {
                        DiskHealth.Unhealthy => Severity.Critical,
                        DiskHealth.Warning => Severity.Warning,
                        _ when disk.PredictFailure == true => Severity.Critical,
                        DiskHealth.Healthy => Severity.Ok,
                        _ => Severity.Info,
                    };
                    var errors = (disk.ReadErrorsUncorrected ?? 0) + (disk.WriteErrorsUncorrected ?? 0);
                    card.Info =
                    [
                        new(Loc.T("disk.type"), $"{Labels.MediaType(disk)} · {Format.OrDash(disk.BusType)} · {Format.MarketingSize(disk.SizeBytes, c)}"),
                        new(Loc.T("disk.letters"), disk.DriveLetters.Count > 0 ? string.Join(", ", disk.DriveLetters) : "—"),
                        new(Loc.T("disk.life"), disk.WearPercent is { } wear ? Format.Percent(100 - wear, c) : "—"),
                        new(Loc.T("disk.power_on"), disk.PowerOnHours is { } h ? Loc.F("disk.power_on_value", h, Math.Round(h / 24.0)) : "—"),
                        new(Loc.T("disk.smart"), disk.PredictFailure switch { true => Loc.T("disk.smart.fail"), false => Loc.T("disk.smart.ok"), null => "—" }),
                        new(Loc.T("disk.errors"), errors > 0 ? errors.ToString("N0", c) : Loc.T("disk.no_errors")),
                        new(Loc.T("disk.firmware"), Format.OrDash(disk.Firmware)),
                        new(Loc.T("disk.serial"), Format.OrDash(disk.Serial)),
                    ];
                }

                break;
            }

            case SensorHardwareKind.Battery:
            {
                var battery = hw?.Battery;
                var level = Find(SensorKind.Level, "Charge Level") ?? battery?.ChargePercent;
                card.Primary = Format.Percent(level, c);
                card.PrimaryLabel = battery?.IsCharging == true ? Loc.T("battery.charging") : Loc.T("battery.charge");
                card.PrimarySeverity = level is < 15 ? Severity.Warning : Severity.Ok;
                var wear = battery?.WearPercent;
                card.Secondary = wear is null ? "" : Loc.F("battery.health", Format.Percent(100 - wear, c));
                if (battery is not null)
                {
                    card.StatusText = wear is null ? "" : Loc.F("device.wear", Format.Percent(wear, c));
                    card.Status = wear >= Thresholds.BatteryWearWarn ? Severity.Warning : wear >= Thresholds.BatteryWearInfo ? Severity.Info : Severity.Ok;
                    card.Info =
                    [
                        new(Loc.T("battery.design"), battery.DesignCapacity > 0 ? battery.DesignCapacity.ToString("N0", c) + " mWh" : "—"),
                        new(Loc.T("battery.full"), battery.FullChargeCapacity > 0 ? battery.FullChargeCapacity.ToString("N0", c) + " mWh" : "—"),
                        new(Loc.T("battery.wear"), Format.Percent(wear, c)),
                        new(Loc.T("battery.cycles"), battery.CycleCount?.ToString(c) ?? "—"),
                    ];
                }

                break;
            }

            case null:
            {
                // Vùng nhiệt ACPI (không cần driver).
                SetPrimaryTemp(card, MaxTemp(), 85, 100, c);
                if (card.PrimarySeverity == Severity.Critical) card.PrimarySeverity = Severity.Warning;
                card.PrimaryLabel = Loc.T("health.estimated");
                card.Secondary = Loc.F("device.zones", readings.Count);
                card.Info = [new(Loc.T("device.temp_source"), Loc.T("device.acpi_desc"))];
                break;
            }

            default:
                SetPrimaryTemp(card, MaxTemp(), 80, 95, c);
                break;
        }
    }

    private static string Join(params string?[] parts) => string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p)));

    private static void SetPrimaryTemp(DeviceCard card, float? value, double warn, double critical, CultureInfo c)
    {
        card.Primary = Format.Celsius(value, c);
        card.PrimarySeverity = Thresholds.TemperatureSeverity(value, warn, critical);
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
        // Vùng nhiệt ACPI chỉ là ước lượng -> tối đa "cảnh báo".
        if (r.Id.StartsWith(ThermalZoneReader.IdPrefix, StringComparison.Ordinal))
        {
            var acpi = Thresholds.TemperatureSeverity(r.Value, 85, 100);
            return acpi == Severity.Critical ? Severity.Warning : acpi;
        }

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
        SensorHardwareKind.Motherboard => "",
        SensorHardwareKind.Storage => "",
        SensorHardwareKind.Battery => "",
        SensorHardwareKind.Network => "",
        _ => "",
    };

    internal static string KindTitle(SensorKind kind) => Loc.T(kind switch
    {
        SensorKind.Temperature => "sensor.section.temperature",
        SensorKind.Load => "sensor.section.load",
        SensorKind.Clock => "sensor.section.clock",
        SensorKind.Fan => "sensor.section.fan",
        SensorKind.Voltage => "sensor.section.voltage",
        SensorKind.Power => "sensor.section.power",
        SensorKind.Level => "sensor.section.level",
        SensorKind.Data or SensorKind.SmallData => "sensor.section.data",
        SensorKind.Throughput => "sensor.section.throughput",
        _ => "sensor.section.other",
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
            SensorKind.Data => v.ToString("0.0", c) + " GB",
            SensorKind.SmallData => v.ToString("0", c) + " MB",
            SensorKind.Throughput => Format.Speed(v, c),
            _ => v.ToString("0.##", c),
        };
    }
}

internal static class NullableExtensions
{
    public static float? ToFloat(this double? value) => value is null ? null : (float)value.Value;
}
