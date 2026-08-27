using System;
using System.IO;
using System.Text.Json;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Loads/saves <see cref="MapSettings"/> as JSON at <c>%APPDATA%\OpenTibiaVision\map_settings.json</c>
/// (was TibiaVision in the original). WindowScale is clamped to [0.6, 1.0] on load. Ported
/// faithfully from the original TibiaVision.
/// </summary>
public static class MapSettingsService
{
    private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenTibiaVision", "map_settings.json");

    public static MapSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                MapSettings? mapSettings = JsonSerializer.Deserialize<MapSettings>(File.ReadAllText(FilePath));
                if (mapSettings != null)
                {
                    mapSettings.WindowScale = ((mapSettings.WindowScale <= 0.0) ? 1.0 : Math.Max(0.6, Math.Min(mapSettings.WindowScale, 1.0)));
                    return mapSettings;
                }
            }
        }
        catch
        {
        }
        return new MapSettings();
    }

    public static void Save(MapSettings settings)
    {
        try
        {
            string? directoryName = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
            string contents = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(FilePath, contents);
        }
        catch
        {
        }
    }
}
