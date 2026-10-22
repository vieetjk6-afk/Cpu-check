using System.Runtime.CompilerServices;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
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
    private readonly Dictionary<string, (float Min, float Max)> _ranges = new(StringComparer.Ordinal);
    private readonly ThermalZoneReader _thermalZones = new();
    private Computer? _computer;
    private bool _openFailed;
    private bool _disposed;
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
            if (_computer is not null || _openFailed || _disposed) return;
            try
            {
                await Task.Run(Open, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Ví dụ: thiếu thư viện native của LibreHardwareMonitor -> lỗi ngay khi JIT, chỉ thử một lần.
                _openFailed = true;
                Log.Warn("Không tải được LibreHardwareMonitor", ex);
            }
        }
        finally
        {
            ReleaseGate();
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

    /// <summary>
    /// Đọc trực tiếp khóa gỡ cài đặt của PawnIO (giống LibreHardwareMonitor) – không dùng PawnIo.IsInstalled vì giá trị đó
    /// chỉ được đọc một lần khi khởi động, nên sẽ sai ngay sau khi vừa cài driver.
    /// </summary>
    private static bool? ReadPawnIo()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                if (key?.GetValue("DisplayVersion") is string version && !string.IsNullOrWhiteSpace(version)) return true;
            }

            return false;
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

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed) return Latest;
            Latest = await Task.Run(ReadAll, ct).ConfigureAwait(false);
            return Latest;
        }
        finally
        {
            ReleaseGate();
        }
    }

    /// <summary>
    /// Đóng rồi mở lại bộ đọc cảm biến – dùng sau khi vừa cài driver PawnIO để đọc được nhiệt độ CPU mà không cần khởi động lại ứng dụng.
    /// </summary>
    public async Task ReopenAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            try
            {
                _computer?.Close();
            }
            catch (Exception ex)
            {
                Log.Warn("Đóng LibreHardwareMonitor lỗi", ex);
            }

            _computer = null;
            _openFailed = false;
            await Task.Run(Open, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _openFailed = true;
            Log.Warn("Mở lại LibreHardwareMonitor lỗi", ex);
        }
        finally
        {
            ReleaseGate();
        }
    }

    private SensorSnapshot ReadAll()
    {
        var readings = new List<SensorReading>(256);
        if (_computer is not null) ReadHardware(readings);

        // Không có cảm biến nhiệt độ CPU (chưa có driver hoặc LibreHardwareMonitor lỗi) -> dùng vùng nhiệt ACPI của Windows.
        if (SensorSnapshot.PickCpuTemperature(readings) is null) readings.AddRange(_thermalZones.Read());

        ApplyRanges(readings, _ranges);

        return new SensorSnapshot
        {
            Readings = readings,
            PawnIoInstalled = _pawnIo,
            Available = readings.Count > 0,
        };
    }

    /// <summary>
    /// Thấp nhất / cao nhất tự theo dõi từ lúc mở ứng dụng, bỏ qua số đo vô lý. Min/Max của LibreHardwareMonitor giữ cả
    /// lần đọc đầu tiên bằng 0 (vd ổ NVMe) nên "thấp nhất" luôn hiện 0 °C. Nhiệt độ ≤ 0 °C coi như chưa đọc được.
    /// </summary>
    internal static void ApplyRanges(List<SensorReading> readings, Dictionary<string, (float Min, float Max)> ranges)
    {
        for (var i = 0; i < readings.Count; i++)
        {
            var r = readings[i];
            var valid = r.Value is { } v && (r.Kind != SensorKind.Temperature || v is > 0 and < 150);
            if (!valid)
            {
                var hasRange = ranges.TryGetValue(r.Id, out var old);
                readings[i] = r with
                {
                    Value = r.Kind == SensorKind.Temperature ? null : r.Value,
                    Min = hasRange ? old.Min : null,
                    Max = hasRange ? old.Max : null,
                };
                continue;
            }

            var value = r.Value!.Value;
            var range = ranges.TryGetValue(r.Id, out var prev) ? (Math.Min(prev.Min, value), Math.Max(prev.Max, value)) : (value, value);
            ranges[r.Id] = range;
            readings[i] = r with { Min = range.Item1, Max = range.Item2 };
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReadHardware(List<SensorReading> readings)
    {
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
                Sanitize(sensor.Max),
                top.Identifier.ToString()));
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
        SensorType.Data => SensorKind.Data,
        SensorType.SmallData => SensorKind.SmallData,
        SensorType.Throughput => SensorKind.Throughput,
        SensorType.Factor => SensorKind.Counter,
        _ => SensorKind.Other,
    };

    private void ReleaseGate()
    {
        try
        {
            _gate.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Chờ lần đọc cảm biến đang chạy (nếu có) xong rồi mới đóng driver.
        var entered = _gate.Wait(TimeSpan.FromSeconds(3));
        try
        {
            _computer?.Close();
        }
        catch (Exception ex)
        {
            Log.Warn("Đóng LibreHardwareMonitor lỗi", ex);
        }
        finally
        {
            _computer = null;
            if (entered) ReleaseGate();
        }
    }
}
