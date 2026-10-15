namespace PcCheck.Core.Models;

public sealed class DeviceInfo
{
    public string Name { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public int ErrorCode { get; set; }
    public string Status { get; set; } = "";
    public string Service { get; set; } = "";
    public string[] HardwareIds { get; set; } = [];

    public string DriverVersion { get; set; } = "";
    public DateTime? DriverDate { get; set; }
    public string DriverProvider { get; set; } = "";
    public bool? IsSigned { get; set; }
    public string InfName { get; set; } = "";

    public bool HasDriver => !string.IsNullOrEmpty(Service) || !string.IsNullOrEmpty(DriverVersion);
    public bool IsMicrosoftDriver => DriverProvider.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);

    /// <summary>Hardware ID rút gọn (vd PCI\VEN_8086&amp;DEV_A2BA) để tra cứu driver trên mạng.</summary>
    public string SearchId
    {
        get
        {
            var id = HardwareIds.FirstOrDefault() ?? DeviceId;
            if (string.IsNullOrEmpty(id)) return Name;
            var parts = id.Split('&');
            return parts.Length > 2 ? string.Join('&', parts.Take(2)) : id;
        }
    }
}

public enum DriverProblemKind
{
    /// <summary>Thiết bị báo lỗi (Code khác 0).</summary>
    DeviceError,

    /// <summary>Chưa cài driver (Code 28 hoặc thiết bị không xác định).</summary>
    Missing,

    /// <summary>Đang dùng driver chung (Basic Display, HD Audio chung...).</summary>
    Generic,

    /// <summary>Driver cũ.</summary>
    Outdated,

    /// <summary>Driver không có chữ ký số.</summary>
    Unsigned,

    /// <summary>Thiết bị bị vô hiệu hóa.</summary>
    Disabled,
}

public sealed record DriverProblem(DeviceInfo Device, DriverProblemKind Kind, Severity Severity);
