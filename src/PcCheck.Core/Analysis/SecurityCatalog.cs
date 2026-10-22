using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using PcCheck.Core.Models;

namespace PcCheck.Core.Analysis;

/// <summary>Phần mềm đã biết: tên hiển thị khi cài, tên tiến trình, tên dịch vụ; Exclude = tên sản phẩm khác cùng hãng (không khớp).</summary>
public sealed record KnownTool(string Key, string Name, SecurityToolKind Kind, string[] Programs, string[] Processes, string[] Services)
{
    public string[] Exclude { get; init; } = [];
}

/// <summary>Thư mục hệ thống dùng để phân loại đường dẫn (tách riêng để kiểm thử được).</summary>
public sealed record PathContext(string Windows, string ProgramFiles, string ProgramFilesX86, string ProgramData, string Users, string Temp)
{
    public static PathContext Current { get; } = new(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? "",
        Path.GetTempPath());
}

/// <summary>Danh mục và quy tắc nhận diện cho phần kiểm tra bảo mật. Toàn bộ là hàm thuần để kiểm thử.</summary>
public static partial class SecurityCatalog
{
    private static KnownTool R(string key, string name, string[] programs, string[] processes, string[]? services = null) =>
        new(key, name, SecurityToolKind.RemoteAccess, programs, processes, services ?? []);

    private static KnownTool M(string key, string name, string[] programs, string[] processes, string[]? services = null) =>
        new(key, name, SecurityToolKind.Monitoring, programs, processes, services ?? []);

    private static KnownTool S(string key, string name, string[] programs, string[] processes, string[]? services = null) =>
        new(key, name, SecurityToolKind.Spyware, programs, processes, services ?? []);

