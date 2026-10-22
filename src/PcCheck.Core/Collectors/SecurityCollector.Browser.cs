using System.Text.Json;
using Microsoft.Win32;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Collectors;

public static partial class SecurityCollector
{
    /// <summary>Trình duyệt nhân Chromium: tên, thư mục dữ liệu (dưới %LocalAppData%), khóa chính sách.</summary>
    private static readonly (string Name, string UserData, string Policy)[] Chromium =
    [
        ("Chrome", @"Google\Chrome\User Data", @"Google\Chrome"),
        ("Edge", @"Microsoft\Edge\User Data", @"Microsoft\Edge"),
        ("Brave", @"BraveSoftware\Brave-Browser\User Data", @"BraveSoftware\Brave"),
        ("Cốc Cốc", @"CocCoc\Browser\User Data", @"CocCoc\Browser"),
        ("Vivaldi", @"Vivaldi\User Data", @"Vivaldi"),
    ];

    /// <summary>Tiện ích có sẵn trong trình duyệt (không phải người dùng cài).</summary>
    private static readonly HashSet<string> BuiltInExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ghbmnnjooekpmoecnnnilnnbdlolhkhi", // Google Docs Offline
        "nmmhkkegccagdldgiimedpiccmgmieda", // Chrome Web Store Payments
        "pkedcjkdefgpdelpbcmbmeomcjbeemfm", // Chrome Media Router
        "jmjflgjpcpepeafmmgdpfkogkghcpiha", // Edge Shopping
        "aapbdbdomjkkjkaonfhkkikfgjllcleb", // Google Translate
        "mhjfbmdgcfjbbpaeojofohoefgiehjai", // Chrome PDF Viewer
    };

    private static List<BrowserPolicy> ReadBrowserPolicies()
    {
        var list = new List<BrowserPolicy>();
        foreach (var (name, _, policy) in Chromium)
        {
            foreach (var (hive, scope) in new[] { (Registry.LocalMachine, "HKLM"), (Registry.CurrentUser, "HKCU") })
            {
                try
                {
                    using var key = hive.OpenSubKey(@"SOFTWARE\Policies\" + policy);
                    if (key is null) continue;
                    foreach (var value in key.GetValueNames().Where(v => v.Length > 0))
                        list.Add(new BrowserPolicy(name, value, Convert.ToString(key.GetValue(value)) ?? "", scope));

                    using var forced = key.OpenSubKey("ExtensionInstallForcelist");
                    foreach (var value in forced?.GetValueNames() ?? [])
                        list.Add(new BrowserPolicy(name, "ExtensionInstallForcelist", Convert.ToString(forced!.GetValue(value)) ?? "", scope));
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                }
            }
        }

        return list;
    }

    private static List<BrowserExtension> ReadExtensions(List<BrowserPolicy> policies)
    {
        var forced = policies.Where(p => p.Name == "ExtensionInstallForcelist")
            .Select(p => (p.Browser, Id: p.Value.Split(';')[0].Trim()))
            .ToHashSet();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var list = new List<BrowserExtension>();

        foreach (var (browser, userData, _) in Chromium)
        {
            var root = Path.Combine(local, userData);
            if (!Directory.Exists(root)) continue;
            foreach (var profile in Directory.EnumerateDirectories(root).Where(IsProfile))
            {
                var extensions = Path.Combine(profile, "Extensions");
                if (!Directory.Exists(extensions)) continue;
                foreach (var dir in Directory.EnumerateDirectories(extensions))
                {
                    var id = Path.GetFileName(dir);
                    if (BuiltInExtensions.Contains(id) || id.Length != 32) continue;
                    var version = Directory.EnumerateDirectories(dir).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (version is null) continue;
                    var ext = ReadManifest(version);
                    if (ext is null) continue;
                    ext.Browser = browser;
                    ext.Profile = Path.GetFileName(profile);
                    ext.Id = id;
                    ext.ForceInstalled = forced.Contains((browser, id));
                    list.Add(ext);
                }
            }
        }

        return list;
    }

    private static bool IsProfile(string dir)
    {
        var name = Path.GetFileName(dir);
        return name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
    }

    private static BrowserExtension? ReadManifest(string versionDir)
    {
        try
        {
            var path = Path.Combine(versionDir, "manifest.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (name.StartsWith("__MSG_", StringComparison.Ordinal)) name = Localized(versionDir, root, name) ?? name;

            var permissions = new List<string>();
            foreach (var prop in new[] { "permissions", "host_permissions" })
            {
                if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                permissions.AddRange(arr.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!));
            }

            if (root.TryGetProperty("content_scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Array)
            {
                foreach (var script in scripts.EnumerateArray())
                {
                    if (script.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
                        permissions.AddRange(matches.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!));
                }
            }

            return new BrowserExtension
            {
                Name = name,
                Version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : Path.GetFileName(versionDir),
                Permissions = permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn($"Đọc tiện ích {versionDir} lỗi", ex);
            return null;
        }
    }

    /// <summary>Tên dạng "__MSG_appName__": tra trong _locales/&lt;default_locale&gt;/messages.json.</summary>
    private static string? Localized(string dir, JsonElement manifest, string token)
    {
        var key = token[6..].TrimEnd('_');
        var locale = manifest.TryGetProperty("default_locale", out var l) ? l.GetString() ?? "en" : "en";
        foreach (var candidate in new[] { locale, "en", "en_US" })
        {
            var path = Path.Combine(dir, "_locales", candidate, "messages.json");
            if (!File.Exists(path)) continue;
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetProperty("message", out var m))
                    return m.GetString();
            }
        }

        return null;
    }
}
