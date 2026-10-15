namespace PcCheck.Core.Analysis;

/// <summary>Danh mục nhận diện phần mềm: thiết yếu (không gợi ý gỡ), phần mềm rác / có thể không mong muốn, app Store cài sẵn.</summary>
public static class SoftwareCatalog
{
    public const string ReasonPreinstalledGame = "bloat.reason.game";
    public const string ReasonAdware = "bloat.reason.adware";
    public const string ReasonOptimizer = "bloat.reason.optimizer";
    public const string ReasonPup = "bloat.reason.pup";
    public const string ReasonToolbar = "bloat.reason.toolbar";
    public const string ReasonBrowser = "bloat.reason.browser";
    public const string ReasonTrial = "bloat.reason.trial";
    public const string ReasonCrack = "bloat.reason.crack";
    public const string ReasonStoreApp = "bloat.reason.store";

    private static readonly (string Pattern, string Reason)[] Bloat =
    [
        ("WildTangent", ReasonPreinstalledGame),
        ("Candy Crush", ReasonPreinstalledGame),
        ("Bubble Witch", ReasonPreinstalledGame),
        ("March of Empires", ReasonPreinstalledGame),
        ("Booking.com", ReasonAdware),
        ("Amazon Assistant", ReasonAdware),
        ("Coupon", ReasonAdware),
        ("Driver Booster", ReasonOptimizer),
        ("DriverPack", ReasonOptimizer),
        ("Driver Easy", ReasonOptimizer),
        ("Driver Support", ReasonOptimizer),
        ("DriverFix", ReasonOptimizer),
        ("Driver Updater", ReasonOptimizer),
        ("Advanced SystemCare", ReasonOptimizer),
        ("PC Optimizer", ReasonOptimizer),
        ("PC Cleaner", ReasonOptimizer),
        ("Reimage Repair", ReasonOptimizer),
        ("Restoro", ReasonOptimizer),
        ("Fortect", ReasonOptimizer),
        ("SlimCleaner", ReasonOptimizer),
        ("MyPC Utilities", ReasonOptimizer),
        ("Registry Cleaner", ReasonOptimizer),
        ("Segurazo", ReasonPup),
        ("SAntivirus", ReasonPup),
        ("ByteFence", ReasonPup),
        ("Web Companion", ReasonPup),
        ("Search Protect", ReasonPup),
        ("Conduit", ReasonPup),
        ("OneLaunch", ReasonPup),
        ("Baidu", ReasonPup),
        ("Hao123", ReasonPup),
        ("PDFix", ReasonPup),
        ("Ask Toolbar", ReasonToolbar),
        ("Yahoo! Toolbar", ReasonToolbar),
        ("Yahoo Toolbar", ReasonToolbar),
        ("Babylon", ReasonToolbar),
        ("Wave Browser", ReasonBrowser),
        ("WebDiscover", ReasonBrowser),
        ("Shift Browser", ReasonBrowser),
        ("CCleaner Browser", ReasonBrowser),
        ("AVG Secure Browser", ReasonBrowser),
        ("Avast Secure Browser", ReasonBrowser),
        ("McAfee WebAdvisor", ReasonTrial),
        ("McAfee LiveSafe", ReasonTrial),
        ("Dropbox Promotion", ReasonTrial),
        ("KMSpico", ReasonCrack),
        ("KMSAuto", ReasonCrack),
        ("KMS Tools", ReasonCrack),
    ];

    private static readonly string[] Essential =
    [
        "Microsoft Visual C++", "Visual C++", ".NET", "Microsoft Edge", "WebView2", "Windows Software Development Kit",
        "Windows SDK", "Microsoft Update Health Tools", "Update for Windows", "Desktop Runtime", "ASP.NET", "DirectX",
        "Driver", "Chipset", "Management Engine", "Realtek", "NVIDIA", "AMD Software", "Radeon", "Intel(R)", "Intel®",
        "Synaptics", "ELAN", "Dolby", "Waves", "Java", "Windows PC Health Check", "GameInput", "Microsoft Office",
        "Microsoft 365", "Windows Driver", "Bonjour", "Vulkan", "OpenAL", "PhysX", "Thunderbolt", "Bluetooth",
        "Touchpad", "BIOS", "Firmware", "Microsoft Defender", "Windows Security", "Redistributable", "Runtime",
    ];

    /// <summary>Ứng dụng Store cài sẵn thường ít dùng (tiền tố tên gói -> tên hiển thị).</summary>
    public static readonly IReadOnlyList<(string Prefix, string Friendly)> StoreBloat =
    [
        ("king.com.", "King games (Candy Crush...)"),
        ("Microsoft.BingNews", "Microsoft News"),
        ("Microsoft.BingWeather", "MSN Weather"),
        ("Microsoft.BingFinance", "MSN Money"),
        ("Microsoft.BingSports", "MSN Sports"),
        ("Microsoft.GetHelp", "Get Help"),
        ("Microsoft.Getstarted", "Tips"),
        ("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection"),
        ("Microsoft.People", "People"),
        ("Microsoft.SkypeApp", "Skype"),
        ("Microsoft.WindowsFeedbackHub", "Feedback Hub"),
        ("Microsoft.MixedReality.Portal", "Mixed Reality Portal"),
        ("Microsoft.Microsoft3DViewer", "3D Viewer"),
        ("Microsoft.Print3D", "Print 3D"),
        ("Microsoft.MSPaint", "Paint 3D"),
        ("Microsoft.OneConnect", "Mobile Plans"),
        ("Microsoft.Messaging", "Messaging"),
        ("Microsoft.Wallet", "Microsoft Pay"),
        ("Microsoft.WindowsMaps", "Maps"),
        ("Microsoft.MicrosoftOfficeHub", "Office Hub"),
        ("Microsoft.549981C3F5F10", "Cortana"),
        ("Clipchamp.Clipchamp", "Clipchamp"),
        ("SpotifyAB.SpotifyMusic", "Spotify"),
        ("Disney.", "Disney+"),
        ("Facebook.", "Facebook"),
        ("BytedancePte.Ltd.TikTok", "TikTok"),
        ("AmazonVideo.PrimeVideo", "Prime Video"),
        ("5A894077.McAfeeSecurity", "McAfee"),
        ("Microsoft.ZuneVideo", "Movies & TV"),
        ("MicrosoftTeams", "Microsoft Teams (personal)"),
    ];

    public static string? BloatReason(string name, string publisher)
    {
        foreach (var (pattern, reason) in Bloat)
        {
            if (name.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return reason;
        }

        return null;
    }

    public static bool IsEssential(string name, string publisher)
    {
        if (BloatReason(name, publisher) is not null) return false;
        return Essential.Any(e => name.Contains(e, StringComparison.OrdinalIgnoreCase));
    }

    public static string? StoreBloatName(string packageName)
    {
        foreach (var (prefix, friendly) in StoreBloat)
        {
            if (packageName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return friendly;
        }

        return null;
    }
}
