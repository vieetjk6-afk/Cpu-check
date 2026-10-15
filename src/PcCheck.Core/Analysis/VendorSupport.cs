using PcCheck.Core.Localization;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>Trang tải driver chính hãng theo nhà sản xuất.</summary>
public static class VendorSupport
{
    private static readonly (string Match, string Url)[] OemPages =
    [
        ("Dell", "https://www.dell.com/support/home"),
        ("Alienware", "https://www.dell.com/support/home"),
        ("HP", "https://support.hp.com/drivers"),
        ("Hewlett", "https://support.hp.com/drivers"),
        ("Lenovo", "https://pcsupport.lenovo.com"),
        ("ASUS", "https://www.asus.com/support/download-center/"),
        ("Acer", "https://www.acer.com/support/drivers-and-manuals"),
        ("MSI", "https://www.msi.com/support/download"),
        ("Micro-Star", "https://www.msi.com/support/download"),
        ("Gigabyte", "https://www.gigabyte.com/Support"),
        ("ASRock", "https://www.asrock.com/support/index.asp"),
        ("Samsung", "https://www.samsung.com/support/"),
        ("Microsoft", "https://support.microsoft.com/surface/download-drivers-and-firmware-for-surface"),
        ("Huawei", "https://consumer.huawei.com/support/"),
        ("LG", "https://www.lg.com/support/software-firmware"),
        ("Toshiba", "https://support.dynabook.com/drivers"),
        ("Dynabook", "https://support.dynabook.com/drivers"),
        ("Razer", "https://mysupport.razer.com"),
        ("Intel", "https://www.intel.com/content/www/us/en/support/detect.html"),
    ];

    public static string? OemDriverPage(string manufacturer)
    {
        if (string.IsNullOrWhiteSpace(manufacturer)) return null;
        foreach (var (match, url) in OemPages)
        {
            if (manufacturer.StartsWith(match, StringComparison.OrdinalIgnoreCase)
                || manufacturer.Contains(" " + match, StringComparison.OrdinalIgnoreCase))
                return url;
        }

        return null;
    }

    public static (string Url, LocText Label)? GpuDriverPage(string gpuName, string vendor)
    {
        var text = gpuName + " " + vendor;
        if (text.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || text.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
            return ("https://www.nvidia.com/Download/index.aspx", L("vendor.nvidia"));
        if (text.Contains("AMD", StringComparison.OrdinalIgnoreCase) || text.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || text.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase))
            return ("https://www.amd.com/en/support/download/drivers.html", L("vendor.amd"));
        if (text.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return ("https://www.intel.com/content/www/us/en/support/detect.html", L("vendor.intel"));
        return null;
    }
}
