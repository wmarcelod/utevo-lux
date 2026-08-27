# Resources — map runtime assets

These folders hold the runtime data the map feature reads at startup. They are **not committed to
git** (see `UtevoLux/.gitignore`) — each install populates them locally. Only this README is tracked.

## What lives here

| Folder            | Contents                                                                 |
|-------------------|--------------------------------------------------------------------------|
| `minimap/`        | `Minimap_Color_x_y_z.png` world tiles (a bundled snapshot fallback)       |
| `map/`            | `monster_spawns.dat` (+ `npcs.json`, `rare_creatures*.json`) — spawn fallback |
| `creatures/`      | creature sprite gifs (`<slug>.gif`)                                       |
| `npcs/`           | NPC sprite gifs (`<slug>.gif`)                                            |
| `items/`          | item icon gifs/pngs (`<slug>.gif|png`) + `_manifest.json`                 |
| `Icons/MapMarkers/` | 20 `marker_NN.png` pin icons (0..19, matching the game's map-mark icons) |

## Where the assets come from

At **runtime**, the map prefers live/local sources and only falls back to the bundled snapshots here:

- **Minimap** — prefers the player's own explored minimap from an installed Tibia client
  (`GameMinimapLocator`, `%LOCALAPPDATA%\Tibia\packages\Tibia\minimap`); falls back to `minimap/`.
- **Creature spawns** — prefers the live `tibiaroute.com` dataset (fetched once per launch, cached
  to `%APPDATA%\UtevoLux`); falls back to `map/monster_spawns.dat`.
- **Creature loot** — fetched from the TibiaData API (`api.tibiadata.com`), cached to `%APPDATA%`.

The **sprite banks** (`creatures/`, `npcs/`, `items/`) are extracted from the current official Tibia
client's asset files (`appearances.dat` + sprite sheets; creature name↔outfit mapping from
`staticdata.dat`) and assembled into per-name gifs/pngs. `monster_spawns.dat` is an
AES-256-CBC-over-gzip container decoded by `Features/Map/SpawnDataCodec.cs`.

## Build wiring

`UtevoLux.csproj` includes `Resources\**\*` as `Content` with `CopyToOutputDirectory=PreserveNewest`,
so the built app gets `Resources\...` next to the exe. Map services resolve assets via
`AppDomain.CurrentDomain.BaseDirectory\Resources\...` (with a `Directory.GetCurrentDirectory()`
fallback). Missing sprites degrade gracefully (a name with no file simply renders no icon; a missing
marker icon falls back to a drawn blue dot).
