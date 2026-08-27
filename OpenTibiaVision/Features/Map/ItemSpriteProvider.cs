using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Resolves an item name -> slug -> <c>Resources/items/{slug}.{gif|png}</c>, loading each with
/// OnLoad + Freeze so it is safe to reuse across threads/renders. Results are memoized (including
/// negative/null misses) so a missing icon is looked up on disk only once.
///
/// The item bank under Resources/items was extracted from the CURRENT official client (see the
/// tibia-extractor pipeline): most items are static single-frame sprites saved as .png, animated
/// ones as .gif. Keyed by the same underscore slug the creature/NPC sprites use
/// (<see cref="SpriteProvider.Slug"/>), so a loot name maps straight to an icon.
///
/// PLURAL FALLBACK: TibiaData loot names are usually plural ("gold coins", "broken helmets") while
/// item names are singular ("gold coin", "broken helmet"). When the exact slug misses we retry a
/// few naive singular forms before giving up, which recovers the vast majority of loot rows.
/// </summary>
public static class ItemSpriteProvider
{
    private static readonly Dictionary<string, ImageSource?> Cache =
        new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new object();

    private static string? _dir;

    private static readonly string[] Extensions = { ".gif", ".png" };

    /// <summary>Item name -> icon, or null when no matching file exists (miss is memoized).</summary>
    public static ImageSource? GetItem(string name)
    {
        string slug = SpriteProvider.Slug(name);
        if (slug.Length == 0)
            return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(slug, out ImageSource? cached))
                return cached;

            ImageSource? resolved = null;
            try
            {
                string dir = ResolveDirectory();
                foreach (string candidate in SlugCandidates(slug))
                {
                    foreach (string ext in Extensions)
                    {
                        string path = Path.Combine(dir, candidate + ext);
                        if (File.Exists(path))
                        {
                            var bmp = new BitmapImage();
                            bmp.BeginInit();
                            bmp.CacheOption = BitmapCacheOption.OnLoad;
                            bmp.UriSource = new Uri(path, UriKind.Absolute);
                            bmp.EndInit();
                            bmp.Freeze();
                            resolved = bmp;
                            break;
                        }
                    }
                    if (resolved != null)
                        break;
                }
            }
            catch
            {
                resolved = null;
            }

            Cache[slug] = resolved;
            return resolved;
        }
    }

    /// <summary>True if <paramref name="name"/> resolves to an icon (used to filter loot rows).</summary>
    public static bool Has(string name) => GetItem(name) != null;

    /// <summary>
    /// The exact slug first, then a few naive singular forms so plural loot names ("...s", "...es",
    /// "...ies") still match a singular item file. Order matters: the exact form must win.
    /// </summary>
    private static IEnumerable<string> SlugCandidates(string slug)
    {
        yield return slug;

        // "berries" -> "berry"
        if (slug.EndsWith("ies", StringComparison.Ordinal) && slug.Length > 3)
            yield return slug.Substring(0, slug.Length - 3) + "y";

        // "boxes"/"leeches" -> "box"/"leech"
        if (slug.EndsWith("es", StringComparison.Ordinal) && slug.Length > 2)
            yield return slug.Substring(0, slug.Length - 2);

        // "helmets"/"coins" -> "helmet"/"coin"  (the common case)
        if (slug.EndsWith("s", StringComparison.Ordinal) && slug.Length > 1)
            yield return slug.Substring(0, slug.Length - 1);
    }

    private static string ResolveDirectory()
    {
        if (_dir != null)
            return _dir;

        string[] candidates =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "items"),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "items")
        };
        foreach (string path in candidates)
        {
            try
            {
                if (Directory.Exists(path))
                    return _dir = path;
            }
            catch
            {
            }
        }
        return _dir = candidates[0];
    }
}
