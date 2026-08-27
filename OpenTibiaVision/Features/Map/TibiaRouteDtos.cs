using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OpenTibiaVision.Features.Map;

// =====================================================================================
// DTOs for the tibiaroute.com "delivery-task-spots" endpoint and our local cache sidecar.
//
// SOURCE: GET https://tibiaroute.com/api/delivery-task-spots -> application/json, an ARRAY
// of monster entries (~918 as of writing, ~6.4 MB). tibiaroute.com is a THIRD-PARTY site;
// this shape is NOT a contract and may change or disappear without notice. Everything that
// consumes these DTOs (TibiaRouteSpawnProvider) is written to degrade gracefully — an
// unexpected shape simply yields fewer/zero entries and the map falls back to its bundled
// monster_spawns.dat. We deliberately model ONLY the handful of fields the map uses
// (monster.name / monster.slug, spots[].locationName, spots[].spawns[].x/y/z); every other
// field in the payload is ignored by System.Text.Json.
//
// Coordinates are Tibia WORLD coords (x~32000, y~31000-32800, z = floor 0..15) — the SAME
// system MapBounds.WorldToPixel expects, so spawns drop straight onto the minimap.
// =====================================================================================

/// <summary>One monster entry in the tibiaroute delivery-task-spots array.</summary>
internal sealed class TibiaRouteMonsterEntry
{
    [JsonPropertyName("monster")]
    public TibiaRouteMonster? Monster { get; set; }

    [JsonPropertyName("spots")]
    public List<TibiaRouteSpot>? Spots { get; set; }
}

/// <summary>The creature itself (only name/slug are used).</summary>
internal sealed class TibiaRouteMonster
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

/// <summary>A named spawn area for a monster; carries the individual spawn tiles.</summary>
internal sealed class TibiaRouteSpot
{
    [JsonPropertyName("locationName")]
    public string? LocationName { get; set; }

    [JsonPropertyName("spawns")]
    public List<TibiaRouteSpawn>? Spawns { get; set; }
}

/// <summary>A single spawn tile in Tibia world coordinates.</summary>
internal sealed class TibiaRouteSpawn
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }
}

/// <summary>
/// Small sidecar written next to the cached payload (tibiaroute_spawns.meta.json) recording
/// when we last fetched, from where, and how many entries parsed. Kept separate from the big
/// payload so the fetch can stream the ~6.4 MB body straight to disk without buffering it in
/// memory just to inject a timestamp field.
/// </summary>
internal sealed class TibiaRouteCacheMeta
{
    [JsonPropertyName("fetchedAtUtc")]
    public DateTime FetchedAtUtc { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("entryCount")]
    public int EntryCount { get; set; }
}
