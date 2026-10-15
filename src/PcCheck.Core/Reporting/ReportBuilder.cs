using System.Net;
using System.Text;
using PcCheck.Core.Analysis;
using PcCheck.Core.Localization;
using PcCheck.Core.Models;
using PcCheck.Core.Util;

namespace PcCheck.Core.Reporting;

/// <summary>Xuất báo cáo HTML độc lập (một tệp, CSS nội tuyến) theo ngôn ngữ đang chọn.</summary>
public static class ReportBuilder
{
    public static string BuildHtml(ScanResult result, string appVersion)
    {
        var d = result.Data;
        var hw = d.Hardware;
        var culture = Loc.Instance.Culture;
        var sb = new StringBuilder(64 * 1024);

        sb.Append("<!doctype html><html lang=\"").Append(Loc.Instance.Language).Append("\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>").Append(E(Loc.T("report.title"))).Append(" – ").Append(E(hw.Os.MachineName)).Append("</title>")
          .Append("<style>").Append(Css).Append("</style></head><body><main>");

        // Tiêu đề + điểm
        var grade = HealthScore.Grade(result.Score);
        sb.Append("<header><div><h1>").Append(E(Loc.T("report.title"))).Append("</h1><p class=\"muted\">")
          .Append(E(hw.Os.MachineName)).Append(" · ").Append(E(result.Time.ToString("f", culture)))
          .Append(" · PC Check ").Append(E(appVersion)).Append("</p></div>")
          .Append("<div class=\"score score-").Append(grade.ToString().ToLowerInvariant()).Append("\"><b>")
          .Append(result.Score).Append("</b><span>").Append(E(Labels.Grade(grade))).Append("</span></div></header>");

        sb.Append("<p class=\"summary\">")
          .Append(E(Loc.F("report.summary", result.Count(Severity.Critical), result.Count(Severity.Warning), result.Count(Severity.Info))))
          .Append("</p>");

        // Tổng quan hệ thống
        Section(sb, Loc.T("report.section.system"));
        var cpu = hw.Cpus.FirstOrDefault();
        Table(sb, null,
        [
            [Loc.T("hw.computer"), $"{hw.Computer.Manufacturer} {hw.Computer.Model}".Trim()],
            [Loc.T("hw.os"), $"{hw.Os.Caption} {hw.Os.DisplayVersion} (build {hw.Os.Build}.{hw.Os.Revision})"],
            [Loc.T("hw.cpu"), cpu is null ? "—" : $"{cpu.Name} · {cpu.Cores}C/{cpu.Threads}T"],
            [Loc.T("hw.ram"), $"{Format.Bytes(hw.Memory.InstalledBytes)} · {hw.Memory.Modules.Count}/{hw.Memory.SlotCount} {Loc.T("hw.slots")}"],
            [Loc.T("hw.gpu"), string.Join(", ", hw.Gpus.Where(g => !g.IsVirtual).Select(g => g.Name))],
            [Loc.T("hw.mainboard"), $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim()],
            [Loc.T("hw.bios"), $"{hw.Board.BiosVersion} ({Format.Date(hw.Board.BiosDate)})"],
            [Loc.T("hw.uptime"), Format.Duration(d.System.Uptime)],
        ]);

        // Vấn đề
        Section(sb, Loc.F("report.section.issues", result.Issues.Count));
        if (result.Issues.Count == 0)
        {
            sb.Append("<p class=\"ok\">").Append(E(Loc.T("issues.none"))).Append("</p>");
        }
        else
        {
            sb.Append("<table><thead><tr><th>").Append(E(Loc.T("report.col.severity"))).Append("</th><th>")
              .Append(E(Loc.T("report.col.issue"))).Append("</th><th>").Append(E(Loc.T("report.col.recommendation"))).Append("</th></tr></thead><tbody>");
            foreach (var issue in result.Issues)
            {
                sb.Append("<tr><td><span class=\"badge ").Append(issue.Severity.ToString().ToLowerInvariant()).Append("\">")
                  .Append(E(Labels.Severity(issue.Severity))).Append("</span><div class=\"muted small\">").Append(E(Labels.Category(issue.Category))).Append("</div></td><td><b>")
                  .Append(E(issue.Title.ToString())).Append("</b><div class=\"muted\">").Append(E(issue.Detail.ToString())).Append("</div></td><td>")
                  .Append(E(issue.Recommendation?.ToString() ?? string.Join(" · ", issue.Fixes.Select(f => f.Label.ToString())))).Append("</td></tr>");
            }

            sb.Append("</tbody></table>");
        }

        // Tương thích
        Section(sb, Loc.T("report.section.compatibility"));
        sb.Append("<table><tbody>");
        foreach (var check in result.Checks)
        {
            sb.Append("<tr><td class=\"nowrap\"><span class=\"badge ").Append(CheckClass(check.Status)).Append("\">").Append(E(Labels.Check(check.Status)))
              .Append("</span></td><td><b>").Append(E(check.Title.ToString())).Append("</b><div class=\"muted small\">").Append(E(check.Group.ToString())).Append("</div></td><td>")
              .Append(E(check.Detail.ToString()));
            if (check.Recommendation is not null) sb.Append("<div class=\"muted\">").Append(E(check.Recommendation.ToString())).Append("</div>");
            sb.Append("</td></tr>");
        }

        sb.Append("</tbody></table>");

        // Nhiệt độ
        Section(sb, Loc.T("report.section.sensors"));
        Table(sb, null,
        [
            [Loc.T("sensor.cpu_temp"), d.Sensors.CpuTemperatureEstimated
                ? Loc.F("dash.temp_estimated", Format.Celsius(d.Sensors.CpuTemperature))
                : Format.Celsius(d.Sensors.CpuTemperature)],
            [Loc.T("sensor.cpu_load"), Format.Percent(d.Sensors.CpuLoad)],
            [Loc.T("sensor.gpu_temp"), Format.Celsius(d.Sensors.GpuTemperature)],
            [Loc.T("sensor.gpu_load"), Format.Percent(d.Sensors.GpuLoad)],
            [Loc.T("sensor.ram_load"), Format.Percent(hw.Memory.UsedPercent)],
        ]);

        // Ổ cứng
        Section(sb, Loc.T("report.section.storage"));
        Table(sb, [Loc.T("disk.model"), Loc.T("disk.type"), Loc.T("disk.size"), Loc.T("disk.health"), Loc.T("disk.temperature"), Loc.T("disk.life"), Loc.T("disk.power_on")],
            hw.Disks.Select(disk => new[]
            {
                disk.Model, $"{Labels.MediaType(disk)} · {disk.BusType}", Format.MarketingSize(disk.SizeBytes), Labels.DiskHealth(disk.Health),
                Format.Celsius(disk.TemperatureC), disk.WearPercent is { } w ? Format.Percent(100 - w) : "—",
                disk.PowerOnHours is { } h ? Loc.F("unit.hours", h) : "—",
            }).ToList());
        Table(sb, [Loc.T("volume.drive"), Loc.T("volume.fs"), Loc.T("volume.total"), Loc.T("volume.free")],
            hw.Volumes.Select(v => new[] { $"{v.Letter} {v.Label}".Trim(), v.FileSystem, Format.Bytes(v.TotalBytes), $"{Format.Bytes(v.FreeBytes)} ({Format.Percent(v.FreePercent)})" }).ToList());

        // RAM
        if (hw.Memory.Modules.Count > 0)
        {
            Section(sb, Loc.T("report.section.memory"));
            Table(sb, [Loc.T("ram.slot"), Loc.T("ram.capacity"), Loc.T("ram.type"), Loc.T("ram.speed"), Loc.T("ram.vendor"), Loc.T("ram.part")],
                hw.Memory.Modules.Select(m => new[]
                {
                    m.Slot, Format.Bytes(m.CapacityBytes), $"{m.Type} {m.FormFactor}".Trim(), $"{m.ConfiguredSpeedMhz}/{m.SpeedMhz} MHz", m.Manufacturer, m.PartNumber,
                }).ToList());
        }

        // Pin
        if (hw.Battery is { } battery)
        {
            Section(sb, Loc.T("report.section.battery"));
            Table(sb, null,
            [
                [Loc.T("battery.design"), battery.DesignCapacity.ToString("N0", culture) + " mWh"],
                [Loc.T("battery.full"), battery.FullChargeCapacity.ToString("N0", culture) + " mWh"],
                [Loc.T("battery.wear"), Format.Percent(battery.WearPercent)],
                [Loc.T("battery.cycles"), battery.CycleCount?.ToString(culture) ?? "—"],
            ]);
        }

        // Driver
        Section(sb, Loc.F("report.section.drivers", result.DriverProblems.Count));
        if (result.DriverProblems.Count == 0)
        {
            sb.Append("<p class=\"ok\">").Append(E(Loc.T("drivers.all_ok"))).Append("</p>");
        }
        else
        {
            Table(sb, [Loc.T("driver.device"), Loc.T("driver.problem"), Loc.T("driver.version"), Loc.T("driver.date")],
                result.DriverProblems.Select(p => new[]
                {
                    p.Device.Name, Labels.DriverProblem(p.Kind) + (p.Device.ErrorCode != 0 ? $" (Code {p.Device.ErrorCode})" : ""),
                    Format.OrDash(p.Device.DriverVersion), Format.Date(p.Device.DriverDate),
                }).ToList());
        }

        // Rác & phần mềm
        Section(sb, Loc.T("report.section.cleanup"));
        Table(sb, [Loc.T("junk.category"), Loc.T("junk.size"), Loc.T("junk.files")],
            d.Junk.Where(j => j.Bytes > 0).OrderByDescending(j => j.Bytes)
                .Select(j => new[] { j.Name.ToString(), Format.Bytes(j.Bytes), j.Files.ToString("N0", culture) }).ToList());

        var flagged = d.Programs.Where(p => p.BloatReasonKey is not null || CleanupAnalyzer.IsUnused(p, result.Time)).ToList();
        if (flagged.Count > 0)
        {
            Table(sb, [Loc.T("software.name"), Loc.T("software.publisher"), Loc.T("software.last_used"), Loc.T("software.note")],
                flagged.Select(p => new[]
                {
                    p.Name, p.Publisher, Format.Date(p.LastUsed),
                    p.BloatReasonKey is not null ? Loc.T(p.BloatReasonKey) : Loc.F("software.unused_days", p.DaysSinceUsed(result.Time) ?? 0),
                }).ToList());
        }

        sb.Append("<footer class=\"muted small\">").Append(E(Loc.F("report.footer", result.Duration.TotalSeconds))).Append("</footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    private static string CheckClass(CheckStatus status) => status switch
    {
        CheckStatus.Pass => "ok",
        CheckStatus.Warning => "warning",
        CheckStatus.Fail => "critical",
        _ => "info",
    };

    private static void Section(StringBuilder sb, string title) =>
        sb.Append("<h2>").Append(E(title)).Append("</h2>");

    private static void Table(StringBuilder sb, string[]? headers, IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0)
        {
            sb.Append("<p class=\"muted\">—</p>");
            return;
        }

        sb.Append("<table>");
        if (headers is not null)
        {
            sb.Append("<thead><tr>");
            foreach (var h in headers) sb.Append("<th>").Append(E(h)).Append("</th>");
            sb.Append("</tr></thead>");
        }

        sb.Append("<tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            for (var i = 0; i < row.Length; i++)
            {
                sb.Append(headers is null && i == 0 ? "<th scope=\"row\">" : "<td>").Append(E(row[i])).Append(headers is null && i == 0 ? "</th>" : "</td>");
            }

            sb.Append("</tr>");
        }

        sb.Append("</tbody></table>");
    }

    private static string E(string? text) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(text) ? "—" : text);

