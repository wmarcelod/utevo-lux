using System.Collections.Generic;

namespace UtevoLux.Features.Map;

/// <summary>
/// A named searchable map entity (NPC, rare creature, or monster-spawn group) with one or more
/// world positions. Defaults keep the type
/// warning-free under the fork's Nullable-enable; every producer sets Name/Positions explicitly.
/// </summary>
public class NpcEntry
{
    public string Name { get; init; } = "";

    public string Location { get; init; } = "";

    public IReadOnlyList<NpcPosition> Positions { get; init; } = System.Array.Empty<NpcPosition>();

    public bool IsSpawnData { get; init; }

    public NpcPosition Primary => Positions[0];
}