    /// <summary>Phần mềm điều khiển từ xa, giám sát và gián điệp phổ biến (khớp nguyên từ, không phân biệt hoa thường).</summary>
    public static IReadOnlyList<KnownTool> Tools { get; } =
    [
        R("teamviewer", "TeamViewer", ["TeamViewer"], ["TeamViewer.exe", "TeamViewer_Service.exe", "tv_w32.exe", "tv_x64.exe"], ["TeamViewer"]),
        R("anydesk", "AnyDesk", ["AnyDesk"], ["AnyDesk.exe"], ["AnyDesk"]),
        R("ultraviewer", "UltraViewer", ["UltraViewer"], ["UltraViewer_Desktop.exe", "UltraViewer_Service.exe", "UltraViewer.exe"], ["UltraViewService"]),
        R("rustdesk", "RustDesk", ["RustDesk"], ["rustdesk.exe"], ["RustDesk"]),
        R("todesk", "ToDesk", ["ToDesk"], ["ToDesk.exe", "ToDesk_Service.exe"], ["ToDesk_Service"]),
        R("sunlogin", "Sunlogin (Oray)", ["SunloginClient", "Sunlogin"], ["SunloginClient.exe"], ["SunloginService"]),
        R("hoptodesk", "HopToDesk", ["HopToDesk"], ["HopToDesk.exe"]),
        R("chrome_remote", "Chrome Remote Desktop", ["Chrome Remote Desktop Host"], ["remoting_host.exe"], ["chromoting"]),
        R("splashtop", "Splashtop", ["Splashtop Streamer"], ["SRService.exe", "SRServer.exe", "SRManager.exe"], ["SplashtopRemoteService"]),
        R("logmein", "LogMeIn", ["LogMeIn"], ["LogMeIn.exe", "LMIGuardianSvc.exe", "LogMeInSystray.exe"], ["LogMeIn"]) with { Exclude = ["Hamachi"] },
        R("goto", "GoTo / GoToMyPC", ["GoToMyPC", "GoTo Resolve", "GoToAssist"], ["g2comm.exe", "g2svc.exe", "GoToAssistService.exe"]),
        R("ammyy", "Ammyy Admin", ["Ammyy Admin"], ["AA_v3.exe", "AMMYY_Admin.exe"]),
        R("radmin", "Radmin", ["Radmin Server"], ["rserver3.exe"], ["RServer3"]),
        R("realvnc", "VNC (RealVNC / TightVNC / UltraVNC / TigerVNC)", ["VNC Server", "RealVNC", "TightVNC", "UltraVNC", "TigerVNC"],
            ["vncserver.exe", "tvnserver.exe", "winvnc.exe", "winvnc4.exe", "vncserverui.exe"], ["vncserver", "tvnserver", "uvnc_service"]),
        R("screenconnect", "ConnectWise ScreenConnect", ["ScreenConnect Client", "ConnectWise Control"], ["ScreenConnect.ClientService.exe", "ScreenConnect.WindowsClient.exe"]),
        R("atera", "Atera", ["AteraAgent", "Atera Agent"], ["AteraAgent.exe"], ["AteraAgent"]),
        R("ninja", "NinjaOne (NinjaRMM)", ["NinjaRMMAgent", "NinjaOne Agent"], ["NinjaRMMAgent.exe", "NinjaRMMAgentPatcher.exe"], ["NinjaRMMAgent"]),
        R("meshagent", "MeshCentral (Mesh Agent)", ["Mesh Agent"], ["MeshAgent.exe"], ["Mesh Agent"]),
        R("remote_utilities", "Remote Utilities", ["Remote Utilities - Host", "Remote Utilities Host"], ["rutserv.exe", "rfusclient.exe"], ["RManService"]),
        R("supremo", "Supremo", ["Supremo"], ["Supremo.exe", "SupremoService.exe"], ["SupremoService"]),
        R("dwservice", "DWService", ["DWAgent"], ["dwagent.exe", "dwagsvc.exe"], ["DWAgent"]),
        R("zoho_assist", "Zoho Assist", ["Zoho Assist"], ["ZohoURS.exe", "ZohoMeeting.exe", "ZA_Connect.exe"]),
        R("aeroadmin", "AeroAdmin", ["AeroAdmin"], ["AeroAdmin.exe"]),
        R("nomachine", "NoMachine", ["NoMachine"], ["nxservice64.exe", "nxserver.exe", "nxnode.exe"], ["nxservice"]),
        R("action1", "Action1", ["Action1 Agent", "Action1"], ["action1_agent.exe"], ["A1Agent"]),
        R("kaseya", "Kaseya", ["Kaseya Agent"], ["AgentMon.exe"]),
        R("remotepc", "RemotePC", ["RemotePC"], ["RemotePCService.exe", "RemotePCDesktop.exe"]),
        R("islonline", "ISL Online", ["ISL AlwaysOn", "ISL Light"], ["ISLAlwaysOn.exe", "ISLLight.exe"]),
        R("beyondtrust", "BeyondTrust / Bomgar", ["BeyondTrust Remote Support", "Bomgar"], ["bomgar-scc.exe", "bomgar-rdp.exe"]),
        R("getscreen", "Getscreen.me", ["Getscreen.me"], ["getscreen.exe"]),
        R("parsec", "Parsec", ["Parsec"], ["parsecd.exe"]),

        M("netsupport", "NetSupport Manager / School", ["NetSupport Manager", "NetSupport School"], ["client32.exe"], ["Client32"]),
        M("kickidler", "Kickidler", ["Kickidler"], ["grabber.exe", "kickidler.exe"]),
        M("activtrak", "ActivTrak", ["ActivTrak"], ["svctcom.exe", "scthost.exe"]),
        M("teramind", "Teramind", ["Teramind"], ["tmagent.exe", "TMService.exe"]),
        M("hubstaff", "Hubstaff", ["Hubstaff"], ["Hubstaff.exe", "HubstaffCLI.exe"]),
        M("timedoctor", "Time Doctor", ["Time Doctor"], ["Time Doctor.exe", "TimeDoctor.exe", "sfproc.exe"]),
        M("desktime", "DeskTime", ["DeskTime"], ["DeskTime.exe"]),
        M("insightful", "Insightful (Workpuls)", ["Insightful", "Workpuls"], ["Insightful.exe", "Workpuls.exe"]),
        M("interguard", "InterGuard", ["InterGuard"], ["igagent.exe"]),
        M("veriato", "Veriato / Spector", ["Veriato", "Spector 360", "Spector CNE"], ["veriato.exe"]),
        M("staffcop", "StaffCop", ["StaffCop"], ["StaffCop.exe", "scagent.exe"]),
        M("workexaminer", "Work Examiner", ["Work Examiner"], ["wex.exe"]),
        M("currentware", "CurrentWare / BrowseReporter", ["CurrentWare", "BrowseReporter", "BrowseControl", "enPowerManager"], ["cwclient.exe"]),
        M("controlio", "Controlio", ["Controlio"], ["controlio.exe"]),
        M("sentrypc", "SentryPC", ["SentryPC"], ["sentrypc.exe"]),
        M("imonitor", "iMonitor / EmpMonitor", ["iMonitor", "EmpMonitor"], ["eamclient.exe", "empmonitor.exe"]),
        M("osmonitor", "OsMonitor", ["OsMonitor"], ["osmonitor.exe"]),
        M("clevercontrol", "CleverControl", ["CleverControl"], ["clevercontrol.exe"]),
        M("wolfeye", "Wolfeye Remote Screen", ["Wolfeye"], ["wolfeye.exe"]),
        M("pearlecho", "Pearl Echo", ["Pearl Echo"], ["PearlEcho.exe"]),
        M("qustodio", "Qustodio", ["Qustodio"], ["QAAdminAgent.exe", "QAAgent.exe", "QUSTODIO.exe"]),
        M("netnanny", "Net Nanny", ["Net Nanny"], ["NetNanny.exe"]),
        M("norton_family", "Norton Family", ["Norton Family"], ["NOF.exe"]),
        M("safekids", "Kaspersky Safe Kids", ["Kaspersky Safe Kids"], ["safekids.exe"]),
        M("famisafe", "FamiSafe", ["FamiSafe"], ["FamiSafe.exe"]),

        S("spyrix", "Spyrix", ["Spyrix"], ["spyrix.exe", "sfk.exe", "skm.exe"]),
        S("refog", "REFOG", ["REFOG", "Refog Personal Monitor", "Refog Employee Monitor"], ["mpk.exe", "refog.exe"]),
        S("kidlogger", "KidLogger", ["KidLogger"], ["kidlogger.exe", "kl.exe"]),
        S("hoverwatch", "Hoverwatch", ["Hoverwatch"], ["hoverwatch.exe"]),
        S("ardamax", "Ardamax Keylogger", ["Ardamax"], ["akl.exe"]),
        S("revealer", "Revealer Keylogger", ["Revealer Keylogger"], ["rvlkl.exe"]),
        S("bestfreekeylogger", "Best Free Keylogger", ["Best Free Keylogger", "BFK"], ["bfk.exe"]),
        S("elite_keylogger", "Elite Keylogger", ["Elite Keylogger"], ["elitekeylogger.exe"]),
        S("actual_keylogger", "Actual Keylogger / Actual Spy", ["Actual Keylogger", "Actual Spy"], ["actualkeylogger.exe"]),
        S("allinone_keylogger", "All In One Keylogger", ["All In One Keylogger"], ["aiokl.exe"]),
        S("spyagent", "Spytech SpyAgent", ["SpyAgent", "Spytech"], ["spyagent.exe"]),
        S("realtimespy", "Realtime-Spy", ["Realtime-Spy"], ["rspy.exe"]),
        S("webwatcher", "WebWatcher", ["WebWatcher"], ["webwatcher.exe"]),
        S("softactivity", "SoftActivity Monitor / Keylogger", ["SoftActivity"], ["sakl.exe", "tsmon.exe"]),
        S("winspy", "Win-Spy", ["Win-Spy", "Win Spy"], ["winspy.exe"]),
        S("pcpandora", "PC Pandora", ["PC Pandora"], ["pcpandora.exe"]),
        S("eblaster", "eBlaster", ["eBlaster"], ["eblaster.exe"]),
        S("mspy", "mSpy / FlexiSPY", ["mSpy", "FlexiSPY"], ["mspy.exe", "flexispy.exe"]),
    ];