    private const string Css = """
        :root{--bg:#f5f7fb;--card:#fff;--text:#1a1d24;--muted:#5f6675;--line:#e3e7ee;--ok:#16a34a;--info:#0284c7;--warning:#d97706;--critical:#dc2626;--accent:#3b6ff5}
        *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.5 "Segoe UI",system-ui,sans-serif}
        main{max-width:1080px;margin:0 auto;padding:32px 20px}
        header{display:flex;justify-content:space-between;align-items:center;gap:16px;background:var(--card);border:1px solid var(--line);border-radius:14px;padding:20px 24px}
        h1{margin:0;font-size:24px}h2{margin:28px 0 10px;font-size:17px}
        .muted{color:var(--muted)}.small{font-size:12px}.nowrap{white-space:nowrap}.ok{color:var(--ok)}
        .summary{margin:14px 2px;font-weight:600}
        .score{display:flex;flex-direction:column;align-items:center;justify-content:center;width:96px;height:96px;border-radius:50%;border:6px solid var(--accent)}
        .score b{font-size:30px;line-height:1}.score span{font-size:12px;color:var(--muted)}
        .score-excellent,.score-good{border-color:var(--ok)}.score-fair{border-color:var(--warning)}.score-poor{border-color:var(--critical)}
        table{width:100%;border-collapse:collapse;background:var(--card);border:1px solid var(--line);border-radius:12px;overflow:hidden;margin-bottom:12px}
        th,td{text-align:left;vertical-align:top;padding:9px 12px;border-bottom:1px solid var(--line)}
        thead th{background:#eef2f8;font-size:12px;text-transform:uppercase;letter-spacing:.03em;color:var(--muted)}
        tbody th{width:220px;color:var(--muted);font-weight:600}
        .badge{display:inline-block;padding:2px 10px;border-radius:999px;font-size:12px;font-weight:600;color:#fff;background:var(--info)}
        .badge.ok{background:var(--ok)}.badge.warning{background:var(--warning)}.badge.critical{background:var(--critical)}
        footer{margin-top:28px;text-align:center}
        @media print{body{background:#fff}header,table{border-color:#ccc}}
        """;
}
