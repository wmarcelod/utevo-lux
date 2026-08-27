using System;
using System.Collections.Generic;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// A named ordered list of waypoints (max 100). Persisted by <see cref="JsonRouteStore"/> and
/// shareable via <see cref="ShareCodeService"/> (TVR- codes). Ported faithfully from the
/// original TibiaVision.
/// </summary>
public class MapRoute
{
    public const int MaxPoints = 100;

    public const int MaxNameLength = 40;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public List<RoutePoint> Points { get; set; } = new List<RoutePoint>();

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