    private static readonly Dictionary<string, KnownTool> ByProcess = Tools
        .SelectMany(t => t.Processes.Select(p => (Process: p, Tool: t)))
        .GroupBy(x => x.Process, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Tool, StringComparer.OrdinalIgnoreCase);

    public static KnownTool? MatchProcess(string exeName)
    {
        var name = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName : exeName + ".exe";
        return ByProcess.GetValueOrDefault(name);
    }

    public static KnownTool? MatchProgram(string programName) =>
        Tools.FirstOrDefault(t => t.Programs.Any(p => ContainsWord(programName, p)) && !t.Exclude.Any(x => ContainsWord(programName, x)));

    /// <summary>
    /// Tên tiến trình/dịch vụ của phần mềm giám sát/gián điệp thường ngắn và chung chung (kl.exe, mpk.exe, grabber.exe...):
    /// chỉ tin khi đường dẫn hoặc tên hãng cũng nhắc tới phần mềm đó. Phần mềm điều khiển từ xa có tên tiến trình đặc trưng.
    /// </summary>
    public static bool IsCorroborated(KnownTool tool, FileTrust? file)
    {
        if (tool.Kind == SecurityToolKind.RemoteAccess) return true;
        if (file is null) return false;
        var text = file.Path + " " + file.Publisher;
        return tool.Programs.Any(p => ContainsWord(text, p)) || text.Contains(tool.Key, StringComparison.OrdinalIgnoreCase);
    }

