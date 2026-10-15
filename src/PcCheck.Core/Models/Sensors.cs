namespace PcCheck.Core.Models;

public enum SensorHardwareKind
{
    Cpu,
    Gpu,
    Memory,
    Motherboard,
    Storage,
    Battery,
    Network,
    Other,
}

public enum SensorKind
{
    Temperature,
    Load,
    Clock,
    Fan,
    Voltage,
    Power,
    Level,

    /// <summary>Dung lượng lớn (GB), vd RAM đã dùng.</summary>
    Data,

    /// <summary>Dung lượng nhỏ (MB), vd VRAM đã dùng.</summary>
    SmallData,

    /// <summary>Tốc độ truyền (byte/giây), vd tốc độ đọc/ghi ổ đĩa.</summary>
    Throughput,
    Other,
}

public sealed record SensorReading(
    string Id,
    string Hardware,
    SensorHardwareKind HardwareKind,
    string Name,
    SensorKind Kind,
    float? Value,
    float? Min,
    float? Max);

public sealed class SensorSnapshot
{
    public static readonly SensorSnapshot Empty = new() { Available = false };

    public DateTime Time { get; init; } = DateTime.Now;
    public bool Available { get; init; } = true;

    /// <summary>Driver PawnIO (cần để đọc nhiệt độ CPU qua MSR) đã được cài chưa.</summary>
    public bool? PawnIoInstalled { get; init; }

    public IReadOnlyList<SensorReading> Readings { get; init; } = [];

    public float? CpuTemperature => PickCpuTemperature(Readings);
    public float? CpuLoad => Pick(Readings, SensorHardwareKind.Cpu, SensorKind.Load, "CPU Total", "Total");
    public float? GpuTemperature => Pick(Readings, SensorHardwareKind.Gpu, SensorKind.Temperature, "GPU Core", "GPU Hot Spot", "Core");
    public float? GpuLoad => Pick(Readings, SensorHardwareKind.Gpu, SensorKind.Load, "GPU Core", "D3D 3D", "Core");
    public float? MemoryLoad => Pick(Readings, SensorHardwareKind.Memory, SensorKind.Load, "Memory");

    public float? GpuHotSpot => Pick(Readings, SensorHardwareKind.Gpu, SensorKind.Temperature, "GPU Hot Spot");

    /// <summary>Xung nhịp cao nhất trong các nhân CPU (MHz).</summary>
    public float? CpuClock =>
        Readings.Where(r => r.HardwareKind == SensorHardwareKind.Cpu && r.Kind == SensorKind.Clock && r.Value > 0
                            && !r.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Value)
            .DefaultIfEmpty(null)
            .Max();

    public float? CpuPower => Pick(Readings, SensorHardwareKind.Cpu, SensorKind.Power, "CPU Package", "Package");

    public float? GpuPower => Pick(Readings, SensorHardwareKind.Gpu, SensorKind.Power, "GPU Package", "GPU Power", "GPU Core");

    /// <summary>VRAM đã dùng / tổng (MB).</summary>
    public float? GpuMemoryUsed => Pick(Readings, SensorHardwareKind.Gpu, SensorKind.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");

    public float? GpuMemoryTotal => Pick(Readings, SensorHardwareKind.Gpu, SensorKind.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");

    /// <summary>% thời gian ổ đĩa bận (ổ bận nhất).</summary>
    public float? DiskActivity =>
        Readings.Where(r => r.HardwareKind == SensorHardwareKind.Storage && r.Kind == SensorKind.Load
                            && r.Name.Contains("Activity", StringComparison.OrdinalIgnoreCase) && r.Value is not null)
            .Select(r => r.Value)
            .DefaultIfEmpty(null)
            .Max();

    public float? DiskReadRate => SumThroughput("Read");

    public float? DiskWriteRate => SumThroughput("Write");

    public float? MaxFanRpm =>
        Readings.Where(r => r.Kind == SensorKind.Fan && r.Value > 0).Select(r => r.Value).DefaultIfEmpty(null).Max();

    public int FanCount => Readings.Count(r => r.Kind == SensorKind.Fan && r.Value > 0);

    public float? BatteryLevel => Pick(Readings, SensorHardwareKind.Battery, SensorKind.Level, "Charge Level");

    private float? SumThroughput(string name)
    {
        var list = Readings.Where(r => r.HardwareKind == SensorHardwareKind.Storage && r.Kind == SensorKind.Throughput
                                       && r.Name.Contains(name, StringComparison.OrdinalIgnoreCase) && r.Value is not null).ToList();
        return list.Count == 0 ? null : list.Sum(r => r.Value!.Value);
    }

    public float? MaxStorageTemperature =>
        Readings.Where(r => r.HardwareKind == SensorHardwareKind.Storage && r.Kind == SensorKind.Temperature && r.Value is > 0 and < 150)
            .Select(r => r.Value)
            .DefaultIfEmpty(null)
            .Max();

    /// <summary>Ưu tiên cảm biến tổng (Package / Tctl) rồi mới tới trung bình / cao nhất các nhân.</summary>
    internal static float? PickCpuTemperature(IReadOnlyList<SensorReading> readings)
    {
        var temps = readings
            .Where(r => r.HardwareKind == SensorHardwareKind.Cpu && r.Kind == SensorKind.Temperature && r.Value is > 0 and < 150)
            .ToList();
        if (temps.Count == 0) return null;

        string[] preferred = ["CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Tdie", "Core Average", "Core Max", "CPU Cores"];
        foreach (var name in preferred)
        {
            var hit = temps.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Value;
        }

        return temps.Max(t => t.Value);
    }

    internal static float? Pick(IReadOnlyList<SensorReading> readings, SensorHardwareKind hw, SensorKind kind, params string[] names)
    {
        var candidates = readings.Where(r => r.HardwareKind == hw && r.Kind == kind && r.Value is not null).ToList();
        if (candidates.Count == 0) return null;
        foreach (var name in names)
        {
            var hit = candidates.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit.Value;
        }

        return kind == SensorKind.Temperature ? candidates.Where(c => c.Value is > 0 and < 150).Max(c => c.Value) : candidates[0].Value;
    }
}
