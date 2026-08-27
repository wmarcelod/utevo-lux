namespace UtevoLux.Features.Map;

/// <summary>
/// One waypoint of a <see cref="MapRoute"/> (world x, y, floor z). Ported faithfully from the
/// original TibiaVision.
/// </summary>
public class RoutePoint
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public RoutePoint()
    {
    }

    public RoutePoint(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}
