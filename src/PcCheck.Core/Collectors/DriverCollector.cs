using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

/// <summary>Liệt kê thiết bị Plug and Play và driver đang dùng.</summary>
public static class DriverCollector
{
    public static List<DeviceInfo> Collect()
    {
        List<WmiRow> entities = [];
        List<WmiRow> drivers = [];
        Parallel.Invoke(
            () => entities = Wmi.Query("SELECT Name, PNPDeviceID, PNPClass, Manufacturer, ConfigManagerErrorCode, Status, Service, HardwareID FROM Win32_PnPEntity", timeoutSeconds: 30),
            () => drivers = Wmi.Query("SELECT DeviceName, DeviceID, DeviceClass, DriverVersion, DriverDate, DriverProviderName, IsSigned, InfName FROM Win32_PnPSignedDriver", timeoutSeconds: 45));

        var driverById = new Dictionary<string, WmiRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in drivers)
        {
            var id = d.Str("DeviceID");
            if (id.Length > 0) driverById.TryAdd(id, d);
        }

        var devices = new List<DeviceInfo>(entities.Count);
        foreach (var e in entities)
        {
            var device = new DeviceInfo
            {
                Name = e.Str("Name"),
                DeviceId = e.Str("PNPDeviceID"),
                ClassName = e.Str("PNPClass"),
                Manufacturer = e.Str("Manufacturer"),
                ErrorCode = e.Int("ConfigManagerErrorCode") ?? 0,
                Status = e.Str("Status"),
                Service = e.Str("Service"),
                HardwareIds = e.StrArray("HardwareID"),
            };

            if (driverById.TryGetValue(device.DeviceId, out var drv))
            {
                device.DriverVersion = drv.Str("DriverVersion");
                device.DriverDate = drv.Date("DriverDate");
                device.DriverProvider = drv.Str("DriverProviderName");
                device.IsSigned = drv.Bool("IsSigned");
                device.InfName = drv.Str("InfName");
                if (string.IsNullOrEmpty(device.ClassName)) device.ClassName = drv.Str("DeviceClass");
                if (string.IsNullOrEmpty(device.Name)) device.Name = drv.Str("DeviceName");
            }

            if (string.IsNullOrWhiteSpace(device.Name)) device.Name = device.DeviceId;
            devices.Add(device);
        }

        return devices;
    }
}
