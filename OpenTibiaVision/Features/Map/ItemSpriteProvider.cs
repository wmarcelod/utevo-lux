using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Resolves an item name -> slug -> <c>Resources/items/{slug}.{gif|png}</c>. The item bank was
/// extracted from the CURRENT official client: most items are static single-frame .png, animated
/// ones are .gif. Keyed by the same underscore slug as the creature/NPC sprites
/// (<see cref="SpriteProvider.Slug"/>), so a loot name maps straight to an icon.
///
/// PLURAL / WORD MATCHING: TibiaData loot names are usually plural ("gold coins") while item names
/// are singular ("gold coin"), and the plural word is not always the last one ("veins of ore" ->
/// "vein of ore") and is sometimes irregular ("wimp teeth chain" -> "wimp tooth chain"). We try the
/// exact slug first, then singularized variants (last word, first word, all words, with an irregular
/// table) so the vast majority of loot rows resolve to an icon instead of falling back to text.
/// </summary>
public static class ItemSpriteProvider
{
    private static readonly Dictionary<string, ImageSource?> Cache =
        new Dictionary<string, ImageSource?>(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new object();

    private static string? _dir;

    private static readonly string[] Extensions = { ".gif", ".png" };

    // Common irregular plurals that show up in Tibia loot names.
    private static readonly Dictionary<string, string> Irregular = new(StringComparer.Ordinal)
    {
        ["teeth"] = "tooth", ["feet"] = "foot", ["wolves"] = "wolf", ["lives"] = "life",
        ["leaves"] = "leaf", ["knives"] = "knife", ["elves"] = "elf", ["loaves"] = "loaf",
        ["halves"] = "half", ["thieves"] = "thief", ["geese"] = "goose", ["mice"] = "mouse",
        ["men"] = "man", ["children"] = "child", ["scarves"] = "scarf",
    };

    /// <summary>Item name -> icon (first frame), or null when no matching file exists (memoized).</summary>
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
                string? path = ResolvePath(slug);
                if (path != null)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(path, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    resolved = bmp;
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

    /// <summary>Resolved absolute path of the item's icon file, or null — for GIF animation.</summary>
    public static string? GetItemPath(string name)
    {
        string slug = SpriteProvider.Slug(name);
        return slug.Length == 0 ? null : ResolvePath(slug);
    }

    /// <summary>True if <paramref name="name"/> resolves to an icon (used to filter loot rows).</summary>
    public static bool Has(string name) => GetItemPath(name) != null;

    private static string? ResolvePath(string slug)
    {
        string dir = ResolveDirectory();
        foreach (string candidate in SlugCandidates(slug))
        {
            foreach (string ext in Extensions)
            {
                string path = Path.Combine(dir, candidate + ext);
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    /// <summary>
    /// The exact slug, then singular variants — trying every candidate form of the last word, the
    /// first word, and an all-words primary form. Each word can singularize more than one way
    /// ("oranges" -> "orange" via drop-s, "boxes" -> "box" via drop-es), so all forms are tried and
    /// the first that maps to an existing file wins.
    /// </summary>
    private static IEnumerable<string> SlugCandidates(string slug)
    {
        var result = new List<string>();
        void Add(string s)
        {
            if (!result.Contains(s))
                result.Add(s);
        }

        Add(slug);
        string[] words = slug.Split('_');
        if (words.Length == 1)
        {
            foreach (string f in SingularForms(words[0]))
                Add(f);
            return result;
        }

        // every singular form of the last word ("broken helmets" -> "broken helmet")
        foreach (string f in SingularForms(words[^1]))
        {
            string[] w = (string[])words.Clone();
            w[^1] = f;
            Add(string.Join('_', w));
        }
        // every singular form of the first word ("veins of ore" -> "vein of ore")
        foreach (string f in SingularForms(words[0]))
        {
            string[] w = (string[])words.Clone();
            w[0] = f;
            Add(string.Join('_', w));
        }
        // all words -> their primary singular form ("wimp teeth chain" -> "wimp tooth chain")
        Add(string.Join('_', words.Select(w => SingularForms(w).First())));
        return result;
    }

    /// <summary>Ordered candidate singular forms of one word; primary (drop-s / irregular) first.</summary>
    private static IEnumerable<string> SingularForms(string word)
    {
        if (word.Length < 2)
        {
            yield return word;
            yield break;
        }
        if (Irregular.TryGetValue(word, out string? irr))
            yield return irr;
        if (word.EndsWith("ies", StringComparison.Ordinal) && word.Length > 3)
            yield return word.Substring(0, word.Length - 3) + "y";
        if (word.EndsWith("s", StringComparison.Ordinal))
            yield return word.Substring(0, word.Length - 1);            // drop s: oranges->orange, coins->coin
        if (word.EndsWith("es", StringComparison.Ordinal) && word.Length > 2)
            yield return word.Substring(0, word.Length - 2);            // drop es: boxes->box, torches->torch
        yield return word;                                             // unchanged (not a plural)
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
