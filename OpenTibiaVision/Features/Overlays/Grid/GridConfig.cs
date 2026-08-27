namespace OpenTibiaVision.Features.Overlays.GridOverlay;

/// <summary>
/// Serializable state for the grid overlay. The snapshot rect is the source CLIENT area in
/// PHYSICAL pixels captured when the grid is pinned (the grid does NOT follow the window after
/// that — it is a fixed reference). <see cref="GridSize"/> is in PHYSICAL pixels; the overlay
/// converts it to DIP with the pinned monitor's scale so lines land exactly on physical
/// multiples at any DPI (the original misaligns at non-100% DPI; this is the fix).
/// </summary>
public sealed class GridConfig
{
    public bool Visible { get; set; }

    // Source identity (best-effort re-detect on restore).
    public string SourceTitle { get; set; } = "";

    // Snapshot of the source CLIENT area in physical screen pixels, taken when pinned.
    public int SnapLeft { get; set; }
    public int SnapTop { get; set; }
    public int SnapWidth { get; set; }
    public int SnapHeight { get; set; }

    /// <summary>Cell size in PHYSICAL pixels.</summary>
    public int GridSize { get; set; } = 32;

    public string LineColor { get; set; } = "#FF3FA9F5";
    public double LineOpacity { get; set; } = 0.45;

    /// <summary>Line thickness in DIP.</summary>
    public double LineThickness { get; set; } = 1.0;

    public bool HasSnapshot => SnapWidth > 0 && SnapHeight > 0;
}