    public static KnownTool? MatchService(string name, string displayName, string imagePath)
    {
        var exe = Path.GetFileName(ExecutableOf(imagePath));
        return Tools.FirstOrDefault(t => t.Services.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
               ?? (exe.Length > 0 ? MatchProcess(exe) : null)
               ?? Tools.FirstOrDefault(t => t.Programs.Any(p => ContainsWord(displayName, p)) && !t.Exclude.Any(x => ContainsWord(displayName, x)));
    }

    /// <summary>Khớp nguyên cụm từ (không khớp "Inspector" với "Spector").</summary>
    internal static bool ContainsWord(string text, string phrase)
    {
        if (text.Length < phrase.Length) return false;
        var index = 0;
        while ((index = text.IndexOf(phrase, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + phrase.Length;
            var after = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (before && after) return true;
            index++;
        }

        return false;
    }

    // ===== Đường dẫn =====

    private static string Norm(string path) => path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();

    private static bool Under(string path, string root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        var p = Norm(path);
        var r = Norm(root);
        return p.Length > r.Length && p.StartsWith(r + "\\", StringComparison.Ordinal);
    }

    public static PathKind Classify(string path, PathContext? context = null)
    {
        var ctx = context ?? PathContext.Current;
        if (string.IsNullOrWhiteSpace(path)) return PathKind.Unknown;
        if (Under(path, ctx.Temp) || Norm(path).Contains("\\APPDATA\\LOCAL\\TEMP\\", StringComparison.Ordinal) || Under(path, Path.Combine(ctx.Windows, "Temp")))
            return PathKind.Temp;
        if (Under(path, ctx.Windows)) return PathKind.Windows;
        if (Under(path, ctx.ProgramFiles) || Under(path, ctx.ProgramFilesX86)) return PathKind.ProgramFiles;
        if (Under(path, ctx.ProgramData)) return PathKind.ProgramData;
        if (Under(path, ctx.Users)) return PathKind.UserProfile;
        return PathKind.Other;
    }

    /// <summary>Tách tệp thực thi khỏi dòng lệnh: "C:\a b\x.exe" -args, C:\x.exe -args, x.exe.</summary>
    public static string ExecutableOf(string command)
    {
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        if (text.Length == 0) return "";
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : text.Trim('"');
        }

        foreach (var ext in new[] { ".exe", ".com", ".bat", ".cmd", ".vbs", ".js", ".ps1", ".lnk", ".scr" })
        {
            var at = text.IndexOf(ext + " ", StringComparison.OrdinalIgnoreCase);
            if (at > 0) return text[..(at + ext.Length)];
            if (text.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return text;
        }

        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }

    private static readonly HashSet<string> ScriptHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe", "conhost.exe",
    };

    private static readonly HashSet<string> LolBins = new(StringComparer.OrdinalIgnoreCase)
    {
        "rundll32.exe", "regsvr32.exe", "mshta.exe", "certutil.exe", "bitsadmin.exe", "msbuild.exe", "installutil.exe",
        "regasm.exe", "regsvcs.exe", "msiexec.exe", "cmstp.exe", "forfiles.exe", "wmic.exe",
    };

    /// <summary>Đánh giá dòng lệnh tự khởi động: chạy qua trình thông dịch, lệnh mã hóa, ẩn cửa sổ, tải từ Internet.</summary>
    public static CommandFlags AnalyzeCommand(string command)
    {
        var flags = CommandFlags.None;
        var exe = Path.GetFileName(ExecutableOf(command).Replace('/', '\\').Split('\\')[^1]);
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && exe.Length > 0 && !exe.Contains('.')) exe += ".exe";
        if (ScriptHosts.Contains(exe)) flags |= CommandFlags.ScriptHost;
        if (LolBins.Contains(exe)) flags |= CommandFlags.LolBin;
        if (EncodedRegex().IsMatch(command)) flags |= CommandFlags.EncodedCommand;
        if (HiddenRegex().IsMatch(command)) flags |= CommandFlags.HiddenWindow;
        if (DownloadRegex().IsMatch(command)) flags |= CommandFlags.Downloads;
        return flags;
    }

    /// <summary>Đường dẫn script (.ps1/.vbs/.js/.bat/.cmd/.hta) trong tham số của trình thông dịch.</summary>
    public static string? ScriptPathOf(string command)
    {
        var match = ScriptPathRegex().Match(Environment.ExpandEnvironmentVariables(command));
        return match.Success ? match.Groups["p"].Value.Trim('"') : null;
    }

    [GeneratedRegex(@"\s-(e|ec|en|enc|enco|encod|encode|encoded|encodedc|encodedco|encodedcom|encodedcomm|encodedcomma|encodedcomman|encodedcommand)\s+[A-Za-z0-9+/=]{16,}", RegexOptions.IgnoreCase)]
    private static partial Regex EncodedRegex();

    [GeneratedRegex(@"(\s-w(indowstyle)?\s+(h|hidden|1)\b)|(\s-windowstyle\s+hidden)|(\bvbhide\b)|(\s--headless\b)", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenRegex();

    [GeneratedRegex(@"(https?://)|(\bdownload(string|file|data)\b)|(\binvoke-webrequest\b)|(\biwr\s)|(\binvoke-expression\b)|(\biex\s*[\(\s])|(frombase64string)|(net\.webclient)|(start-bitstransfer)|(-urlcache)|(\bjavascript:)|(\bvbscript:)", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadRegex();

    [GeneratedRegex(@"(?<p>""[^""]+\.(ps1|vbs|vbe|js|jse|wsf|bat|cmd|hta)""|[^\s""]+\.(ps1|vbs|vbe|js|jse|wsf|bat|cmd|hta))(\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptPathRegex();

    // ===== Tiến trình giả mạo tên hệ thống =====

    private static readonly HashSet<string> System32Processes = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost.exe", "lsass.exe", "csrss.exe", "winlogon.exe", "services.exe", "smss.exe", "wininit.exe", "spoolsv.exe",
        "taskhostw.exe", "dwm.exe", "lsm.exe", "sihost.exe", "ctfmon.exe", "dllhost.exe", "rundll32.exe", "runtimebroker.exe",
        "searchindexer.exe", "audiodg.exe", "fontdrvhost.exe", "conhost.exe", "wuauclt.exe", "taskmgr.exe", "regsvr32.exe",
    };

    /// <summary>Tên giống tiến trình hệ thống nhưng chạy ngoài thư mục Windows (vd svchost.exe trong AppData).</summary>
    public static bool IsMasquerade(string exeName, string path, PathContext? context = null)
    {
        var ctx = context ?? PathContext.Current;
        if (string.IsNullOrEmpty(path)) return false;
        if (exeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            return !Norm(path).Equals(Norm(Path.Combine(ctx.Windows, "explorer.exe")), StringComparison.Ordinal)
                   && !Under(path, ctx.Windows);
        return System32Processes.Contains(exeName) && !Under(path, ctx.Windows);
    }

    /// <summary>Như trên nhưng dùng vị trí đã phân loại sẵn (Windows hay không).</summary>
    public static bool IsMasquerade(string exeName, FileTrust file) =>
        file.Kind is not (PathKind.Windows or PathKind.Unknown)
        && (System32Processes.Contains(exeName) || exeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>Tên gần giống tiến trình hệ thống (sai 1 ký tự / đảo 2 ký tự), vd scvhost.exe, lsasss.exe.</summary>
    public static bool IsLookalike(string exeName)
    {
        var name = exeName.ToLowerInvariant();
        if (!name.EndsWith(".exe", StringComparison.Ordinal) || name.Length < 9) return false;
        if (System32Processes.Contains(name) || name == "explorer.exe") return false;
        return System32Processes.Append("explorer.exe").Any(s => Distance(name, s.ToLowerInvariant()) == 1);
    }

    /// <summary>Khoảng cách Damerau-Levenshtein (OSA).</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        }

        return d[a.Length, b.Length];
    }

    // ===== Hosts =====

    public enum HostsVerdict
    {
        Normal,

        /// <summary>Chặn tên miền cập nhật Windows / diệt virus (vd trỏ về 0.0.0.0).</summary>
        BlocksSecurity,

        /// <summary>Chuyển hướng trang ngân hàng / ví / đăng nhập / diệt virus tới máy chủ khác – có thể là trang giả.</summary>
        Redirect,

        /// <summary>Chuyển hướng trang phổ biến khác (vd danh sách "tăng tốc GitHub/YouTube") – cần xem lại.</summary>
        RedirectOther,
    }

    private static readonly string[] SecurityDomains =
    [
        "windowsupdate.com", "update.microsoft.com", "download.microsoft.com", "wdcp.microsoft.com", "wd.microsoft.com",
        "smartscreen.microsoft.com", "kaspersky", "eset.com", "avast.com", "avg.com", "bitdefender", "malwarebytes",
        "mcafee.com", "norton.com", "symantec.com", "trendmicro", "sophos.com", "virustotal.com", "bkav.com", "f-secure.com",
    ];

    /// <summary>Ngân hàng, ví điện tử, tài khoản đăng nhập: chuyển hướng = nguy hiểm.</summary>
    private static readonly string[] CriticalDomains =
    [
        "microsoft.com", "live.com", "outlook.com", "google.com", "gmail.com", "facebook.com", "apple.com", "icloud.com",
        "paypal.com", "vietcombank.com.vn", "techcombank.com", "techcombank.com.vn", "bidv.com.vn", "vietinbank.vn",
        "agribank.com.vn", "vpbank.com.vn", "mbbank.com.vn", "acb.com.vn", "tpb.vn", "sacombank.com", "shb.com.vn",
        "momo.vn", "zalopay.vn",
    ];

    /// <summary>Trang phổ biến khác: chuyển hướng cần xem lại (danh sách "tăng tốc" GitHub/YouTube hay sửa hosts hợp lệ).</summary>
    private static readonly string[] OtherSensitiveDomains =
    [
        "office.com", "youtube.com", "messenger.com", "instagram.com", "zalo.me", "github.com", "githubusercontent.com",
    ];

    public static HostsVerdict ClassifyHosts(HostsEntry entry)
    {
        var host = entry.Host.ToLowerInvariant();
        var blocked = IsBlackhole(entry.Address);
        var security = SecurityDomains.Any(d => host == d || host.EndsWith("." + d, StringComparison.Ordinal) || (d.IndexOf('.') < 0 && host.Contains(d, StringComparison.Ordinal)));
        if (security && blocked) return HostsVerdict.BlocksSecurity;
        if (blocked) return HostsVerdict.Normal;
        if (security || CriticalDomains.Any(d => Matches(host, d))) return HostsVerdict.Redirect;
        return OtherSensitiveDomains.Any(d => Matches(host, d)) ? HostsVerdict.RedirectOther : HostsVerdict.Normal;
    }

    private static bool Matches(string host, string domain) => host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

    private static bool IsBlackhole(string address) =>
        address is "0.0.0.0" or "::" or "::1" or "0" || address.StartsWith("127.", StringComparison.Ordinal);

    /// <summary>Đọc tệp hosts (bỏ chú thích, localhost mặc định).</summary>
    public static List<HostsEntry> ParseHosts(IEnumerable<string> lines)
    {
        var list = new List<HostsEntry>();
        var number = 0;
        foreach (var raw in lines)
        {
            number++;
            var line = raw;
            var hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            foreach (var host in parts.Skip(1))
            {
                if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && IsBlackhole(parts[0])) continue;
                list.Add(new HostsEntry(parts[0], host, number));
            }
        }

        return list;
    }

    // ===== Mạng =====

    /// <summary>Địa chỉ công cộng (không phải localhost / mạng nội bộ / link-local).</summary>
    public static bool IsPublicAddress(string address)
    {
        if (!IPAddress.TryParse(address, out var ip)) return false;
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] == 0 || b[0] >= 224);
        }

        return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.IsIPv6UniqueLocal);
    }

    // ===== Trình duyệt =====

    private static readonly string[] BroadHosts = ["<all_urls>", "*://*/*", "http://*/*", "https://*/*", "*://*/"];

    /// <summary>Khóa quyền rủi ro của tiện ích mở rộng (đã dịch ở ext.perm.*).</summary>
    public static IReadOnlyList<string> RiskyPermissions(IEnumerable<string> permissions)
    {
        var set = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        if (BroadHosts.Any(set.Contains)) list.Add("all_sites");
        foreach (var (perm, key) in new[]
                 {
                     ("debugger", "debugger"), ("proxy", "proxy"), ("nativeMessaging", "native"), ("desktopCapture", "capture"),
                     ("tabCapture", "capture"), ("clipboardRead", "clipboard"), ("history", "history"), ("cookies", "cookies"),
                     ("management", "management"), ("webRequestBlocking", "intercept"),
                 })
        {
            if (set.Contains(perm) && !list.Contains(key)) list.Add(key);
        }

        return list;
    }

    // ===== Chứng chỉ =====

    private static readonly string[] KnownRootMarkers =
    [
        "Microsoft", "Kaspersky", "ESET", "Avast", "AVG", "Bitdefender", "Norton", "Fiddler", "Sophos", "Fortinet", "Zscaler",
        "Cisco Umbrella", "Netskope", "Palo Alto", "Check Point", "Trend Micro", "McAfee", "Malwarebytes", "Adguard", "Thawte",
        "VeriSign", "Symantec", "DigiCert", "GlobalSign", "Sectigo", "Entrust", "GoDaddy", "Let's Encrypt", "ISRG", "Baltimore",

        // CA chữ ký số Việt Nam (cài cùng phần mềm token USB / khai thuế điện tử).
        "Vietnam National", "VNPT", "Viettel", "BKAV", "FPT", "MISA", "NewCA", "SmartSign", "Nacencomm", "EFY", "WINCA",
        "SAFE-CA", "TrustCA", "EasyCA", "LCS-CA", "VINA-CA", "CMC CA", "Bộ Tài chính",

        // Chứng chỉ phát triển trên máy (IIS Express, dotnet dev-certs).
        "IIS Express", "ASP.NET Core HTTPS development",
    ];

    /// <summary>Chứng chỉ gốc do phần mềm diệt virus / công ty / CA lớn thêm – thường hợp lệ (chỉ hiển thị, không cảnh báo).</summary>
    public static bool IsKnownRoot(CertificateEntry cert) =>
        cert.Subject.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || KnownRootMarkers.Any(m => cert.Subject.Contains(m, StringComparison.OrdinalIgnoreCase) || cert.Issuer.Contains(m, StringComparison.OrdinalIgnoreCase));

    // ===== Hijack =====

    private static readonly HashSet<string> AccessibilityExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sethc.exe", "utilman.exe", "osk.exe", "narrator.exe", "magnify.exe", "displayswitch.exe", "atbroker.exe",
    };

    private static readonly HashSet<string> SecurityExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "msmpeng.exe", "mpcmdrun.exe", "securityhealthsystray.exe", "msascui.exe", "taskmgr.exe", "regedit.exe", "mmc.exe",
        "procexp.exe", "procexp64.exe", "autoruns.exe", "mbam.exe", "avp.exe", "ekrn.exe", "avastui.exe",
    };

