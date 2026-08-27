# Resources — TibiaMaps runtime assets

These folders hold the runtime data the TibiaMaps feature reads at startup. They are **copied
from the installed original TibiaVision** and are intentionally **not committed to git** (see
`OpenTibiaVision/.gitignore`). Only this README is tracked.

## What lives here

| Folder      | Contents                                                            | Count |
|-------------|--------------------------------------------------------------------|-------|
| `minimap/`  | `Minimap_Color_x_y_z.png` tiles (256x256 world px each)             | 1094  |
| `map/`      | `monster_spawns.dat`, `npcs.json`, `rare_creatures.json`, `rare_creatures_manual.json` | 4 |
| `creatures/`| creature sprite gifs (`<slug>.gif`)                                 | 996   |
| `npcs/`     | NPC sprite gifs (`<slug>.gif`)                                      | 1236  |

`monster_spawns.dat` is an AES-256-CBC-over-gzip-over-binary container decoded by
`Features/Map/SpawnDataCodec.cs`.

## How they got here

Copied with robocopy from the installed TibiaVision:

```
robocopy "C:\Program Files\TibiaVision\Resources\minimap"   ".\Resources\minimap"   /E
robocopy "C:\Program Files\TibiaVision\Resources\map"       ".\Resources\map"       /E
robocopy "C:\Program Files\TibiaVision\Resources\creatures" ".\Resources\creatures" /E
robocopy "C:\Program Files\TibiaVision\Resources\npcs"      ".\Resources\npcs"      /E
```

To refresh after a TibiaVision update, re-run the same commands.

## Build wiring

`OpenTibiaVision.csproj` includes `Resources\**\*` as `Content` with
`CopyToOutputDirectory=PreserveNewest`, so the built app gets `Resources\minimap` etc. next to
the exe. The map services resolve assets via `AppDomain.CurrentDomain.BaseDirectory\Resources\...`
(with a `Directory.GetCurrentDirectory()` fallback), matching the original's lookup.

## Not copied

`Icons/MapMarkers/` (the 20 `marker_NN.png` pin icons) exists in the original but is **not** part
of this copy. Without it, `MarkerIconProvider` falls back to a drawn dot in the fork's blue accent
(#FF3FA9F5). Copy that folder too if the real pin icons are wanted.
