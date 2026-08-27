using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Resolves creature/NPC name -> slug -> <c>Resources/{creatures|npcs}/{slug}.gif</c>, loading
/// each with OnLoad + Freeze so it is safe to reuse across threads/renders. Results are memoized
/// (including negative/null misses) so a missing sprite is looked up on disk only once. Ported
/// faithfully from the original TibiaVision.
/// </summary>
public static class SpriteProvider
{
    private static readonly Dictionary<string, ImageSource?> Cache = new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> ResolvedDirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new object();

    public static string Slug(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }
        return Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
    }

    public static ImageSource? GetCreature(string name)
    {
        return Get("creatures", name);
    }

    public static ImageSource? GetNpc(string name)
    {
        return Get("npcs", name);
    }

    private static ImageSource? Get(string folder, string name)
    {
        string text = Slug(name);
        if (text.Length == 0)
        {
            return null;
        }
        string key = folder + "/" + text;
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var value))
            {
                return value;
            }
            ImageSource? imageSource = null;
            try
            {
                string text2 = Path.Combine(ResolveDirectory(folder), text + ".gif");
                if (File.Exists(text2))
                {
                    BitmapImage bitmapImage = new BitmapImage();
                    bitmapImage.BeginInit();
                    bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                    bitmapImage.UriSource = new Uri(text2, UriKind.Absolute);
                    bitmapImage.EndInit();
                    bitmapImage.Freeze();
                    imageSource = bitmapImage;
                }
            }
            catch
            {
                imageSource = null;
            }
            Cache[key] = imageSource;
            return imageSource;
        }
    }

    private static string ResolveDirectory(string folder)
    {
        if (ResolvedDirs.TryGetValue(folder, out var value))
        {
            return value;
        }
        string[] array = new string[2]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", folder),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", folder)
        };
        string[] array2 = array;
        foreach (string text in array2)
        {
            try
            {
                if (Directory.Exists(text))
                {
                    return ResolvedDirs[folder] = text;
                }
            }
            catch
            {
            }
        }
        return ResolvedDirs[folder] = array[0];
    }
}
