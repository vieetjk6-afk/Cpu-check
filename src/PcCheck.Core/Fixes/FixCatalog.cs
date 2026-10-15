using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Fixes;

/// <summary>Các trang trong ứng dụng mà FixKind.Navigate có thể chuyển tới.</summary>
public static class Pages
{
    public const string Dashboard = "dashboard";
    public const string Hardware = "hardware";
    public const string Health = "health";
    public const string Compatibility = "compatibility";
    public const string Drivers = "drivers";
    public const string Cleanup = "cleanup";
    public const string Software = "software";
    public const string Startup = "startup";
    public const string Issues = "issues";
    public const string Widget = "widget";
    public const string Settings = "settings";
}

/// <summary>Các hành động khắc phục dựng sẵn để analyzer tái sử dụng.</summary>
public static class FixCatalog
{
    public static FixAction Navigate(string page, string labelKey) => new(FixKind.Navigate, page, L(labelKey));

    public static FixAction Uri(string uri, string labelKey) => new(FixKind.OpenUri, uri, L(labelKey));

    public static FixAction Tool(string tool, string labelKey, bool confirm = false) =>
        new(FixKind.RunTool, tool, L(labelKey), confirm) { ConfirmMessage = confirm ? L("tool." + tool + ".confirm") : null };

    public static FixAction CleanupPage => Navigate(Pages.Cleanup, "fix.go_cleanup");
    public static FixAction SoftwarePage => Navigate(Pages.Software, "fix.go_software");
    public static FixAction StartupPage => Navigate(Pages.Startup, "fix.go_startup");
    public static FixAction DriversPage => Navigate(Pages.Drivers, "fix.go_drivers");
    public static FixAction HealthPage => Navigate(Pages.Health, "fix.go_health");
    public static FixAction CompatibilityPage => Navigate(Pages.Compatibility, "fix.go_compatibility");

    public static FixAction WindowsUpdate => Uri("ms-settings:windowsupdate", "fix.windows_update");
    public static FixAction OptionalUpdates => Uri("ms-settings:windowsupdate-optionalupdates", "fix.optional_updates");
    public static FixAction WindowsSecurity => Uri("windowsdefender:", "fix.windows_security");
    public static FixAction FirewallSettings => Uri("windowsdefender://network", "fix.firewall");
    public static FixAction Activation => Uri("ms-settings:activation", "fix.activation");
    public static FixAction StorageSense => Uri("ms-settings:storagesense", "fix.storage_sense");
    public static FixAction DisplaySettings => Uri("ms-settings:display-advanced", "fix.display_settings");
    public static FixAction BackupSettings => Uri("ms-settings:backup", "fix.backup");
    public static FixAction EnableCpuSensor => Tool(SystemTools.CpuSensor, "fix.enable_cpu_sensor", confirm: true);
    public static FixAction Windows11Download => Uri("https://www.microsoft.com/software-download/windows11", "fix.win11_download");

    public static FixAction DeviceManager => Tool(SystemTools.DeviceManager, "tool.devmgmt.name");
    public static FixAction ScanDevices => Tool(SystemTools.ScanDevices, "tool.scan-devices.name");
    public static FixAction SfcDism => Tool(SystemTools.SfcDism, "tool.sfc-dism.name", confirm: true);
    public static FixAction MemoryDiagnostic => Tool(SystemTools.MemoryDiagnostic, "tool.mdsched.name", confirm: true);
    public static FixAction ReliabilityMonitor => Tool(SystemTools.ReliabilityMonitor, "tool.reliability.name");
    public static FixAction PowerOptions => Tool(SystemTools.PowerOptions, "tool.powercfg.name");
    public static FixAction UacSettings => Tool(SystemTools.Uac, "tool.uac.name");
    public static FixAction PerformanceOptions => Tool(SystemTools.PerformanceOptions, "tool.performance.name");
    public static FixAction DiskCleanup => Tool(SystemTools.DiskCleanup, "tool.cleanmgr.name");

    public static FixAction Chkdsk(string drive) =>
        new(FixKind.RunTool, SystemTools.Chkdsk + ":" + drive.TrimEnd(':', '\\'), L("tool.chkdsk.name"), true)
        {
            ConfirmMessage = L("tool.chkdsk.confirm"),
        };

    public static FixAction Restart => new(FixKind.Restart, null, L("fix.restart"), true) { ConfirmMessage = L("fix.restart.confirm") };

    public static FixAction SearchDriver(DeviceInfo device) =>
        new(FixKind.OpenUri, "https://www.google.com/search?q=" + global::System.Uri.EscapeDataString(device.SearchId + " driver"), L("fix.search_driver"));

    public static FixAction VendorDrivers(string url) => new(FixKind.OpenUri, url, L("fix.vendor_drivers"));
}