    public static Severity ClassifyHijack(HijackEntry entry)
    {
        switch (entry.Kind)
        {
            case HijackKind.WinlogonShell:
                return entry.Value.Trim().TrimEnd(',').Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) ? Severity.Ok : Severity.Critical;
            case HijackKind.WinlogonUserinit:
                var parts = entry.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return parts.Length == 1 && Path.GetFileName(parts[0].Replace('/', '\\').Split('\\')[^1]).Equals("userinit.exe", StringComparison.OrdinalIgnoreCase)
                    ? Severity.Ok
                    : Severity.Critical;
            case HijackKind.AppInitDlls:
                return string.IsNullOrWhiteSpace(entry.Value) ? Severity.Ok : Severity.Warning;
            default:
                var value = entry.Value.ToLowerInvariant();

                // Công cụ hợp lệ thay Task Manager / gỡ lỗi: Process Explorer, Process Hacker, System Informer, VS, WinDbg.
                var knownTool = new[] { "procexp", "processhacker", "systeminformer", "vsjitdebugger", "windbg" }.Any(t => value.Contains(t, StringComparison.Ordinal));
                if (AccessibilityExes.Contains(entry.Target)) return Severity.Critical;
                if (knownTool) return Severity.Info;
                return SecurityExes.Contains(entry.Target) ? Severity.Critical : Severity.Warning;
        }
    }

    // ===== Loại trừ của Defender =====

    /// <summary>Loại trừ quá rộng hoặc ở thư mục người dùng – malware hay tự thêm để Defender bỏ qua.</summary>
    public static bool IsRiskyExclusion(string exclusion, PathContext? context = null)
    {
        var ctx = context ?? PathContext.Current;
        var value = Environment.ExpandEnvironmentVariables(exclusion.Trim());
        if (value.Length == 0) return false;
        if (value.Length <= 3 && value.Contains(':', StringComparison.Ordinal)) return true; // C:\
        if (value.StartsWith('.') || value.StartsWith("*.", StringComparison.Ordinal))
            return value.TrimStart('*', '.').ToLowerInvariant() is "exe" or "dll" or "ps1" or "bat" or "cmd" or "vbs" or "js" or "scr";
        var exe = Path.GetFileName(value.Replace('/', '\\').Split('\\')[^1]);
        if (exe.ToLowerInvariant() is "powershell.exe" or "pwsh.exe" or "cmd.exe" or "wscript.exe" or "cscript.exe" or "mshta.exe" or "rundll32.exe" or "regsvr32.exe")
            return true;
        var normalized = Norm(value);
        if (normalized == Norm(ctx.Users) || normalized == Norm(ctx.ProgramData) || normalized == Norm(ctx.Windows) || normalized == Norm(ctx.Temp)) return true;
        var kind = Classify(value + "\\x", ctx);
        return kind is PathKind.Temp || (kind is PathKind.UserProfile && (normalized.Contains("\\APPDATA", StringComparison.Ordinal) || normalized.Contains("\\DOWNLOADS", StringComparison.Ordinal) || normalized.Count(c => c == '\\') <= 2));
    }
}
