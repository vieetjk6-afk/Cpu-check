using System.Diagnostics;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Sensors;

/// <summary>
/// Nhiệt độ vùng nhiệt ACPI do Windows cung cấp sẵn – KHÔNG cần cài driver.
/// Đa số laptop và một số máy bàn có; giá trị gần với nhiệt độ CPU/bo mạch nên được ghi là "ước lượng".
/// </summary>
public sealed class ThermalZoneReader
{
    public const string IdPrefix = "/acpi/";
    public const string HardwareName = "ACPI Thermal Zone";

    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RetryWhenMissing = TimeSpan.FromMinutes(2);

    private readonly Stopwatch _sinceRead = new();
    private IReadOnlyList<SensorReading> _cached = [];
    private bool _unsupported;

    /// <summary>Đọc (có bộ đệm vài giây vì truy vấn WMI tốn khoảng chục ms).</summary>
    public IReadOnlyList<SensorReading> Read()
    {
        var wait = _unsupported ? RetryWhenMissing : CacheTime;
        if (_sinceRead.IsRunning && _sinceRead.Elapsed < wait) return _cached;
        _sinceRead.Restart();

        try
        {
            var zones = ReadPerformanceCounters();
            if (zones.Count == 0) zones = ReadAcpiWmi();
            _unsupported = zones.Count == 0;
            _cached = zones;
        }
        catch (Exception ex)
        {
            Log.Warn("Đọc vùng nhiệt ACPI lỗi", ex);
            _cached = [];
            _unsupported = true;
        }

        return _cached;
    }

    /// <summary>Bộ đếm hiệu năng "Thermal Zone Information" (Windows 8+): HighPrecisionTemperature = 1/10 Kelvin.</summary>
    private static List<SensorReading> ReadPerformanceCounters() =>
        Wmi.Query("SELECT Name, HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation", Wmi.Cimv2, 5)
            .Select(r =>
            {
                var tenths = r.Long("HighPrecisionTemperature") ?? 0;
                var kelvin = r.Long("Temperature") ?? 0;
                var celsius = tenths > 0 ? tenths / 10.0 - 273.15 : kelvin > 0 ? kelvin - 273.15 : double.NaN;
                return Make(ZoneName(r.Str("Name")), celsius);
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

    /// <summary>MSAcpi_ThermalZoneTemperature (root\wmi): CurrentTemperature = 1/10 Kelvin.</summary>
    private static List<SensorReading> ReadAcpiWmi() =>
        Wmi.Query("SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", Wmi.RootWmi, 5)
            .Select(r =>
            {
                var tenths = r.Long("CurrentTemperature") ?? 0;
                return Make(ZoneName(r.Str("InstanceName")), tenths > 0 ? tenths / 10.0 - 273.15 : double.NaN);
            })
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

    internal static SensorReading? Make(string zone, double celsius)
    {
        if (double.IsNaN(celsius) || celsius is < 10 or > 115) return null;
        var value = (float)Math.Round(celsius, 1);
        return new SensorReading(IdPrefix + zone, HardwareName, SensorHardwareKind.Motherboard, zone, SensorKind.Temperature, value, null, null);
    }

    /// <summary>"\_TZ.TZ00" hoặc "ACPI\ThermalZone\TZ00_0" -> "TZ00".</summary>
    internal static string ZoneName(string raw)
    {
        var name = raw.Trim();
        var cut = name.LastIndexOfAny(['\\', '.']);
        if (cut >= 0 && cut < name.Length - 1) name = name[(cut + 1)..];
        if (name.EndsWith("_0", StringComparison.Ordinal)) name = name[..^2];
        return string.IsNullOrEmpty(name) ? "Zone" : name;
    }
}
