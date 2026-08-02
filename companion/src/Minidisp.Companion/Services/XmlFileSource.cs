using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Minidisp.Companion.Models;

namespace Minidisp.Companion.Services;

/// <summary>
/// Watches a user-supplied auto-updating XML file and maps it to stats.
/// Supports two formats (docs/RESEARCH-projects.md):
///  1. Native "minidisp" schema — see companion/docs/sample.xml.
///  2. AIDA64-style sensor fragments (&lt;temp&gt;/&lt;sys&gt;/&lt;fan&gt; items with
///     &lt;id&gt;/&lt;label&gt;/&lt;value&gt;), which may lack a single root element.
/// </summary>
public sealed class XmlFileSource : ISensorSource
{
    private readonly ILogger _log;
    private readonly string _path;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _gate = new();
    private StatsSnapshot? _current;
    private DateTime _lastWrite = DateTime.MinValue;

    public string Name => $"XML file ({Path.GetFileName(_path)})";

    public XmlFileSource(ILogger<XmlFileSource> log, string path)
    {
        _log = log;
        _path = Path.GetFullPath(path);

        var dir = Path.GetDirectoryName(_path);
        if (dir is not null && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => TryReload();
            _watcher.Created += (_, _) => TryReload();
        }
        TryReload();
    }

    public StatsSnapshot? GetSnapshot()
    {
        // Poll as a fallback — some writers replace the file in ways
        // FileSystemWatcher misses.
        var writeTime = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
        if (writeTime != _lastWrite) TryReload();
        lock (_gate) return _current;
    }

