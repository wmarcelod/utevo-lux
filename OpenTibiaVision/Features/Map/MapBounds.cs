namespace OpenTibiaVision.Features.Map;

/// <summary>
/// World-pixel bounds of the stitched minimap, derived from the tile index. Tiles are 256x256
/// world pixels; bounds are [Min, MaxExclusive) so <see cref="Width"/>/<see cref="Height"/> give
/// the exact size of the stitched floor bitmap. Ported faithfully from the original TibiaVision.
/// </summary>
public readonly struct MapBounds
{
    public int MinX { get; }

    public int MinY { get; }

    public int MaxXExclusive { get; }

    public int MaxYExclusive { get; }

    public int Width => MaxXExclusive - MinX;

    public int Height => MaxYExclusive - MinY;

    public MapBounds(int minX, int minY, int maxXExclusive, int maxYExclusive)
    {
        MinX = minX;
        MinY = minY;
        MaxXExclusive = maxXExclusive;
        MaxYExclusive = maxYExclusive;
    }

    public bool Contains(int worldX, int worldY)
    {
        if (worldX >= MinX && worldX < MaxXExclusive && worldY >= MinY)
        {
            return worldY < MaxYExclusive;
        }
        return false;
    }

    public (int px, int py) WorldToPixel(int worldX, int worldY)
    {
        return (px: worldX - MinX, py: worldY - MinY);
    }

    public (int x, int y) PixelToWorld(int pixelX, int pixelY)
    {
        return (x: MinX + pixelX, y: MinY + pixelY);
    }
}
