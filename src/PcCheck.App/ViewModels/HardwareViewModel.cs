using System.Globalization;
using System.Text;
using System.Windows;
using PcCheck.App.Infrastructure;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.App.ViewModels;

/// <summary>Trang thông tin phần cứng chi tiết.</summary>
public sealed class HardwareViewModel : PageViewModel
{
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private IReadOnlyList<InfoSection> _sections = [];

    public HardwareViewModel(MainViewModel main) : base(main)
    {
        CopyCommand = new RelayCommand(Copy, () => Sections.Count > 0);
        ExpandAllCommand = new RelayCommand(() => SetAllExpanded(true), () => Sections.Count > 0);
        CollapseAllCommand = new RelayCommand(() => SetAllExpanded(false), () => Sections.Count > 0);
    }

    public IReadOnlyList<InfoSection> Sections { get => _sections; private set => Set(ref _sections, value); }

    public RelayCommand CopyCommand { get; }
    public RelayCommand ExpandAllCommand { get; }
    public RelayCommand CollapseAllCommand { get; }

    private void SetAllExpanded(bool expanded)
    {
        foreach (var section in Sections) section.IsExpanded = expanded;
    }

    protected override void Rebuild()
    {
        if (State.Result is not { } result)
        {
            Sections = [];
            return;
        }

        var hw = result.Data.Hardware;
        var c = Loc.Instance.Culture;
        var sections = new List<InfoSection>();

        // Hệ thống
        sections.Add(new InfoSection("", "hw.section.system", $"{hw.Computer.Manufacturer} {hw.Computer.Model}".Trim(),
        [
            new(Loc.T("hw.os"), $"{hw.Os.Caption} {hw.Os.DisplayVersion}".Trim()),
            new(Loc.T("hw.build"), $"{hw.Os.Build}.{hw.Os.Revision} ({(hw.Os.Is64Bit ? "64-bit" : "32-bit")})"),
            new(Loc.T("hw.machine_name"), hw.Os.MachineName),
            new(Loc.T("hw.user"), hw.Os.UserName),
            new(Loc.T("hw.installed"), Format.Date(hw.Os.InstallDate, c)),
            new(Loc.T("hw.last_boot"), hw.Os.LastBoot?.ToString("g", c) ?? "—"),
            new(Loc.T("hw.type"), hw.Computer.IsLaptop ? Loc.T("hw.laptop") : Loc.T("hw.desktop")),
            new(Loc.T("hw.firmware"), $"{(hw.Firmware.IsUefi == true ? "UEFI" : hw.Firmware.IsUefi == false ? "Legacy BIOS" : "—")} · Secure Boot: {Labels.YesNo(hw.Firmware.SecureBootEnabled)} · TPM: {Format.OrDash(hw.Firmware.TpmVersion)}"),
        ]));

        // CPU
        foreach (var cpu in hw.Cpus)
        {
            sections.Add(new InfoSection("", "hw.cpu", cpu.Name,
            [
                new(Loc.T("hw.cpu.vendor"), cpu.Manufacturer),
                new(Loc.T("hw.cpu.cores"), Loc.F("hw.cpu.cores_value", cpu.Cores, cpu.Threads)),
                new(Loc.T("hw.cpu.clock"), cpu.MaxClockMhz > 0 ? Format.Mhz(cpu.MaxClockMhz, c) : "—"),
                new(Loc.T("hw.cpu.cache"), $"L2 {Format.Bytes(cpu.L2CacheKb * 1024L, c)} · L3 {Format.Bytes(cpu.L3CacheKb * 1024L, c)}"),
                new(Loc.T("hw.cpu.socket"), Format.OrDash(cpu.Socket)),
                new(Loc.T("hw.cpu.virtualization"), Labels.YesNo(cpu.VirtualizationEnabled == true || hw.Computer.HypervisorPresent ? true : cpu.VirtualizationEnabled)),
                new(Loc.T("hw.cpu.temp_now"), result.Data.Sensors.CpuTemperatureEstimated
                    ? Loc.F("dash.temp_estimated", Format.Celsius(result.Data.Sensors.CpuTemperature, c))
                    : Format.Celsius(result.Data.Sensors.CpuTemperature, c)),
            ]));
        }

        // Mainboard
        sections.Add(new InfoSection("", "hw.mainboard", $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim(),
        [
            new(Loc.T("hw.board.vendor"), Format.OrDash(hw.Board.Manufacturer)),
            new(Loc.T("hw.board.model"), Format.OrDash(hw.Board.Product)),
            new(Loc.T("hw.board.version"), Format.OrDash(hw.Board.Version)),
            new(Loc.T("hw.bios"), $"{hw.Board.BiosVendor} {hw.Board.BiosVersion}".Trim()),
            new(Loc.T("hw.bios_date"), Format.Date(hw.Board.BiosDate, c)),
        ]));

        // RAM
        var ramGroups = hw.Memory.Modules.Select(m => new InfoGroup(
            Format.OrDash(m.Slot), $"{Format.Bytes(m.CapacityBytes, c)} {m.Type} {m.FormFactor}".Trim(),
            [
                new(Loc.T("ram.speed"), m.ConfiguredSpeedMhz > 0 && m.ConfiguredSpeedMhz != m.SpeedMhz ? $"{m.ConfiguredSpeedMhz} / {m.SpeedMhz} MHz" : $"{m.SpeedMhz} MHz"),
                new(Loc.T("ram.vendor"), Format.OrDash(m.Manufacturer)),
                new(Loc.T("ram.part"), Format.OrDash(m.PartNumber)),
                new(Loc.T("ram.bank"), Format.OrDash(m.Bank)),
            ])).ToList();
        sections.Add(new InfoSection("", "hw.ram", Format.Bytes(hw.Memory.InstalledBytes, c),
        [
            new(Loc.T("hw.ram.usable"), Format.Bytes(hw.Memory.TotalBytes, c)),
            new(Loc.T("hw.ram.slots"), hw.Memory.SlotCount > 0 ? Loc.F("hw.ram.slots_value", hw.Memory.Modules.Count, hw.Memory.SlotCount) : hw.Memory.Modules.Count.ToString(c)),
            new(Loc.T("hw.ram.max"), hw.Memory.MaxCapacityBytes > 0 ? Format.Bytes(hw.Memory.MaxCapacityBytes, c) : "—"),
            new(Loc.T("hw.ram.used"), Format.Percent(hw.Memory.UsedPercent, c)),
        ], ramGroups));

        // GPU
        var gpuGroups = hw.Gpus.Where(g => !g.IsVirtual || hw.Gpus.Count == 1).Select(g => new InfoGroup(
            g.Name, g.Vendor,
            [
                new(Loc.T("gpu.vram"), g.VramBytes > 0 ? Format.Bytes(g.VramBytes, c) : "—"),
                new(Loc.T("gpu.driver"), Format.OrDash(g.DriverVersion)),
                new(Loc.T("gpu.driver_date"), Format.Date(g.DriverDate, c)),
                new(Loc.T("gpu.mode"), g.ResolutionX > 0 ? $"{g.ResolutionX}×{g.ResolutionY} @ {g.RefreshRate} Hz" : "—"),
            ],
            g.IsBasicDriver || g.ErrorCode != 0 ? Severity.Critical : Severity.Ok,
            g.IsBasicDriver ? Loc.T("driver.kind.missing") : g.ErrorCode != 0 ? $"Code {g.ErrorCode}" : Loc.T("common.ok"))).ToList();
        sections.Add(new InfoSection("", "hw.gpu", Loc.F("hw.count", gpuGroups.Count), [], gpuGroups));

        // Ổ cứng
        var diskGroups = hw.Disks.Select(d => new InfoGroup(
            Format.OrDash(d.Model), $"{Labels.MediaType(d)} · {Format.OrDash(d.BusType)} · {Format.MarketingSize(d.SizeBytes, c)}",
            [
                new(Loc.T("disk.health"), Labels.DiskHealth(d.Health)),
                new(Loc.T("disk.temperature"), Format.Celsius(d.TemperatureC, c)),
                new(Loc.T("disk.life"), d.WearPercent is { } w ? Format.Percent(100 - w, c) : "—"),
                new(Loc.T("disk.power_on"), d.PowerOnHours is { } h ? Loc.F("unit.hours", h) : "—"),
                new(Loc.T("disk.partition"), Format.OrDash(d.PartitionStyle)),
                new(Loc.T("disk.letters"), d.DriveLetters.Count > 0 ? string.Join(", ", d.DriveLetters) : "—"),
                new(Loc.T("disk.firmware"), Format.OrDash(d.Firmware)),
                new(Loc.T("disk.serial"), Format.OrDash(d.Serial)),
            ],
            d.Health switch { DiskHealth.Unhealthy => Severity.Critical, DiskHealth.Warning => Severity.Warning, DiskHealth.Healthy => Severity.Ok, _ => Severity.Info },
            Labels.DiskHealth(d.Health))).ToList();
        var volumes = hw.Volumes.Select(v => new InfoItem(
            $"{v.Letter} {v.Label}".Trim(),
            Loc.F("volume.usage", Format.Bytes(v.FreeBytes, c), Format.Bytes(v.TotalBytes, c), v.FileSystem))).ToList();
        sections.Add(new InfoSection("", "hw.storage", Loc.F("hw.count", diskGroups.Count), volumes, diskGroups));

        // Màn hình
        if (hw.Monitors.Count > 0)
        {
            var monitorGroups = hw.Monitors.Select(m => new InfoGroup(
                Format.OrDash(m.Name), m.IsPrimary ? Loc.T("monitor.primary") : m.DeviceName,
                [
                    new(Loc.T("monitor.resolution"), $"{m.Width}×{m.Height}"),
                    new(Loc.T("monitor.refresh"), $"{m.RefreshRate} Hz"),
                    new(Loc.T("monitor.max_refresh"), m.MaxRefreshAtCurrent > 0 ? $"{m.MaxRefreshAtCurrent} Hz" : "—"),
                    new(Loc.T("monitor.max_resolution"), m.MaxWidth > 0 ? $"{m.MaxWidth}×{m.MaxHeight}" : "—"),
                ])).ToList();
            sections.Add(new InfoSection("", "hw.monitors", Loc.F("hw.count", monitorGroups.Count), [], monitorGroups));
        }

        // Mạng
        if (hw.Networks.Count > 0)
        {
            var netGroups = hw.Networks.Select(n => new InfoGroup(
                n.Name, n.IsWireless ? "Wi-Fi" : "Ethernet",
                [
                    new(Loc.T("net.status"), n.Connected ? Loc.T("net.connected") : Loc.T("net.disconnected")),
                    new(Loc.T("net.speed"), n.SpeedBps > 0 ? FormatSpeed(n.SpeedBps, c) : "—"),
                    new(Loc.T("net.mac"), Format.OrDash(n.MacAddress)),
                ],
                n.Connected ? Severity.Ok : Severity.Info,
                n.Connected ? Loc.T("net.connected") : Loc.T("net.disconnected"))).ToList();
            sections.Add(new InfoSection("", "hw.network", Loc.F("hw.count", netGroups.Count), [], netGroups));
        }

        // Âm thanh
        if (hw.AudioDevices.Count > 0)
        {
            sections.Add(new InfoSection("", "hw.audio", Loc.F("hw.count", hw.AudioDevices.Count),
                hw.AudioDevices.Select(a => new InfoItem(a.Name, $"{a.Manufacturer} · {a.Status}")).ToList()));
        }

        // Pin
        if (hw.Battery is { } battery)
        {
            sections.Add(new InfoSection("", "hw.battery", Format.OrDash(battery.Name),
            [
                new(Loc.T("battery.charge"), Format.Percent(battery.ChargePercent, c) + (battery.IsCharging == true ? " · " + Loc.T("battery.charging") : "")),
                new(Loc.T("battery.design"), battery.DesignCapacity > 0 ? battery.DesignCapacity.ToString("N0", c) + " mWh" : "—"),
                new(Loc.T("battery.full"), battery.FullChargeCapacity > 0 ? battery.FullChargeCapacity.ToString("N0", c) + " mWh" : "—"),
                new(Loc.T("battery.wear"), Format.Percent(battery.WearPercent, c)),
                new(Loc.T("battery.cycles"), battery.CycleCount?.ToString(c) ?? "—"),
            ]));
        }

        // Giữ trạng thái thu gọn của từng mục khi dựng lại (quét lại / đổi ngôn ngữ).
        foreach (var section in Sections) _expanded[section.Key] = section.IsExpanded;
        foreach (var section in sections)
        {
            if (_expanded.TryGetValue(section.Key, out var expanded)) section.IsExpanded = expanded;
        }

        Sections = sections;
    }

    private static string FormatSpeed(long bps, CultureInfo c) =>
        bps >= 1_000_000_000 ? (bps / 1_000_000_000d).ToString("0.#", c) + " Gbps" : (bps / 1_000_000d).ToString("0", c) + " Mbps";

    private void Copy()
    {
        var sb = new StringBuilder();
        foreach (var section in Sections)
        {
            sb.AppendLine($"== {section.Title}: {section.Subtitle}");
            foreach (var item in section.Items) sb.AppendLine($"   {item.Label}: {item.Value}");
            foreach (var group in section.Groups)
            {
                sb.AppendLine($"   - {group.Title} ({group.Subtitle})");
                foreach (var item in group.Items) sb.AppendLine($"       {item.Label}: {item.Value}");
            }

            sb.AppendLine();
        }

        try
        {
            Clipboard.SetText(sb.ToString());
            Main.ShowToast(Loc.T("hw.copied"));
        }
        catch (Exception ex)
        {
            Log.Warn("Sao chép clipboard lỗi", ex);
            Main.ShowToast(Loc.T("hw.copy_failed"), true);
        }
    }
}
