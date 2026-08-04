using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minidisp.Companion.Editor;

/// <summary>
/// Typed model of a theme.json (docs/THEMES.md). Coordinates are per-mille
/// (0-1000) of the screen. Optional fields stay null so saving preserves the
/// firmware's defaulting behavior.
/// </summary>
public sealed class ThemeDocument
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Palette defaults, mirroring theme_engine.cpp.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultColors =
        new Dictionary<string, string>
        {
            ["bg"] = "#101418",
            ["fg"] = "#E6E6E6",
            ["accent"] = "#00C8FF",
            ["accent2"] = "#7CFC00",
            ["muted"] = "#5A6570",
            ["warn"] = "#FF5040",
        };

    [JsonPropertyName("name")] public string Name { get; set; } = "Untitled";
    [JsonPropertyName("author")] public string? Author { get; set; }
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    /// <summary>"portrait" rotates the device panel; null/"landscape" is default.</summary>
    [JsonPropertyName("orientation")] public string? Orientation { get; set; }
    [JsonPropertyName("colors")] public Dictionary<string, string> Colors { get; set; } = new();
    [JsonPropertyName("warnAbove")] public Dictionary<string, float>? WarnAbove { get; set; }
    [JsonPropertyName("pages")] public List<ThemePage> Pages { get; set; } = [];

    /// <summary>Palette lookup with firmware defaults.</summary>
    public string PaletteColor(string key) =>
        Colors.TryGetValue(key, out var v) ? v
        : DefaultColors.TryGetValue(key, out var d) ? d : "#FF00FF";

    public static ThemeDocument Load(string path) =>
        JsonSerializer.Deserialize<ThemeDocument>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"empty theme file: {path}");

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ThemeDocument FromJson(string json) =>
        JsonSerializer.Deserialize<ThemeDocument>(json, JsonOptions)
        ?? throw new InvalidDataException("empty theme json");

    public ThemeDocument Clone() => FromJson(ToJson());

    public static ThemeDocument NewDefault()
    {
        var doc = new ThemeDocument
        {
            Name = "My Theme",
            Author = Environment.UserName,
            Colors = new Dictionary<string, string>(DefaultColors),
            WarnAbove = new Dictionary<string, float>
            {
                ["cpu.load"] = 95,
                ["cpu.temp"] = 85,
                ["mem.pct"] = 90,
            },
        };
        doc.Pages.Add(new ThemePage
        {
            Name = "Page 1",
            Widgets =
            {
                new ThemeWidget { Type = "text", Text = "CPU", X = 40, Y = 60, Size = "sm", Color = "muted" },
                new ThemeWidget { Type = "text", Bind = "cpu.load", Fmt = "{v:.0f}%", X = 40, Y = 140, Size = "xl" },
                new ThemeWidget { Type = "bar", Bind = "cpu.load", X = 40, Y = 320, W = 920, H = 50 },
            },
        });
        return doc;
    }
}

public sealed class ThemePage
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("widgets")] public List<ThemeWidget> Widgets { get; set; } = [];
}

public sealed class ThemeWidget
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("w")] public int? W { get; set; }
    [JsonPropertyName("h")] public int? H { get; set; }
    [JsonPropertyName("r")] public int? R { get; set; }
    [JsonPropertyName("anchor")] public string? Anchor { get; set; }
    [JsonPropertyName("bind")] public string? Bind { get; set; }
    [JsonPropertyName("fmt")] public string? Fmt { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("size")] public string? Size { get; set; }
    [JsonPropertyName("color")] public string? Color { get; set; }
    [JsonPropertyName("bg")] public string? Bg { get; set; }
    [JsonPropertyName("min")] public int? Min { get; set; }
    [JsonPropertyName("max")] public int? Max { get; set; }
    [JsonPropertyName("thickness")] public int? Thickness { get; set; }
    [JsonPropertyName("points")] public int? Points { get; set; }
    [JsonPropertyName("autoscale")] public bool? Autoscale { get; set; }
    [JsonPropertyName("label")] public bool? Label { get; set; }
    [JsonPropertyName("src")] public string? Src { get; set; }
    [JsonPropertyName("radius")] public int? Radius { get; set; }

    [JsonIgnore] public bool HasSize => Type is "bar" or "chart" or "rect";
}
