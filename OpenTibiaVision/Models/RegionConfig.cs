using System;

namespace OpenTibiaVision.Models;

/// <summary>
/// Serializable description of one mirror region, persisted via the shared ISettingsStore.
///
/// Coordinate conventions (all PHYSICAL pixels — conversion happens only at the WPF boundary):
///  - Crop* are physical pixels relative to the source window's CLIENT area (the game
///    viewport), matching DWM fSourceClientAreaOnly. They feed rcSource of the thumbnail.
///  - Mirror* are the mirror window's bounds in PHYSICAL screen pixels; the window is placed
///    with SetWindowPos and read back with GetWindowRect, so mixed-DPI setups stay exact.
///
/// The source window handle is not stable across restarts, so we persist the title and
/// process name and re-match on load (best effort).
/// </summary>
public class RegionConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Regiao";

    // Source identity (for best-effort re-binding after a restart).
    public string SourceTitle { get; set; } = "";
    public string SourceProcess { get; set; } = "";

    // Crop rectangle in physical pixels, relative to the source window's CLIENT area.
    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }

    // Mirror window placement in PHYSICAL screen pixels.
    public int MirrorLeft { get; set; } = 240;
    public int MirrorTop { get; set; } = 240;
    public int MirrorWidth { get; set; } = 400;
    public int MirrorHeight { get; set; } = 300;

    public bool Locked { get; set; }
    public bool Visible { get; set; }

    public int CropWidth => CropRight - CropLeft;
    public int CropHeight => CropBottom - CropTop;
}
