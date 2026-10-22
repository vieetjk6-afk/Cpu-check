using PcCheck.Core.Fixes;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using static PcCheck.Core.Localization.LocFactory;

namespace PcCheck.Core.Analysis;

/// <summary>
/// Đánh giá dữ liệu bảo mật: phần mềm theo dõi / điều khiển từ xa, ứng dụng dùng camera-micro, chương trình tự chạy lạ,
/// tiến trình giả mạo, kết nối mạng, trình duyệt, chứng chỉ, Defender, truy cập từ xa, tài khoản và cấu hình hệ thống.
/// Nguyên tắc: chỉ báo "nguy hiểm" khi dấu hiệu rõ ràng; phần mềm hợp pháp nhưng có thể bị lợi dụng chỉ là "cần xem lại".
/// </summary>
public static class SecurityAnalyzer
{
    private const int MaxIssues = 25;

    private static readonly int[] RemotePorts = [22, 23, 3389, 4899, 5800, 5900, 5938, 5985, 5986, 7070];

    public static SecurityReport Analyze(ScanData data, DateTime now)
    {
        var security = data.Security;
        if (!security.Collected) return SecurityReport.Empty;

        var items = new List<SecurityItem>();
        var processes = security.Processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());
        var publicConnections = security.Connections.Where(c => !c.Listening && SecurityCatalog.IsPublicAddress(c.RemoteAddress)).ToList();

        Run(items, () => Tools(data, processes, publicConnections));
        Run(items, () => DeviceAccess(security, now));
        Run(items, () => Autostart(security));
        var flaggedProcesses = new Dictionary<int, Severity>();
        Run(items, () => Processes(security, publicConnections, flaggedProcesses));
        Run(items, () => Network(security, processes, publicConnections, flaggedProcesses));
        Run(items, () => Browser(security));
        Run(items, () => Certificates(security));
        Run(items, () => Defender(security.Defender));
        Run(items, () => Access(security));
        Run(items, () => SystemConfig(data));

