using PcCheck.Core.Localization;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Fixes;

public sealed record SystemTool(string Key, LocText Name, LocText Description, string Glyph, bool RequiresConfirmation);

/// <summary>Công cụ sửa lỗi tích hợp sẵn của Windows mà ứng dụng có thể chạy giúp.</summary>
public static class SystemTools
{
    public const string SfcDism = "sfc-dism";
    public const string Chkdsk = "chkdsk";
    public const string MemoryDiagnostic = "mdsched";
    public const string RestorePoint = "restore-point";
    public const string FlushDns = "flushdns";
    public const string NetworkReset = "network-reset";
    public const string ScanDevices = "scan-devices";
    public const string DeviceManager = "devmgmt";
    public const string EventViewer = "eventvwr";
    public const string ReliabilityMonitor = "reliability";
    public const string DiskCleanup = "cleanmgr";
    public const string PowerOptions = "powercfg";
    public const string Uac = "uac";
    public const string PerformanceOptions = "performance";
    public const string WindowsUpdateReset = "wu-reset";
    public const string BatteryReport = "battery-report";
    public const string StoreReset = "wsreset";

    /// <summary>Cài driver PawnIO để đọc nhiệt độ CPU chính xác (tải bản chính thức, kiểm tra chữ ký, cài im lặng).</summary>
    public const string CpuSensor = "cpu-sensor";

    /// <summary>Danh sách hiển thị ở mục "Công cụ sửa lỗi nhanh". Glyph là mã Segoe MDL2 Assets.</summary>
    public static IReadOnlyList<SystemTool> QuickTools { get; } =
    [
        Make(SfcDism, "", true),
        Make(Chkdsk, "", true),
        Make(MemoryDiagnostic, "", true),
        Make(RestorePoint, "", true),
        Make(FlushDns, "", false),
        Make(NetworkReset, "", true),
        Make(WindowsUpdateReset, "", true),
        Make(ScanDevices, "", false),
        Make(StoreReset, "", false),
        Make(BatteryReport, "", false),
        Make(DiskCleanup, "", false),
        Make(ReliabilityMonitor, "", false),
        Make(EventViewer, "", false),
        Make(DeviceManager, "", false),
    ];

    private static SystemTool Make(string key, string glyph, bool confirm) =>
        new(key, L($"tool.{key}.name"), L($"tool.{key}.desc"), glyph, confirm);
}
