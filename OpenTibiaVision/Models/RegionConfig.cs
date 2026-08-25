using System;

namespace OpenTibiaVision.Models;

/// <summary>
/// Serializable description of one mirror region. This is what RegionStore reads/writes
/// to regions.json.
///
/// Coordinate conventions:
///  - Crop* are PHYSICAL pixels relative to the source window's visible frame
///    (see DwmThumbnail.GetSourceBounds). They feed rcSource of the DWM thumbnail.
///  - Mirror* are WPF DIPs (device-independent units), the mirror window's bounds.
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

    // Crop rectangle in physical pixels, relative to the source window's visible frame.
    public int CropLeft { get; set; }
    public int CropTop { get; set; }
    public int CropRight { get; set; }
    public int CropBottom { get; set; }

    // Mirror window placement in WPF DIPs.
    public double MirrorLeft { get; set; } = 120;
    public double MirrorTop { get; set; } = 120;
    public double MirrorWidth { get; set; } = 400;
    public double MirrorHeight { get; set; } = 300;

    public bool Locked { get; set; }
    public bool Visible { get; set; }

    public int CropWidth => CropRight - CropLeft;
    public int CropHeight => CropBottom - CropTop;
}