    private void TryReload()
    {
        try
        {
            if (!File.Exists(_path)) return;
            string content = ReadWithRetry(_path);
            if (content.Length == 0) return;

            var snapshot = Parse(content);
            lock (_gate)
            {
                _current = snapshot;
                _lastWrite = File.GetLastWriteTimeUtc(_path);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning("Failed to read XML {Path}: {Error}", _path, ex.Message);
        }
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd().Replace("\0", "");
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100); // writer holds the file — retry briefly
            }
        }
    }

    private StatsSnapshot Parse(string content)
    {
        XElement root;
        try
        {
            root = XElement.Parse(content);
        }
        catch (System.Xml.XmlException)
        {
            // AIDA64 shared-memory style: sibling fragments with no root.
            root = XElement.Parse($"<root>{content}</root>");
        }

        return root.Name.LocalName.Equals("minidisp", StringComparison.OrdinalIgnoreCase)
            ? ParseNative(root)
            : ParseAida64(root);
    }

    // ---- native schema ----------------------------------------------------

    private static StatsSnapshot ParseNative(XElement root)
    {
        var snap = new StatsSnapshot
        {
            Host = (string?)root.Element("host"),
            Uptime = (long?)root.Element("uptime"),
        };

        var cpu = root.Element("cpu");
        if (cpu is not null)
        {
            snap.Cpu = new CpuStats
            {
                Load = (float?)cpu.Attribute("load"),
                Temp = (float?)cpu.Attribute("temp"),
                Freq = (float?)cpu.Attribute("freq"),
                Name = (string?)cpu.Attribute("name"),
            };
            var cores = ((string?)cpu.Attribute("cores"))?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => float.TryParse(c, System.Globalization.CultureInfo.InvariantCulture,
                    out var v) ? v : 0f)
                .ToList();
            if (cores is { Count: > 0 }) snap.Cpu.Cores = cores;
        }

        var mem = root.Element("mem");
        if (mem is not null)
        {
            snap.Mem = new MemStats
            {
                Pct = (float?)mem.Attribute("pct"),
                Used = (float?)mem.Attribute("used"),
                Total = (float?)mem.Attribute("total"),
            };
        }

        var gpu = root.Element("gpu");
        if (gpu is not null)
        {
            snap.Gpu = new GpuStats
            {
                Load = (float?)gpu.Attribute("load"),
                Temp = (float?)gpu.Attribute("temp"),
                Name = (string?)gpu.Attribute("name"),
            };
        }

        var nets = root.Elements("net")
            .Select(n => new NetStats
            {
                Interface = (string?)n.Attribute("if"),
                Ip = (string?)n.Attribute("ip"),
                Up = (float?)n.Attribute("up"),
                Down = (float?)n.Attribute("down"),
            })
            .ToList();
        if (nets.Count > 0) snap.Net = nets;

        var disks = root.Elements("disk")
            .Select(d => new DiskStats
            {
                Name = (string?)d.Attribute("n"),
                Pct = (float?)d.Attribute("pct"),
                Free = (float?)d.Attribute("free"),
            })
            .ToList();
        if (disks.Count > 0) snap.Disk = disks;

        // <value id="myapp.status">Running</value> — arbitrary user data that
        // themes bind to by id (numbers stay numeric, everything else string).
        var custom = new Dictionary<string, object>();
        foreach (var v in root.Descendants("value"))
        {
            var id = (string?)v.Attribute("id");
            if (string.IsNullOrWhiteSpace(id)) continue;
            var text = v.Value.Trim();
            custom[id] = double.TryParse(text,
                System.Globalization.CultureInfo.InvariantCulture, out var num)
                ? num : text;
        }
        if (custom.Count > 0) snap.Custom = custom;

        return snap;
    }

    // ---- AIDA64-style fragments -------------------------------------------

    private static StatsSnapshot ParseAida64(XElement root)
    {
        var snap = new StatsSnapshot { Host = Environment.MachineName };

        foreach (var item in root.Elements())
        {
            var id = (string?)item.Element("id") ?? "";
            var label = ((string?)item.Element("label") ?? "").ToLowerInvariant();
            var valueText = (string?)item.Element("value") ?? "";
            if (!float.TryParse(valueText, System.Globalization.CultureInfo.InvariantCulture,
                    out var value))
                continue;

            switch (item.Name.LocalName)
            {
                case "temp":
                    if (label.Contains("cpu"))
                    {
                        snap.Cpu ??= new CpuStats();
                        snap.Cpu.Temp ??= value;
                    }
                    else if (label.Contains("gpu"))
                    {
                        snap.Gpu ??= new GpuStats();
                        snap.Gpu.Temp ??= value;
                    }
                    break;

                case "sys":
                    // AIDA64 "sys" ids: SCPUUTIL, SMEMUTIL, SCPUCLK, SUSEDMEM...
                    switch (id.ToUpperInvariant())
                    {
                        case "SCPUUTIL":
                            snap.Cpu ??= new CpuStats();
                            snap.Cpu.Load = value;
                            break;
                        case "SCPUCLK":
                            snap.Cpu ??= new CpuStats();
                            snap.Cpu.Freq = (float)Math.Round(value / 1000, 2); // MHz -> GHz
                            break;
                        case "SMEMUTIL":
                            snap.Mem ??= new MemStats();
                            snap.Mem.Pct = value;
                            break;
                        case "SUSEDMEM":
                            snap.Mem ??= new MemStats();
                            snap.Mem.Used = (float)Math.Round(value / 1024, 1); // MB -> GB
                            break;
                        case "SGPU1UTIL":
                            snap.Gpu ??= new GpuStats();
                            snap.Gpu.Load = value;
                            break;
                        case "SNIC1DLRATE":
                            snap.Net ??= [new NetStats()];
                            snap.Net[0].Down = (float)Math.Round(value * 8 / 1000, 2); // KB/s -> Mbps
                            break;
                        case "SNIC1ULRATE":
                            snap.Net ??= [new NetStats()];
                            snap.Net[0].Up = (float)Math.Round(value * 8 / 1000, 2);
                            break;
                    }
                    break;
            }
        }
        return snap;
    }

    public void Dispose() => _watcher?.Dispose();
}
