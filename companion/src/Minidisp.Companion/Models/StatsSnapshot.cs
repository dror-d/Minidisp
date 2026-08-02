using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minidisp.Companion.Models;

/// <summary>The stats payload pushed to the device (docs/PROTOCOL.md).</summary>
public sealed class StatsSnapshot
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("host")] public string? Host { get; set; }
    [JsonPropertyName("uptime")] public long? Uptime { get; set; }
    [JsonPropertyName("cpu")] public CpuStats? Cpu { get; set; }
    [JsonPropertyName("mem")] public MemStats? Mem { get; set; }
    [JsonPropertyName("gpu")] public GpuStats? Gpu { get; set; }
    [JsonPropertyName("net")] public List<NetStats>? Net { get; set; }
    [JsonPropertyName("disk")] public List<DiskStats>? Disk { get; set; }

    /// <summary>
    /// User-defined values (from XML &lt;value id="..."&gt; elements) that themes
    /// can bind to by key — e.g. data published by another application.
    /// Values are double (numeric) or string.
    /// </summary>
    [JsonPropertyName("custom")] public Dictionary<string, object>? Custom { get; set; }

    /// <summary>Serializes as a single protocol line: {"stats":{...}}.</summary>
    public string ToProtocolLine()
    {
        var envelope = new Dictionary<string, StatsSnapshot> { ["stats"] = this };
        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }
}

public sealed class CpuStats
{
    [JsonPropertyName("load")] public float? Load { get; set; }
    [JsonPropertyName("temp")] public float? Temp { get; set; }
    [JsonPropertyName("freq")] public float? Freq { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("cores")] public List<float>? Cores { get; set; }
}

public sealed class MemStats
{
    [JsonPropertyName("pct")] public float? Pct { get; set; }
    [JsonPropertyName("used")] public float? Used { get; set; }
    [JsonPropertyName("total")] public float? Total { get; set; }
}

public sealed class GpuStats
{
    [JsonPropertyName("load")] public float? Load { get; set; }
    [JsonPropertyName("temp")] public float? Temp { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
}

public sealed class NetStats
{
    [JsonPropertyName("if")] public string? Interface { get; set; }
    [JsonPropertyName("ip")] public string? Ip { get; set; }
    [JsonPropertyName("up")] public float? Up { get; set; }
    [JsonPropertyName("down")] public float? Down { get; set; }
}

public sealed class DiskStats
{
    [JsonPropertyName("n")] public string? Name { get; set; }
    [JsonPropertyName("pct")] public float? Pct { get; set; }
    [JsonPropertyName("free")] public float? Free { get; set; }
}
