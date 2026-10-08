using NewHwInspector.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NewHwInspector.Services;

public static class ReportService
{
    public static string AppVersion
    {
        get
        {
            try
            {
                var assembly = typeof(ReportService).Assembly;
                var v = assembly.GetName().Version;
                return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
            }
            catch { return "1.0.0"; }
        }
    }

    public static string DefaultReportFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "New HwInspector Reports");

    public static async Task<string> GenerateAsync(ReportOptions opts, OverviewData overview, List<HardwareNode> tree, IReadOnlyList<SensorEntry> sensors, IReadOnlyList<AlertRule> alerts)
    {
        string folder = AppSettings.Current.ReportFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            folder = DefaultReportFolder;
            Directory.CreateDirectory(folder);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string ext = opts.Format.ToLowerInvariant() switch { "html" => "html", "json" => "json", "csv" => "csv", _ => "txt" };
        string fileName = $"NewHwInspector_Report_{timestamp}.{ext}";
        string fullPath = Path.Combine(folder, fileName);

        string content = opts.Format switch
        {
            "HTML" => BuildHtml(overview, tree, sensors, alerts, opts),
            "JSON" => BuildJson(overview, tree, sensors, alerts, opts),
            "CSV" => BuildCsv(sensors),
            _ => BuildText(overview, tree, sensors, alerts, opts)
        };

        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
        AppLogger.Log($"Report saved: {fullPath} format={opts.Format}");
        return fullPath;
    }

    private static string BuildText(OverviewData ov, List<HardwareNode> tree, IReadOnlyList<SensorEntry> sensors, IReadOnlyList<AlertRule> alerts, ReportOptions opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("New HwInspector — Hardware Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Machine: {Environment.MachineName}");
        sb.AppendLine($"App version: {AppVersion}");
        sb.AppendLine(new string('=', 70));

        if (opts.IncludeOverview)
        {
            sb.AppendLine("\n[System Overview]");
            sb.AppendLine($"  CPU: {ov.CpuName} ({ov.CpuCores}, {ov.CpuBaseClock}, Socket {ov.CpuSocket}, Temp {ov.CpuTempText})");
            sb.AppendLine($"  Motherboard: {ov.BoardVendor} {ov.BoardModel} / BIOS {ov.BiosVersion} ({ov.BiosDate})");
            sb.AppendLine($"  Memory: {ov.TotalRamText} {ov.RamType} {ov.RamSpeed} — {ov.MemoryUsedText}");
            sb.AppendLine($"  GPU: {ov.GpuName} — {ov.GpuMemory} — Driver {ov.GpuDriver}");
            sb.AppendLine($"  Storage: {ov.StorageSummary} — System drive {ov.SystemDriveFree}");
            sb.AppendLine($"  OS: {ov.OsName} {ov.OsVersion} Build {ov.OsBuild} {ov.OsArch}");
            sb.AppendLine($"  Firmware: {ov.FirmwareMode} / Secure Boot {ov.SecureBoot} / {ov.TpmText} / Virtualization {ov.Virtualization}");
            if (ov.Warnings.Count > 0) sb.AppendLine($"  Warnings: {string.Join("; ", ov.Warnings)}");
        }

        if (opts.IncludeHardware)
        {
            sb.AppendLine("\n[Hardware Tree]");
            foreach (var node in tree) AppendNodeText(sb, node, 0);
        }

        if (opts.IncludeSensors)
        {
            sb.AppendLine("\n[Sensors]");
            foreach (var g in sensors.GroupBy(s => s.Group))
            {
                sb.AppendLine($"\n  {g.Key}:");
                sb.AppendLine($"    {"Sensor",-30} {"Current",-12} {"Min",-12} {"Max",-12} {"Average",-12} {"Status"}");
                sb.AppendLine($"    {new string('-', 95)}");
                foreach (var s in g)
                {
                    string status = s.IsAvailable ? s.Status.ToString() : "Unavailable";
                    sb.AppendLine($"    {s.Name,-30} {s.DisplayCurrent,-12} {s.DisplayMin,-12} {s.DisplayMax,-12} {s.DisplayAverage,-12} {status}");
                }
            }
        }

        if (opts.IncludeAlerts)
        {
            sb.AppendLine("\n[Alerts]");
            if (alerts.Count == 0) sb.AppendLine("  No alert rules configured.");
            else foreach (var r in alerts) sb.AppendLine($"  {(r.Enabled ? "[ON]" : "[OFF]")} {r.Group}/{r.SensorName} {r.Condition} {r.Threshold} {r.Unit} — Active={r.IsActive}");
        }

        sb.AppendLine("\n— End of report —");
        sb.AppendLine("Note: Some sensor values may be 'n/a' if not exposed by firmware or driver. No network transmission occurred.");
        return sb.ToString();
    }

    private static void AppendNodeText(StringBuilder sb, HardwareNode node, int indent)
    {
        string pad = new string(' ', indent * 2);
        sb.AppendLine($"{pad}- {node.Name} [{node.Type}] {(node.Summary.Length > 0 ? $"— {node.Summary}" : "")} {node.Icon}");
        foreach (var p in node.Properties) sb.AppendLine($"{pad}    {p.Name}: {p.Value} {(p.Note?.Length>0? $"({p.Note})":"")}");
        foreach (var c in node.Children) AppendNodeText(sb, c, indent + 1);
    }

    private static string BuildHtml(OverviewData ov, List<HardwareNode> tree, IReadOnlyList<SensorEntry> sensors, IReadOnlyList<AlertRule> alerts, ReportOptions opts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'><title>New HwInspector Report</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;background:#0B1220;color:#E6E8EC;margin:32px}h1{color:#4CC3FF}h2{color:#9AE0FF;border-bottom:1px solid #1F2A44;padding-bottom:6px}table{border-collapse:collapse;width:100%;margin:8px 0}th,td{border:1px solid #26324F;padding:6px 8px;text-align:left;font-size:13px}th{background:#121D33;color:#7EC8FF}tr:nth-child(even){background:#0F1A2E}.warn{color:#E8B33C}.crit{color:#E8574E}.ok{color:#4CC38A}.section{background:#111C33;border:1px solid #1F2A44;border-radius:8px;padding:14px;margin:16px 0}</style></head><body>");
        sb.AppendLine($"<h1>New HwInspector &mdash; Hardware Report</h1><p>Generated {DateTime.Now:yyyy-MM-dd HH:mm:ss} &bull; Machine {Environment.MachineName} &bull; v{AppVersion}</p>");
        if (opts.IncludeOverview)
        {
            sb.AppendLine("<div class='section'><h2>System Overview</h2><table>");
            void Row(string k,string v)=> sb.AppendLine($"<tr><th style='width:180px'>{System.Net.WebUtility.HtmlEncode(k)}</th><td>{System.Net.WebUtility.HtmlEncode(v)}</td></tr>");
            Row("CPU", $"{ov.CpuName} ({ov.CpuCores}, {ov.CpuBaseClock}, {ov.CpuTempText})");
            Row("Motherboard", $"{ov.BoardVendor} {ov.BoardModel}");
            Row("BIOS", $"{ov.BiosVersion} ({ov.BiosDate})");
            Row("Memory", $"{ov.TotalRamText} {ov.RamType} {ov.RamSpeed} — {ov.MemoryUsedText}");
            Row("GPU", $"{ov.GpuName} — {ov.GpuMemory} — Driver {ov.GpuDriver}");
            Row("Storage", $"{ov.StorageSummary} — {ov.SystemDriveFree}");
            Row("OS", $"{ov.OsName} {ov.OsVersion} Build {ov.OsBuild} {ov.OsArch}");
            Row("Firmware", $"{ov.FirmwareMode} / Secure Boot {ov.SecureBoot} / {ov.TpmText}");
            sb.AppendLine("</table></div>");
        }
        if (opts.IncludeHardware)
        {
            sb.AppendLine("<div class='section'><h2>Hardware Tree</h2>");
            foreach (var n in tree) AppendNodeHtml(sb, n, 0);
            sb.AppendLine("</div>");
        }
        if (opts.IncludeSensors)
        {
            sb.AppendLine("<div class='section'><h2>Sensors</h2>");
            foreach (var g in sensors.GroupBy(s => s.Group))
            {
                sb.AppendLine($"<h3>{System.Net.WebUtility.HtmlEncode(g.Key)}</h3><table><tr><th>Sensor</th><th>Current</th><th>Min</th><th>Max</th><th>Average</th><th>Status</th></tr>");
                foreach (var s in g)
                {
                    string cls = s.Status == SensorStatus.Critical ? "crit" : s.Status == SensorStatus.Warning ? "warn" : "ok";
                    sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(s.Name)}</td><td>{System.Net.WebUtility.HtmlEncode(s.DisplayCurrent)}</td><td>{System.Net.WebUtility.HtmlEncode(s.DisplayMin)}</td><td>{System.Net.WebUtility.HtmlEncode(s.DisplayMax)}</td><td>{System.Net.WebUtility.HtmlEncode(s.DisplayAverage)}</td><td class='{cls}'>{s.Status}</td></tr>");
                }
                sb.AppendLine("</table>");
            }
            sb.AppendLine("</div>");
        }
        if (opts.IncludeAlerts)
        {
            sb.AppendLine("<div class='section'><h2>Alerts</h2>");
            if (alerts.Count == 0) sb.AppendLine("<p>No alert rules.</p>");
            else
            {
                sb.AppendLine("<table><tr><th>Sensor</th><th>Condition</th><th>Enabled</th><th>Active</th></tr>");
                foreach (var r in alerts) sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(r.SensorName)}</td><td>{r.Condition} {r.Threshold} {r.Unit}</td><td>{r.Enabled}</td><td>{(r.IsActive?"<span class='crit'>ALERT</span>":"OK")}</td></tr>");
                sb.AppendLine("</table>");
            }
            sb.AppendLine("</div>");
        }
        sb.AppendLine("<p style='color:#7A869A;font-size:12px'>No network transmission &mdash; all data collected locally via Windows Management Instrumentation.</p></body></html>");
        return sb.ToString();
    }
    private static void AppendNodeHtml(StringBuilder sb, HardwareNode node, int depth)
    {
        sb.AppendLine($"<h4 style='margin:{8+depth*4}px 0 4px 0;color:#C8D6F0'>{System.Net.WebUtility.HtmlEncode(node.Name)} <span style='color:#7EC8FF;font-weight:400'>[{System.Net.WebUtility.HtmlEncode(node.Type)}]</span> <span style='color:#9AA0A6'>{System.Net.WebUtility.HtmlEncode(node.Summary)}</span></h4>");
        if (node.Properties.Count > 0)
        {
            sb.AppendLine("<table><tr><th>Property</th><th>Value</th></tr>");
            foreach (var p in node.Properties) sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(p.Name)}</td><td>{System.Net.WebUtility.HtmlEncode(p.Value)}</td></tr>");
            sb.AppendLine("</table>");
        }
        foreach (var c in node.Children) AppendNodeHtml(sb, c, depth+1);
    }

    private static string BuildJson(OverviewData ov, List<HardwareNode> tree, IReadOnlyList<SensorEntry> sensors, IReadOnlyList<AlertRule> alerts, ReportOptions opts)
    {
        var obj = new
        {
            generatedAt = DateTime.Now,
            machine = Environment.MachineName,
            version = AppVersion,
            overview = opts.IncludeOverview ? new
            {
                cpu = new { name = ov.CpuName, cores = ov.CpuCores, baseClock = ov.CpuBaseClock, socket = ov.CpuSocket, tempC = ov.CpuTempC },
                motherboard = new { vendor = ov.BoardVendor, model = ov.BoardModel, bios = ov.BiosVersion, biosDate = ov.BiosDate },
                memory = new { total = ov.TotalRamText, type = ov.RamType, speed = ov.RamSpeed, used = ov.MemoryUsedText, totalBytes = ov.TotalRamBytes },
                gpu = new { name = ov.GpuName, memory = ov.GpuMemory, driver = ov.GpuDriver },
                storage = new { summary = ov.StorageSummary, systemDriveFree = ov.SystemDriveFree, count = ov.DriveCount },
                os = new { name = ov.OsName, version = ov.OsVersion, build = ov.OsBuild, arch = ov.OsArch },
                firmware = new { mode = ov.FirmwareMode, secureBoot = ov.SecureBoot, tpm = ov.TpmText, virtualization = ov.Virtualization },
                warnings = ov.Warnings
            } : null,
            hardware = opts.IncludeHardware ? tree.Select(SerializeNode).ToList() : null,
            sensors = opts.IncludeSensors ? sensors.Select(s => new { s.Group, s.Name, s.Unit, s.Kind, current = s.DisplayCurrent, min = s.DisplayMin, max = s.DisplayMax, avg = s.DisplayAverage, status = s.Status.ToString(), available = s.IsAvailable }).ToList() : null,
            alerts = opts.IncludeAlerts ? alerts.Select(a => new { a.Group, a.SensorName, a.Condition, a.Threshold, a.Unit, a.Enabled, a.IsActive }).ToList() : null
        };
        return JsonConvert.SerializeObject(obj, Formatting.Indented);
    }

    private static object SerializeNode(HardwareNode n) => new
    {
        name = n.Name,
        type = n.Type,
        summary = n.Summary,
        properties = n.Properties.Select(p => new { name = p.Name, value = p.Value }).ToList(),
        children = n.Children.Select(SerializeNode).ToList()
    };

    private static string BuildCsv(IReadOnlyList<SensorEntry> sensors)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Group,Sensor,Current,Min,Max,Average,Unit,Status,Available");
        foreach (var s in sensors)
        {
            string esc(string t) => $"\"{t.Replace("\"","\"\"")}\"";
            sb.AppendLine($"{esc(s.Group)},{esc(s.Name)},{esc(s.DisplayCurrent)},{esc(s.DisplayMin)},{esc(s.DisplayMax)},{esc(s.DisplayAverage)},{esc(s.Unit)},{s.Status},{s.IsAvailable}");
        }
        return sb.ToString();
    }
}
