using Minidisp.Companion.Models;

namespace Minidisp.Companion.Editor;

/// <summary>Value source for preview rendering, keyed by theme bind paths.</summary>
public interface IStatsProvider
{
    bool TryGetNumber(string path, out float value);
    bool TryGetText(string path, out string text);
}

/// <summary>Deterministic sample values matching docs/PROTOCOL.md bind paths.</summary>
public sealed class SampleStats : IStatsProvider
{
    private static readonly Dictionary<string, float> Numbers = new()
    {
        ["cpu.load"] = 42.5f, ["cpu.temp"] = 56, ["cpu.freq"] = 3.8f,
        ["cpu.core0"] = 35.1f, ["cpu.core1"] = 46.2f, ["cpu.core2"] = 55.3f,
        ["cpu.core3"] = 44.8f, ["cpu.core4"] = 22.0f, ["cpu.core5"] = 61.7f,
        ["cpu.core6"] = 38.4f, ["cpu.core7"] = 50.9f,
        ["mem.pct"] = 61.2f, ["mem.used"] = 9.8f, ["mem.total"] = 16,
        ["gpu.load"] = 17, ["gpu.temp"] = 48,
        ["net.up"] = 1.2f, ["net.down"] = 34.5f,
        ["net1.up"] = 0.3f, ["net1.down"] = 2.1f,
        ["disk.pct"] = 75, ["disk.free"] = 250.1f,
        ["disk1.pct"] = 42, ["disk1.free"] = 512.3f,
        ["uptime"] = 123456,
    };

    private static readonly Dictionary<string, string> Texts = new()
    {
        ["host"] = "MYPC",
        ["cpu.name"] = "Ryzen 7 5800X",
        ["gpu.name"] = "RTX 3070",
        ["net.ip"] = "192.168.1.10", ["net.if"] = "Ethernet",
        ["net1.ip"] = "10.0.0.5", ["net1.if"] = "Wi-Fi",
        ["disk.n"] = "C:", ["disk1.n"] = "D:",
        ["uptime"] = "1d 10:17",
    };

    public bool TryGetNumber(string path, out float value) =>
        Numbers.TryGetValue(path, out value);

    public bool TryGetText(string path, out string text)
    {
        if (Texts.TryGetValue(path, out text!)) return true;
        if (Numbers.TryGetValue(path, out var v))
        {
            text = v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        text = "";
        return false;
    }
}

/// <summary>
/// Adapts a live StatsSnapshot to bind-path lookups, mirroring the firmware's
/// stats_model.cpp resolution rules (net/netN, disk/diskN, cpu.coreN, ...).
/// </summary>
public sealed class SnapshotStats(StatsSnapshot snap) : IStatsProvider
{
    public bool TryGetNumber(string path, out float value)
    {
        value = 0;
        float? v = path switch
        {
            "cpu.load" => snap.Cpu?.Load,
            "cpu.temp" => snap.Cpu?.Temp,
            "cpu.freq" => snap.Cpu?.Freq,
            "mem.pct" => snap.Mem?.Pct,
            "mem.used" => snap.Mem?.Used,
            "mem.total" => snap.Mem?.Total,
            "gpu.load" => snap.Gpu?.Load,
            "gpu.temp" => snap.Gpu?.Temp,
            "uptime" => snap.Uptime,
            _ => Indexed(path),
        };
        if (v is null) return false;
        value = v.Value;
        return true;
    }

    private float? Indexed(string path)
    {
        if (path.StartsWith("cpu.core", StringComparison.Ordinal) &&
            int.TryParse(path.AsSpan("cpu.core".Length), out var core))
            return core >= 0 && core < (snap.Cpu?.Cores?.Count ?? 0)
                ? snap.Cpu!.Cores![core] : null;

        var (group, index, field) = Split(path);
        return group switch
        {
            "net" when Net(index) is { } n => field switch
            {
                "up" => n.Up, "down" => n.Down, _ => null,
            },
            "disk" when Disk(index) is { } d => field switch
            {
                "pct" => d.Pct, "free" => d.Free, _ => null,
            },
            _ => null,
        };
    }

    public bool TryGetText(string path, out string text)
    {
        text = "";
        var (group, index, field) = Split(path);
        string? s = (group, field) switch
        {
            ("host", _) => snap.Host,
            ("cpu", "name") => snap.Cpu?.Name,
            ("gpu", "name") => snap.Gpu?.Name,
            ("net", "ip") => Net(index)?.Ip,
            ("net", "if") => Net(index)?.Interface,
            ("disk", "n") => Disk(index)?.Name,
            ("uptime", _) => snap.Uptime is long up
                ? $"{up / 86400}d {up / 3600 % 24:00}:{up / 60 % 60:00}" : null,
            _ => null,
        };
        if (s is null)
        {
            if (!TryGetNumber(path, out var v)) return false;
            s = v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
        text = s;
        return true;
    }

    private NetStats? Net(int i) =>
        snap.Net is { } list && i < list.Count ? list[i] : null;

    private DiskStats? Disk(int i) =>
        snap.Disk is { } list && i < list.Count ? list[i] : null;

    /// <summary>"net1.ip" → ("net", 1, "ip"); "host" → ("host", 0, "").</summary>
    private static (string group, int index, string field) Split(string path)
    {
        var dot = path.IndexOf('.');
        var group = dot < 0 ? path : path[..dot];
        var field = dot < 0 ? "" : path[(dot + 1)..];
        int index = 0;
        if (group.Length > 0 && char.IsDigit(group[^1]))
        {
            index = group[^1] - '0';
            group = group[..^1];
        }
        return (group, index, field);
    }
}

/// <summary>Rolling per-bind value history for chart widgets.</summary>
public sealed class ChartHistory
{
    private readonly Dictionary<string, List<float>> _series = new();

    public void Add(string bind, float value)
    {
        if (!_series.TryGetValue(bind, out var list))
            _series[bind] = list = [];
        list.Add(value);
        if (list.Count > 240) list.RemoveRange(0, list.Count - 240);
    }

    /// <summary>Last `points` values; synthesizes a wave when empty (sample mode).</summary>
    public float[] Get(string bind, int points, float min, float max)
    {
        if (_series.TryGetValue(bind, out var list) && list.Count > 1)
        {
            var take = Math.Min(points, list.Count);
            return list.Skip(list.Count - take).ToArray();
        }
        // Deterministic pseudo-wave so sample previews look alive.
        var phase = Math.Abs(bind.GetHashCode() % 17);
        var mid = min + (max - min) * 0.45f;
        var amp = (max - min) * 0.3f;
        var result = new float[points];
        for (int i = 0; i < points; i++)
            result[i] = mid + amp * (float)Math.Sin((i + phase) / 5.0)
                            + amp * 0.4f * (float)Math.Sin((i + phase * 3) / 2.1);
        return result;
    }

    public void Clear() => _series.Clear();
}
