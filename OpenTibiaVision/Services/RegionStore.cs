using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using OpenTibiaVision.Models;

namespace OpenTibiaVision.Services;

/// <summary>
/// Persists the region list as JSON at %APPDATA%\OpenTibiaVision\regions.json.
/// All failures are swallowed to a safe default (empty list / no-op) so a corrupt or
/// missing file never blocks startup.
/// </summary>
public static class RegionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenTibiaVision");

    public static string FilePath => Path.Combine(DirectoryPath, "regions.json");

    public static List<RegionConfig> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new List<RegionConfig>();

            string json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<RegionConfig>>(json, JsonOptions);
            return list ?? new List<RegionConfig>();
        }
        catch
        {
            // Corrupt/unreadable file: start clean rather than crash.
            return new List<RegionConfig>();
        }
    }

    public static void Save(IEnumerable<RegionConfig> regions)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string json = JsonSerializer.Serialize(regions, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Persistence is best-effort in M1; ignore write failures.
        }
    }
}
