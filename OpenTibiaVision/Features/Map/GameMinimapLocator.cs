using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Locates the installed official Tibia client's data on this machine so the map can use the
/// PLAYER'S own minimap (their explored map, kept current by the game) instead of the bundled
/// snapshot. The client stores per-tile minimap PNGs (<c>Minimap_Color_{x}_{y}_{z}.png</c>) plus
/// <c>Minimap_WaypointCost_*.png</c> and <c>minimapmarkers.bin</c> under
/// <c>%LOCALAPPDATA%\Tibia\packages\Tibia\minimap</c> (historically also under %APPDATA%).
///
/// Everything here is best-effort and never throws: if no install is found the caller falls back
/// to the bundled tiles.
/// </summary>
public static class GameMinimapLocator
{
    /// <summary>
    /// The player's live minimap tile directory (first candidate that actually contains
    /// Minimap_Color tiles), or null when no Tibia install is found.
    /// </summary>
    public static string? FindPlayerMinimapDir()
    {
        foreach (string dir in MinimapCandidates())
        {
            try
            {
                if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "Minimap_Color_*.png").Any())
                    return dir;
            }
            catch
            {
                // unreadable candidate — skip it
            }
        }
        return null;
    }

    /// <summary>The <c>...\Tibia\packages\Tibia</c> package root (has assets/, minimap/, ...), or null.</summary>
    public static string? FindGamePackageDir()
    {
        foreach (string dir in PackageCandidates())
        {
            try
            {
                if (Directory.Exists(dir))
                    return dir;
            }
            catch
            {
            }
        }
        return null;
    }

    private static IEnumerable<string> PackageCandidates()
    {
        string local = SafeFolder(Environment.SpecialFolder.LocalApplicationData);
        string roaming = SafeFolder(Environment.SpecialFolder.ApplicationData);
        if (local.Length > 0)
            yield return Path.Combine(local, "Tibia", "packages", "Tibia");
        if (roaming.Length > 0)
            yield return Path.Combine(roaming, "Tibia", "packages", "Tibia");
    }

    private static IEnumerable<string> MinimapCandidates()
    {
        foreach (string pkg in PackageCandidates())
            yield return Path.Combine(pkg, "minimap");
    }

    private static string SafeFolder(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch
        {
            return "";
        }
    }
}
