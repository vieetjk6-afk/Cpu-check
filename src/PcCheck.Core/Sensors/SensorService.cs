using System.Runtime.CompilerServices;
using LibreHardwareMonitor.Hardware;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Sensors;

/// <summary>
/// Đọc cảm biến (nhiệt độ, tải, quạt, xung nhịp...) qua LibreHardwareMonitor.
/// Mở 1 lần, cập nhật theo yêu cầu; mọi lỗi đều được nuốt để không làm treo ứng dụng.
/// Lưu ý: kiểu của LibreHardwareMonitor chỉ xuất hiện trong lớp này để phần còn lại chạy được cả khi thiếu thư viện native.
/// </summary>
public sealed class SensorService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Computer? _computer;
    private bool _openFailed;
    private bool? _pawnIo;

    public SensorSnapshot Latest { get; private set; } = SensorSnapshot.Empty;

    public bool IsOpen => _computer is not null;

    /// <summary>Mở bộ đọc cảm biến (tốn 1-3 giây lần đầu) – gọi từ luồng nền.</summary>
    public async Task EnsureOpenAsync(CancellationToken ct = default)
    {
        if (_computer is not null || _openFailed) return;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_computer is not null || _openFailed) return;
            await Task.Run(Open, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Open()
    {
        try
        {
            _pawnIo = ReadPawnIo();
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsBatteryEnabled = true,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsPsuEnabled = false,
            };
            computer.Open();
            _computer = computer;
        }
        catch (Exception ex)
        {
            _openFailed = true;
            Log.Warn("Không mở được LibreHardwareMonitor", ex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool? ReadPawnIo()
    {
        try
        {
            return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
        }
        catch (Exception ex)
        {
            Log.Warn("Không kiểm tra được PawnIO", ex);
            return null;
        }
    }

    /// <summary>Cập nhật và trả về ảnh chụp cảm biến mới nhất.</summary>
    public async Task<SensorSnapshot> RefreshAsync(CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct).ConfigureAwait(false);
        if (_computer is null) return Latest = new SensorSnapshot { Available = false, PawnIoInstalled = _pawnIo };

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Latest = await Task.Run(ReadAll, ct).ConfigureAwait(false);
            return Latest;
        }
        finally
        {
            _gate.Release();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private SensorSnapshot ReadAll()
    {
        var readings = new List<SensorReading>(256);
        try
        {
            foreach (var hardware in _computer!.Hardware)
            {
                Collect(hardware, readings);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc cảm biến lỗi", ex);
        }

        return new SensorSnapshot { Readings = readings, PawnIoInstalled = _pawnIo, Available = true };
    }

    private static void Collect(IHardware hardware, List<SensorReading> readings, IHardware? root = null)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception ex)
        {
            Log.Warn($"Update {hardware.Name} lỗi", ex);
            return;
        }

        var top = root ?? hardware;
        var kind = MapHardware(top.HardwareType);
        foreach (var sensor in hardware.Sensors)
        {
            var sensorKind = MapSensor(sensor.SensorType);
            if (sensorKind == SensorKind.Other) continue;
            readings.Add(new SensorReading(
                sensor.Identifier.ToString(),
                top.Name,
                kind,
                sensor.Name,
                sensorKind,
                Sanitize(sensor.Value),
                Sanitize(sensor.Min),
                Sanitize(sensor.Max)));
        }

        foreach (var sub in hardware.SubHardware)
        {
            Collect(sub, readings, top);
        }
    }

    private static float? Sanitize(float? value) =>
        value is null || float.IsNaN(value.Value) || float.IsInfinity(value.Value) ? null : value;

    private static SensorHardwareKind MapHardware(HardwareType type) => type switch
    {
        HardwareType.Cpu => SensorHardwareKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => SensorHardwareKind.Gpu,
        HardwareType.Memory => SensorHardwareKind.Memory,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => SensorHardwareKind.Motherboard,
        HardwareType.Storage => SensorHardwareKind.Storage,
        HardwareType.Battery => SensorHardwareKind.Battery,
        HardwareType.Network => SensorHardwareKind.Network,
        _ => SensorHardwareKind.Other,
    };

    private static SensorKind MapSensor(SensorType type) => type switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Power => SensorKind.Power,
        SensorType.Level => SensorKind.Level,
        SensorType.Data or SensorType.SmallData => SensorKind.Data,
        _ => SensorKind.Other,
    };

    /// <summary>Nhiệt độ ACPI (dự phòng khi không đọc được cảm biến CPU) – thường kém chính xác.</summary>
    public static float? ReadAcpiTemperature()
    {
        var temps = Wmi.Query("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", Wmi.RootWmi, 5)
            .Select(r => r.Long("CurrentTemperature"))
            .Where(t => t is > 2732 and < 4000)
            .Select(t => (float)((t!.Value - 2732) / 10.0))
            .ToList();
        return temps.Count == 0 ? null : temps.Max();
    }

    public void Dispose()
    {
        try
        {
            _computer?.Close();
        }
        catch (Exception ex)
        {
            Log.Warn("Đóng LibreHardwareMonitor lỗi", ex);
        }

        _computer = null;
        _gate.Dispose();
    }
}
