using System;
using System.Collections.Generic;
using System.Linq;

namespace UtevoLux.Features.Map;

/// <summary>
/// Buckets spawn positions into a uniform world-space grid (cell size in world px) so a dense
/// spawn list collapses into O(n) clusters for rendering. Each cluster carries its member
/// positions and averaged center. Ported faithfully from the original TibiaVision.
/// </summary>
public static class SpawnClusterer
{
    public sealed class Cluster
    {
        public double CenterX { get; init; }

        public double CenterY { get; init; }

        public IReadOnlyList<NpcPosition> Members { get; init; } = System.Array.Empty<NpcPosition>();

        public int Count => Members.Count;

        public bool AnyOnFloor(int z)
        {
            return Members.Any((NpcPosition m) => m.Z == z);
        }

        public (int MinX, int MinY, int MaxX, int MaxY) Bounds()
        {
            return (MinX: Members.Min((NpcPosition m) => m.X), MinY: Members.Min((NpcPosition m) => m.Y), MaxX: Members.Max((NpcPosition m) => m.X), MaxY: Members.Max((NpcPosition m) => m.Y));
        }
    }

    public static List<Cluster> Build(IReadOnlyList<NpcPosition> positions, double cellWorldSize)
    {
        if (positions == null || positions.Count == 0)
        {
            return new List<Cluster>();
        }
        if (cellWorldSize <= 0.0)
        {
            cellWorldSize = 1.0;
        }
        Dictionary<(int, int), List<NpcPosition>> dictionary = new Dictionary<(int, int), List<NpcPosition>>();
        foreach (NpcPosition position in positions)
        {
            (int, int) key = ((int)Math.Floor((double)position.X / cellWorldSize), (int)Math.Floor((double)position.Y / cellWorldSize));
            if (!dictionary.TryGetValue(key, out var value))
            {
                value = (dictionary[key] = new List<NpcPosition>());
            }
            value.Add(position);
        }
        List<Cluster> list2 = new List<Cluster>(dictionary.Count);
        foreach (List<NpcPosition> value2 in dictionary.Values)
        {
            list2.Add(new Cluster
            {
                CenterX = ((IEnumerable<NpcPosition>)value2).Average((Func<NpcPosition, double>)((NpcPosition m) => m.X)),
                CenterY = ((IEnumerable<NpcPosition>)value2).Average((Func<NpcPosition, double>)((NpcPosition m) => m.Y)),
                Members = value2
            });
        }
        return list2;
    }
}
