using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Indexes the <c>Minimap_Color_x_y_z.png</c> tile files (256x256 world px each) into per-floor
/// buckets and computes the overall <see cref="MapBounds"/>. Tile lookup resolves against
/// <c>Resources/minimap</c> next to the exe (build-copied Content). Ported faithfully from the
/// original TibiaVision.
/// </summary>
public class MapTileIndex
{
    public readonly struct TileRef
    {
        public int WorldX { get; }

        public int WorldY { get; }

        public string FilePath { get; }

        public TileRef(int worldX, int worldY, string filePath)
        {
            WorldX = worldX;
            WorldY = worldY;
            FilePath = filePath;
        }
    }

    public const int TileSize = 256;

    public const int FloorCount = 16;

    public const int GroundFloor = 7;

    private static readonly Regex TileNameRegex = new Regex("^Minimap_Color_(\\d+)_(\\d+)_(\\d+)\\.png$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<int, List<TileRef>> _tilesByFloor;

    public MapBounds Bounds { get; }

    public bool HasTiles => _tilesByFloor.Count > 0;

    public IReadOnlyList<TileRef> GetTilesForFloor(int z)
    {
        if (!_tilesByFloor.TryGetValue(z, out var value))
        {
            return Array.Empty<TileRef>();
        }
        return value;
    }

    private MapTileIndex(MapBounds bounds, Dictionary<int, List<TileRef>> tilesByFloor)
    {
        Bounds = bounds;
        _tilesByFloor = tilesByFloor;
    }

    public static bool TryParseTileName(string fileName, out int worldX, out int worldY, out int z)
    {
        worldX = (worldY = (z = 0));
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }
        Match match = TileNameRegex.Match(fileName);
        if (!match.Success)
        {
            return false;
        }
        if (!int.TryParse(match.Groups[1].Value, out worldX))
        {
            return false;
        }
        if (!int.TryParse(match.Groups[2].Value, out worldY))
        {
            return false;
        }
        if (!int.TryParse(match.Groups[3].Value, out z))
        {
            return false;
        }
        if (z >= 0)
        {
            return z < 16;
        }
        return false;
    }

    public static MapTileIndex Load(string directory)
    {
        IEnumerable<string> filePaths;
        try
        {
            IEnumerable<string> enumerable;
            if (!Directory.Exists(directory))
            {
                enumerable = Enumerable.Empty<string>();
            }
            else
            {
                IEnumerable<string> enumerable2 = Directory.EnumerateFiles(directory, "Minimap_Color_*.png");
                enumerable = enumerable2;
            }
            filePaths = enumerable;
        }
        catch
        {
            filePaths = Enumerable.Empty<string>();
        }
        return Build(filePaths);
    }

    public static MapTileIndex Build(IEnumerable<string> filePaths)
    {
        Dictionary<int, List<TileRef>> dictionary = new Dictionary<int, List<TileRef>>();
        int num = int.MaxValue;
        int num2 = int.MaxValue;
        int num3 = int.MinValue;
        int num4 = int.MinValue;
        foreach (string filePath in filePaths)
        {
            if (TryParseTileName(Path.GetFileName(filePath), out var worldX, out var worldY, out var z))
            {
                if (!dictionary.TryGetValue(z, out var value))
                {
                    value = (dictionary[z] = new List<TileRef>());
                }
                value.Add(new TileRef(worldX, worldY, filePath));
                if (worldX < num)
                {
                    num = worldX;
                }
                if (worldY < num2)
                {
                    num2 = worldY;
                }
                if (worldX > num3)
                {
                    num3 = worldX;
                }
                if (worldY > num4)
                {
                    num4 = worldY;
                }
            }
        }
        return new MapTileIndex((dictionary.Count == 0) ? new MapBounds(0, 0, 0, 0) : new MapBounds(num, num2, num3 + 256, num4 + 256), dictionary);
    }

    public static string ResolveTileDirectory()
    {
        string[] array = new string[3]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "minimap"),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "minimap"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Resources", "minimap")
        };
        string[] array2 = array;
        foreach (string text in array2)
        {
            try
            {
                if (Directory.Exists(text) && Directory.EnumerateFiles(text, "Minimap_Color_*.png").Any())
                {
                    return text;
                }
            }
            catch
            {
            }
        }
        return array[0];
    }
}
