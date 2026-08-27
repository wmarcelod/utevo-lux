namespace UtevoLux.Features.Map;

/// <summary>
/// A single world position (x, y, floor z) with an optional spawn timer in seconds. Ported
/// faithfully from the original TibiaVision. z is a Tibia floor index (0..15, 7 == ground).
/// </summary>
public readonly struct NpcPosition
{
    public int X { get; }

    public int Y { get; }

    public int Z { get; }

    public int SpawnTimeSeconds { get; }

    public NpcPosition(int x, int y, int z)
        : this(x, y, z, 0)
    {
    }

    public NpcPosition(int x, int y, int z, int spawnTimeSeconds)
    {
        X = x;
        Y = y;
        Z = z;
        SpawnTimeSeconds = spawnTimeSeconds;
    }
}