        var ordered = items
            .GroupBy(i => i.Id)
            .Select(g => g.OrderByDescending(i => i.Severity).First())
            .OrderBy(i => i.Section)
            .ThenByDescending(i => i.Severity)
            .ToList();
        return new SecurityReport { Collected = true, Items = ordered };
    }

    private static void Run(List<SecurityItem> items, Func<IEnumerable<SecurityItem>> producer)
    {
        try
        {
            items.AddRange(producer());
        }
        catch (Exception ex)
        {
            Util.Log.Error("Phân tích bảo mật lỗi", ex);
        }
    }

    /// <summary>Vấn đề cho danh sách lỗi: các mục cảnh báo / nguy hiểm (nhiều nhất 25 mục).</summary>
    public static IEnumerable<Issue> ToIssues(SecurityReport report)
    {
        foreach (var item in report.Items.Where(i => i.Severity >= Severity.Warning && !i.NoIssue).OrderByDescending(i => i.Severity).Take(MaxIssues))
        {
            var fixes = new List<FixAction> { FixCatalog.Navigate(Pages.Security, "fix.go_security") };
            fixes.AddRange(item.Actions.Where(a => a.Kind == SecurityActionKind.OpenUri).Take(1).Select(a => new FixAction(FixKind.OpenUri, a.Arg, a.Label)));
            yield return new Issue
            {
                Id = item.Id,
                Category = IssueCategory.Security,
                Severity = item.Severity,
                Title = item.Title,
                Detail = item.Detail ?? item.Title,
                Recommendation = item.Evidence.Length > 0 ? L("sec.evidence", item.Evidence) : null,
                Fixes = fixes,
            };
        }
    }

    // ===== Phần mềm theo dõi / điều khiển từ xa =====

    private static IEnumerable<SecurityItem> Tools(ScanData data, Dictionary<int, ProcessEntry> processes, List<ConnectionEntry> publicConnections)
    {
        var security = data.Security;
        foreach (var tool in SecurityCatalog.Tools)
        {
            var programs = data.Programs.Where(p => SecurityCatalog.MatchProgram(p.Name) == tool).ToList();
            var running = security.Processes.Where(p => SecurityCatalog.MatchProcess(p.Name) == tool).ToList();
            var services = security.Services.Where(s => SecurityCatalog.MatchService(s.Name, s.DisplayName, s.PathName) == tool).ToList();
            var autostart = security.Autostart.Where(a =>
                    a.Enabled && (a.Target is { } t && SecurityCatalog.MatchProcess(Path.GetFileName(t.Path)) == tool || tool.Programs.Any(p => SecurityCatalog.ContainsWord(a.Name, p))))
                .ToList();
            if (programs.Count == 0 && running.Count == 0 && services.Count == 0 && autostart.Count == 0) continue;

            var pids = running.Select(p => p.Pid).Concat(services.Where(s => s.ProcessId > 0).Select(s => s.ProcessId)).ToHashSet();
            var connections = publicConnections.Count(c => pids.Contains(c.Pid));
            var isRunning = running.Count > 0 || services.Any(s => s.Running);
            var autoStarts = autostart.Count > 0 || services.Any(s => s.AutoStart);

            var severity = tool.Kind switch
            {
                SecurityToolKind.Spyware => Severity.Critical,
                SecurityToolKind.Monitoring => Severity.Warning,
                _ => isRunning || autoStarts || connections > 0 ? Severity.Warning : Severity.Info,
            };

            var path = running.FirstOrDefault()?.File?.Path ?? services.FirstOrDefault()?.File?.Path
                       ?? autostart.FirstOrDefault()?.Target?.Path ?? programs.FirstOrDefault()?.InstallLocation ?? "";
            var tags = new List<LocText>();
            if (programs.Count > 0) tags.Add(L("sec.tag.installed"));
            if (isRunning) tags.Add(L("sec.tag.running"));
            if (autoStarts) tags.Add(L("sec.tag.autostart"));
            if (connections > 0) tags.Add(L("sec.tag.connected", connections));

            var actions = new List<SecurityAction>();
            if (programs.FirstOrDefault() is { } program) actions.Add(new SecurityAction(SecurityActionKind.Uninstall, program.Id, L("sec.action.uninstall")));
            AddFileActions(actions, path);

            var kind = tool.Kind switch
            {
                SecurityToolKind.Spyware => "spyware",
                SecurityToolKind.Monitoring => "monitoring",
                _ => "remote",
            };
            yield return new SecurityItem
            {
                Id = $"secure/sec_{kind}/{tool.Key}",
                Section = SecuritySectionKind.Tools,
                Severity = severity,
                Title = L($"sec.tool.{kind}.title", tool.Name),
                Detail = L($"sec.tool.{kind}.detail", tool.Name),
                Evidence = path,
                Tags = tags,
                Actions = actions,
                GuideKey = "sec_" + kind,
            };
        }
    }

    // ===== Camera / micro / vị trí =====

    private static IEnumerable<SecurityItem> DeviceAccess(SecurityData security, DateTime now)
    {
        foreach (var entry in security.DeviceAccess)
        {
            var name = entry.Packaged ? entry.App.Split('_')[0] : Path.GetFileName(entry.App);
            var file = entry.File;
            var unknown = !entry.Packaged && (file is null || !file.Exists || file.Signed == false || (file.UserWritable && file.Signed != true));
            var recent = entry.LastStart is { } start && now - start < TimeSpan.FromDays(7);
            var severity = unknown && (entry.InUse || recent) ? Severity.Warning : Severity.Info;

            var tags = new List<LocText>();
            if (entry.InUse) tags.Add(L("sec.tag.in_use"));
            if (entry.Packaged) tags.Add(L("sec.tag.store_app"));
            AddTrustTags(tags, file);

            var actions = new List<SecurityAction> { new(SecurityActionKind.OpenUri, PrivacyUri(entry.Capability), L("sec.action.privacy")) };
            if (!entry.Packaged) AddFileActions(actions, entry.App);

            yield return new SecurityItem
            {
                Id = $"secure/sec_device_access/{entry.Capability}/{StableHash(entry.App)}",
                Section = SecuritySectionKind.DeviceAccess,
                Severity = severity,
                Title = L($"sec.access.{entry.Capability}", name),
                Detail = entry.InUse ? L("sec.access.in_use", entry.LastStart!.Value) : L("sec.access.last", entry.LastStart!.Value),
                Evidence = entry.App,
                Tags = tags,
                Actions = actions,
                GuideKey = "sec_device_access",
            };
        }
    }

    private static string PrivacyUri(string capability) => capability switch
    {
        "webcam" => "ms-settings:privacy-webcam",
        "microphone" => "ms-settings:privacy-microphone",
        _ => "ms-settings:privacy-location",
    };

    // ===== Tự khởi động =====

    internal static (Severity Severity, string Reason) RateAutostart(AutostartEntry entry)
    {
        var flags = entry.Flags;
        var target = entry.Target;
        var risky = flags.HasFlag(CommandFlags.ScriptHost) || flags.HasFlag(CommandFlags.LolBin);
        Severity severity;
        string reason;
        if (risky && (flags.HasFlag(CommandFlags.EncodedCommand) || flags.HasFlag(CommandFlags.Downloads)))
            (severity, reason) = (Severity.Critical, flags.HasFlag(CommandFlags.EncodedCommand) ? "encoded" : "downloads");
        else if (risky && flags.HasFlag(CommandFlags.HiddenWindow))
            (severity, reason) = (Severity.Warning, "hidden_script");
        else if (target is { Exists: true, Kind: PathKind.Temp } && target.Signed != true)
            (severity, reason) = (Severity.Warning, "temp");
        else if (target is { Exists: true, UserWritable: true } && target.Signed != true)
            (severity, reason) = (Severity.Warning, "unsigned_user");
        else if (target is { Exists: true, Signed: false })
            (severity, reason) = (Severity.Info, "unsigned");
        else if (target is { Exists: false } && target.Path.Length > 0 && target.Kind != PathKind.Windows)
            (severity, reason) = (Severity.Info, "missing");
        else
            (severity, reason) = (Severity.Ok, "ok");

        if (entry.Hidden && severity == Severity.Info) severity = Severity.Warning;
        if (!entry.Enabled && severity > Severity.Ok) severity = severity == Severity.Critical ? Severity.Warning : Severity.Info;
        return (severity, reason);
    }

    private static IEnumerable<SecurityItem> Autostart(SecurityData security)
    {
        foreach (var entry in security.Autostart)
        {
            if (entry.Target is { Kind: PathKind.Windows } && entry.Flags == CommandFlags.None) continue;
            var (severity, reason) = RateAutostart(entry);

            var tags = new List<LocText> { L("sec.tag.source." + entry.Source.ToString().ToLowerInvariant()) };
            if (!entry.Enabled) tags.Add(L("sec.tag.disabled"));
            if (entry.Hidden) tags.Add(L("sec.tag.hidden"));
            if (entry.Flags.HasFlag(CommandFlags.EncodedCommand)) tags.Add(L("sec.tag.encoded"));
            if (entry.Flags.HasFlag(CommandFlags.Downloads)) tags.Add(L("sec.tag.downloads"));
            if (entry.Flags.HasFlag(CommandFlags.ScriptHost)) tags.Add(L("sec.tag.script"));
            AddTrustTags(tags, entry.Target);

            var actions = new List<SecurityAction>();
            if (entry.Enabled && entry.Source is AutostartSource.RunKey or AutostartSource.StartupFolder && entry.ActionId.Length > 0)
                actions.Add(new SecurityAction(SecurityActionKind.DisableStartup, entry.ActionId, L("sec.action.disable")));
            if (entry.Enabled && entry.Source == AutostartSource.ScheduledTask)
                actions.Add(new SecurityAction(SecurityActionKind.DisableTask, entry.ActionId, L("sec.action.disable")));
            if (entry.Target is { Exists: true } t) AddFileActions(actions, t.Path);

            yield return new SecurityItem
            {
                Id = $"secure/sec_autostart/{entry.Source}/{StableHash(entry.Location + "|" + entry.Name + "|" + entry.Command)}",
                Section = SecuritySectionKind.Autostart,
                Severity = severity,
                Title = L("sec.autostart.title", entry.Name),
                Detail = L("sec.autostart.reason." + reason),
                Evidence = entry.Command,
                Tags = tags,
                Actions = actions,
                GuideKey = "sec_autostart",
            };
        }
    }

    // ===== Tiến trình =====

    private static IEnumerable<SecurityItem> Processes(SecurityData security, List<ConnectionEntry> publicConnections, Dictionary<int, Severity> flagged)
    {
        var connectedPids = publicConnections.Select(c => c.Pid).ToHashSet();
        foreach (var group in security.Processes.Where(p => p.File is not null).GroupBy(p => p.File!.Path, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First();
            var file = first.File!;
            var connected = group.Any(p => connectedPids.Contains(p.Pid));
            Severity severity;
            string reason;
            if (SecurityCatalog.IsMasquerade(first.Name, file))
                (severity, reason) = (Severity.Critical, "masquerade");
            else if (SecurityCatalog.IsLookalike(first.Name))
                (severity, reason) = (Severity.Warning, "lookalike");
            else if (file.Kind == PathKind.Temp && file.Signed != true)
                (severity, reason) = (Severity.Warning, "temp");
            else if (file.UserWritable && file.Signed == false)
                (severity, reason) = (connected ? Severity.Warning : Severity.Info, connected ? "unsigned_connected" : "unsigned_user");
            else
                continue;

            foreach (var p in group) flagged[p.Pid] = severity;
            var tags = new List<LocText>();
            if (group.Count() > 1) tags.Add(L("sec.tag.instances", group.Count()));
            if (connected) tags.Add(L("sec.tag.connected", publicConnections.Count(c => group.Any(p => p.Pid == c.Pid))));
            AddTrustTags(tags, file);
            var actions = new List<SecurityAction>();
            AddFileActions(actions, file.Path);

            yield return new SecurityItem
            {
                Id = $"secure/sec_process/{StableHash(file.Path)}",
                Section = SecuritySectionKind.Processes,
                Severity = severity,
                Title = L("sec.process.title", first.Name),
                Detail = L("sec.process.reason." + reason),
                Evidence = file.Path,
                Tags = tags,
                Actions = actions,
                GuideKey = reason == "masquerade" ? "sec_masquerade" : "sec_process",
            };
        }
    }

    // ===== Mạng =====

    private static IEnumerable<SecurityItem> Network(SecurityData security, Dictionary<int, ProcessEntry> processes, List<ConnectionEntry> publicConnections, Dictionary<int, Severity> flagged)
    {
        foreach (var group in publicConnections.GroupBy(c => c.Pid).OrderByDescending(g => flagged.GetValueOrDefault(g.Key)).ThenByDescending(g => g.Count()).Take(40))
        {
            processes.TryGetValue(group.Key, out var process);
            var name = process?.Name ?? "PID " + group.Key;
            var severity = flagged.TryGetValue(group.Key, out var s) && s >= Severity.Warning ? Severity.Warning : Severity.Ok;
            var endpoints = group.Select(c => $"{(c.IPv6 ? "[" + c.RemoteAddress + "]" : c.RemoteAddress)}:{c.RemotePort}").Distinct().ToList();
            var tags = new List<LocText>();
            AddTrustTags(tags, process?.File);
            var actions = new List<SecurityAction>();
            if (process?.File is { } f) AddFileActions(actions, f.Path);

            yield return new SecurityItem
            {
                Id = $"secure/sec_network/{StableHash(process?.File?.Path ?? name)}",
                Section = SecuritySectionKind.Network,
                Severity = severity,
                Title = L("sec.net.title", name, endpoints.Count),
                Detail = L("sec.net.detail", string.Join(", ", endpoints.Take(6)) + (endpoints.Count > 6 ? ", …" : "")),
                Evidence = process?.File?.Path ?? "",
                Tags = tags,
                Actions = actions,
                GuideKey = "sec_network",
            };
        }

        // Cổng mở cho mạng ngoài của chương trình không thuộc Windows.
        foreach (var group in security.Connections.Where(c => c.Listening && c.LocalAddress is "0.0.0.0" or "::").GroupBy(c => c.Pid))
        {
            processes.TryGetValue(group.Key, out var process);
            if (process?.File is null || process.File.Kind == PathKind.Windows) continue;
            var ports = group.Select(c => c.LocalPort).Distinct().OrderBy(p => p).ToList();
            var remotePort = ports.Any(RemotePorts.Contains);
            var risky = process.File.UserWritable && process.File.Signed != true;
            var severity = remotePort || risky ? Severity.Warning : Severity.Info;
            var tags = new List<LocText>();
            if (remotePort) tags.Add(L("sec.tag.remote_port"));
            AddTrustTags(tags, process.File);
            var actions = new List<SecurityAction>();
            AddFileActions(actions, process.File.Path);

            yield return new SecurityItem
            {
                Id = $"secure/sec_listen/{StableHash(process.File.Path)}",
                Section = SecuritySectionKind.Network,
                Severity = severity,
                Title = L("sec.listen.title", process.Name, string.Join(", ", ports.Take(8))),
                Detail = L("sec.listen.detail"),
                Evidence = process.File.Path,
                Tags = tags,
                Actions = actions,
                GuideKey = "sec_listen",
            };
        }
    }

    // ===== Trình duyệt =====

    private static IEnumerable<SecurityItem> Browser(SecurityData security)
    {
        var proxy = security.Proxy;
        if (proxy.AutoConfigUrl.Length > 0 || (proxy.Enabled && proxy.Server.Length > 0))
        {
            var local = proxy.AutoConfigUrl.Length == 0 && (proxy.Server.Contains("127.0.0.1", StringComparison.Ordinal) || proxy.Server.Contains("localhost", StringComparison.OrdinalIgnoreCase));
            yield return new SecurityItem
            {
                Id = "secure/sec_proxy/system",
                Section = SecuritySectionKind.Browser,
                Severity = local ? Severity.Info : Severity.Warning,
                Title = L(proxy.AutoConfigUrl.Length > 0 ? "sec.proxy.pac" : "sec.proxy.title"),
                Detail = L(local ? "sec.proxy.local" : "sec.proxy.detail"),
                Evidence = proxy.AutoConfigUrl.Length > 0 ? proxy.AutoConfigUrl : proxy.Server,
                Actions = [new SecurityAction(SecurityActionKind.OpenUri, "ms-settings:network-proxy", L("sec.action.proxy_settings"))],
                GuideKey = "sec_proxy",
            };
        }

        foreach (var group in security.BrowserPolicies.GroupBy(p => p.Browser))
        {
            var proxyPolicy = group.FirstOrDefault(p => p.Name is "ProxyServer" or "ProxyPacUrl" || p.Name == "ProxyMode" && p.Value is "fixed_servers" or "pac_script");
            if (proxyPolicy is not null)
            {
                yield return new SecurityItem
                {
                    Id = $"secure/sec_browser_policy/proxy/{StableHash(group.Key)}",
                    Section = SecuritySectionKind.Browser,
                    Severity = Severity.Warning,
                    Title = L("sec.browser.proxy_policy", group.Key),
                    Detail = L("sec.browser.proxy_policy_detail"),
                    Evidence = $"{proxyPolicy.Scope}: {proxyPolicy.Name} = {proxyPolicy.Value}",
                    GuideKey = "sec_browser_policy",
                };
            }

            var others = group.Count(p => p.Name != "ExtensionInstallForcelist");
            if (others > 0)
            {
                yield return new SecurityItem
                {
                    Id = $"secure/sec_browser_policy/{StableHash(group.Key)}",
                    Section = SecuritySectionKind.Browser,
                    Severity = Severity.Info,
                    Title = L("sec.browser.managed", group.Key, others),
                    Detail = L("sec.browser.managed_detail"),
                    Evidence = string.Join(", ", group.Where(p => p.Name != "ExtensionInstallForcelist").Select(p => p.Name).Distinct().Take(12)),
                    GuideKey = "sec_browser_policy",
                };
            }
        }

        foreach (var ext in security.Extensions)
        {
            var risky = SecurityCatalog.RiskyPermissions(ext.Permissions);
            var severity = ext.ForceInstalled ? Severity.Warning : risky.Count > 0 ? Severity.Info : Severity.Ok;
            var tags = new List<LocText> { L("sec.tag.browser", ext.Browser) };
            if (ext.ForceInstalled) tags.Add(L("sec.tag.forced"));
            tags.AddRange(risky.Select(r => L("ext.perm." + r)));

            yield return new SecurityItem
            {
                Id = $"secure/{(ext.ForceInstalled ? "sec_browser_forced" : "sec_extension")}/{ext.Browser}/{ext.Id}",
                Section = SecuritySectionKind.Browser,
                Severity = severity,
                Title = L("sec.ext.title", ext.Name.Length > 0 ? ext.Name : ext.Id, ext.Version),
                Detail = L(ext.ForceInstalled ? "sec.ext.forced" : risky.Count > 0 ? "sec.ext.risky" : "sec.ext.ok"),
                Evidence = $"{ext.Browser} · {ext.Profile} · {ext.Id}",
                Tags = tags,
                GuideKey = ext.ForceInstalled ? "sec_browser_forced" : "sec_extension",
            };
        }
    }

    // ===== Chứng chỉ & hosts =====

    private static IEnumerable<SecurityItem> Certificates(SecurityData security)
    {
        foreach (var cert in security.AddedRootCertificates)
        {
            var known = SecurityCatalog.IsKnownRoot(cert);
            yield return new SecurityItem
            {
                Id = $"secure/sec_root_cert/{cert.Thumbprint}",
                Section = SecuritySectionKind.Certificates,
                Severity = known ? Severity.Info : Severity.Warning,
                Title = L("sec.cert.title", cert.Subject),
                Detail = L(known ? "sec.cert.known" : "sec.cert.detail", cert.Store, cert.NotAfter),
                Evidence = $"{cert.Issuer} · {cert.Thumbprint}",
                Tags = known ? [L("sec.tag.known_root")] : [],
                Actions = [new SecurityAction(SecurityActionKind.OpenUri, cert.Store == "CurrentUser" ? "certmgr.msc" : "certlm.msc", L("sec.action.certs"))],
                GuideKey = "sec_root_cert",
            };
        }

        var hostsFile = Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");
        foreach (var entry in security.Hosts)
        {
            var verdict = SecurityCatalog.ClassifyHosts(entry);
            yield return new SecurityItem
            {
                Id = $"secure/sec_hosts/{StableHash(entry.Host + entry.Address)}",
                Section = SecuritySectionKind.Certificates,
                Severity = verdict switch
                {
                    SecurityCatalog.HostsVerdict.Redirect => Severity.Critical,
                    SecurityCatalog.HostsVerdict.BlocksSecurity => Severity.Warning,
                    _ => Severity.Info,
                },
                Title = L("sec.hosts.title", entry.Host, entry.Address),
                Detail = L("sec.hosts." + verdict.ToString().ToLowerInvariant()),
                Evidence = $"hosts:{entry.Line}",
                Actions = [new SecurityAction(SecurityActionKind.OpenFile, hostsFile, L("sec.action.open_hosts"))],
                GuideKey = "sec_hosts",
            };
        }
    }

    // ===== Microsoft Defender =====

    private static IEnumerable<SecurityItem> Defender(DefenderInfo defender)
    {
        if (!defender.Available)
        {
            yield return Simple("defender/unavailable", SecuritySectionKind.Defender, Severity.Info, "sec.defender.unavailable", null, noIssue: true);
            yield break;
        }

        var settings = new SecurityAction(SecurityActionKind.OpenUri, "windowsdefender://threatsettings", L("sec.action.defender_settings"));
        foreach (var threat in defender.Threats)
        {
            yield return new SecurityItem
            {
                Id = $"secure/sec_threat/{StableHash(threat.Name + threat.Time)}",
                Section = SecuritySectionKind.Defender,
                Severity = threat.Active ? Severity.Critical : Severity.Info,
                Title = L(threat.Active ? "sec.threat.active" : "sec.threat.resolved", threat.Name),
                Detail = threat.Time is { } time ? L("sec.threat.time", time) : null,
                Evidence = string.Join(" | ", threat.Resources),
                Actions = [new SecurityAction(SecurityActionKind.OpenUri, "windowsdefender://threat", L("sec.action.defender_history"))],
                GuideKey = "sec_threat",
            };
        }

        if (!defender.ExclusionsReadable)
            yield return Simple("exclusions/unreadable", SecuritySectionKind.Defender, Severity.Info, "sec.exclusion.unreadable", null, noIssue: true);

        foreach (var (value, type) in defender.ExclusionPaths.Select(v => (v, "path"))
                     .Concat(defender.ExclusionProcesses.Select(v => (v, "process")))
                     .Concat(defender.ExclusionExtensions.Select(v => (v, "extension"))))
        {
            var risky = SecurityCatalog.IsRiskyExclusion(type == "extension" && !value.StartsWith('.') ? "." + value : value);
            yield return new SecurityItem
            {
                Id = $"secure/sec_exclusion/{StableHash(type + value)}",
                Section = SecuritySectionKind.Defender,
                Severity = risky ? Severity.Warning : Severity.Info,
                Title = L("sec.exclusion.title", value),
                Detail = L(risky ? "sec.exclusion.risky" : "sec.exclusion.detail"),
                Evidence = value,
                Tags = [L("sec.tag.exclusion." + type)],
                Actions = [settings],
                GuideKey = "sec_exclusion",
            };
        }

        if (!defender.IsActiveAntivirus)
        {
            yield return Simple("defender/passive", SecuritySectionKind.Defender, Severity.Info, "sec.defender.passive", null, noIssue: true);
            yield break;
        }

        if (defender.RealTimeEnabled == false)
            yield return Simple("defender/realtime", SecuritySectionKind.Defender, Severity.Critical, "sec.defender.realtime_off", settings, noIssue: true);
        if (defender.TamperProtected == false)
            yield return Simple("sec_tamper/off", SecuritySectionKind.Defender, Severity.Warning, "sec.defender.tamper_off", settings);
        if (defender.QuickScanAgeDays is null or > 30)
            yield return Simple("defender/scan_old", SecuritySectionKind.Defender, Severity.Info, "sec.defender.scan_old", null, noIssue: true);
        if (defender.RealTimeEnabled == true && defender.TamperProtected != false)
            yield return Simple("defender/ok", SecuritySectionKind.Defender, Severity.Ok, "sec.defender.ok", null, noIssue: true);
    }

    // ===== Truy cập từ xa, tài khoản, quản lý =====

    private static IEnumerable<SecurityItem> Access(SecurityData security)
    {
        var e = security.Exposure;
        if (e.RdpEnabled == true)
            yield return Simple("sec_rdp/on", SecuritySectionKind.Access, Severity.Warning, "sec.rdp",
                new SecurityAction(SecurityActionKind.OpenUri, "ms-settings:remotedesktop", L("sec.action.rdp_settings")));
        if (e.RemoteRegistryRunning == true)
            yield return Simple("sec_remote_service/remoteregistry", SecuritySectionKind.Access, Severity.Warning, "sec.remote_registry", null);
        if (e.TelnetRunning == true)
            yield return Simple("sec_remote_service/telnet", SecuritySectionKind.Access, Severity.Warning, "sec.telnet", null);
        if (e.WinRmRunning == true)
            yield return Simple("sec_remote_service/winrm", SecuritySectionKind.Access, Severity.Info, "sec.winrm", null);
        if (e.SshServerRunning == true)
            yield return Simple("sec_remote_service/sshd", SecuritySectionKind.Access, Severity.Info, "sec.sshd", null);
        if (e.Smb1Enabled == true)
            yield return Simple("sec_smb1/on", SecuritySectionKind.Access, Severity.Warning, "sec.smb1", null);
        if (e.AutoLogonWithPassword)
            yield return Simple("sec_autologon/on", SecuritySectionKind.Access, Severity.Warning, "sec.autologon",
                new SecurityAction(SecurityActionKind.OpenUri, "netplwiz", L("sec.action.accounts")));

        var accountsAction = new SecurityAction(SecurityActionKind.OpenUri, "ms-settings:otherusers", L("sec.action.accounts"));
        foreach (var account in security.Accounts.Where(a => a.Enabled))
        {
            if (account.HiddenFromLogon)
            {
                yield return new SecurityItem
                {
                    Id = $"secure/sec_hidden_account/{account.Name}",
                    Section = SecuritySectionKind.Access,
                    Severity = account.IsAdmin ? Severity.Critical : Severity.Warning,
                    Title = L("sec.account.hidden", account.Name),
                    Detail = L(account.IsAdmin ? "sec.account.hidden_admin" : "sec.account.hidden_detail"),
                    Evidence = account.Sid,
                    Actions = [accountsAction],
                    GuideKey = "sec_hidden_account",
                };
            }
            else if (account.Rid == 500)
            {
                yield return Simple($"sec_builtin_admin/{account.Name}", SecuritySectionKind.Access, Severity.Warning, "sec.account.builtin_admin", accountsAction, account.Name);
            }
            else if (account.Rid == 501)
            {
                yield return Simple($"sec_guest/{account.Name}", SecuritySectionKind.Access, Severity.Warning, "sec.account.guest", accountsAction, account.Name);
            }
        }

        var admins = security.Accounts.Where(a => a.Enabled && a.IsAdmin).Select(a => a.Name).ToList();
        if (admins.Count > 0)
        {
            yield return new SecurityItem
            {
                Id = "secure/sec_admins/list",
                Section = SecuritySectionKind.Access,
                Severity = admins.Count > 2 ? Severity.Warning : Severity.Info,
                Title = L("sec.account.admins", admins.Count),
                Detail = L("sec.account.admins_detail"),
                Evidence = string.Join(", ", admins),
                Actions = [accountsAction],
                GuideKey = "sec_admins",
            };
        }

        var m = security.Management;
        if (m.DomainJoined == true)
            yield return Simple("sec_managed/domain", SecuritySectionKind.Access, Severity.Info, "sec.domain", null, m.Domain);
        if (m.MdmEnrolled)
            yield return Simple("sec_managed/mdm", SecuritySectionKind.Access, Severity.Info, "sec.mdm",
                new SecurityAction(SecurityActionKind.OpenUri, "ms-settings:workplace", L("sec.action.work_access")), m.MdmAccount);
    }

    // ===== Cấu hình hệ thống =====

    private static IEnumerable<SecurityItem> SystemConfig(ScanData data)
    {
        foreach (var hijack in data.Security.Hijacks)
        {
            var severity = SecurityCatalog.ClassifyHijack(hijack);
            if (severity == Severity.Ok) continue;
            var key = hijack.Kind switch
            {
                HijackKind.IfeoDebugger => "sec_ifeo",
                HijackKind.AppInitDlls => "sec_appinit",
                _ => "sec_winlogon",
            };
            yield return new SecurityItem
            {
                Id = $"secure/{key}/{StableHash(hijack.Location + hijack.Target)}",
                Section = SecuritySectionKind.System,
                Severity = severity,
                Title = L("sec.hijack." + hijack.Kind.ToString().ToLowerInvariant(), hijack.Target),
                Detail = L("sec.hijack.detail", hijack.Value),
                Evidence = hijack.Location,
                GuideKey = key,
            };
        }

        var e = data.Security.Exposure;
        if (e.SmartScreenOff == true)
            yield return Simple("sec_smartscreen/off", SecuritySectionKind.System, Severity.Warning, "sec.smartscreen",
                new SecurityAction(SecurityActionKind.OpenUri, "windowsdefender://appbrowser", L("sec.action.defender_settings")));

        var system = data.System;
        if (system.FirewallKnown && system.FirewallDisabledProfiles.Count > 0 && !system.ThirdPartyFirewall)
            yield return Simple("system/firewall", SecuritySectionKind.System, Severity.Critical, "sec.firewall_off",
                new SecurityAction(SecurityActionKind.OpenUri, "windowsdefender://network", L("sec.action.firewall")), noIssue: true);
        if (system.UacEnabled == false)
            yield return Simple("system/uac", SecuritySectionKind.System, Severity.Warning, "sec.uac_off", null, noIssue: true);
        if (data.Hardware.Firmware.SecureBootEnabled == false)
            yield return Simple("sec_secureboot/off", SecuritySectionKind.System, Severity.Info, "sec.secureboot_off", null);
        if (e.BitLockerOn == false)
            yield return Simple("sec_bitlocker/off", SecuritySectionKind.System, Severity.Info, "sec.bitlocker_off", null);
    }

    // ===== Tiện ích =====

    private static SecurityItem Simple(string id, SecuritySectionKind section, Severity severity, string key, SecurityAction? action, string? arg = null, bool noIssue = false)
    {
        var guide = id.StartsWith("sec_", StringComparison.Ordinal) ? id[..id.IndexOf('/')] : null;
        return new SecurityItem
        {
            Id = "secure/" + id,
            Section = section,
            Severity = severity,
            Title = arg is null ? L(key) : L(key, arg),
            Detail = L(key + "_detail"),
            Actions = action is null ? [] : [action],
            GuideKey = guide,
            NoIssue = noIssue,
        };
    }

    private static void AddTrustTags(List<LocText> tags, FileTrust? file)
    {
        if (file is null) return;
        if (!file.Exists)
        {
            tags.Add(L("sec.tag.missing"));
            return;
        }

        if (file.Signed == false) tags.Add(L("sec.tag.unsigned"));
        if (file.Kind == PathKind.Temp) tags.Add(L("sec.tag.temp_dir"));
        else if (file.UserWritable) tags.Add(L("sec.tag.user_dir"));
        if (file.Publisher.Length > 0) tags.Add(L("sec.tag.publisher", file.Publisher));
    }

    private static void AddFileActions(List<SecurityAction> actions, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        actions.Add(new SecurityAction(SecurityActionKind.OpenLocation, path, L("sec.action.open_location")));
        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".vbs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".scr", StringComparison.OrdinalIgnoreCase))
            actions.Add(new SecurityAction(SecurityActionKind.VirusTotal, path, L("sec.action.virustotal")));
    }

    /// <summary>Băm ổn định (FNV-1a) để Id không đổi giữa các lần chạy.</summary>
    internal static string StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var ch in text.ToUpperInvariant())
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }
}
