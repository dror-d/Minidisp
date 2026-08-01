using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minidisp.Companion;

public enum SourceMode
{
    Live,
    XmlFile,
}

public sealed class Settings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Minidisp", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SourceMode Mode { get; set; } = SourceMode.Live;
    public string? XmlPath { get; set; }
    public double UpdateHz { get; set; } = 2.0;

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(
                    File.ReadAllText(FilePath), Options) ?? new Settings();
        }
        catch (Exception)
        {
            // fall through to defaults
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }
}
