using System;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// Per-region UX state that layers on top of <see cref="OpenTibiaVision.Models.RegionConfig"/>
/// WITHOUT modifying it (RegionConfig is a shared/foundation model). Persisted separately, keyed
/// by the region's stable Id, via <see cref="MirrorUxStore"/>.
///
/// Coordinate/units conventions:
///  - <see cref="Zoom"/> is CONTENT magnification: rcSource shrinks to crop/zoom, centered
///    (principle 1). 1.0 == the whole crop; >1 magnifies into it; &lt;1 shows surrounding context.
///  - <see cref="Opacity"/> is the raw DWM byte pushed with the OPACITY-only flag.
///  - <see cref="FixedCropWidth"/>/<see cref="FixedCropHeight"/> are the loupe's fixed-box crop
///    size in SOURCE physical pixels (what a one-click loupe crop captures).
/// </summary>
public sealed class MirrorUxState
{
    public const double MinZoom = 0.5;
    public const double MaxZoom = 4.0;
    public const byte MinOpacity = 40;   // never fully invisible
    public const double MinScalePercent = 25;
    public const double MaxScalePercent = 400;

    /// <summary>Content magnification, 0.5..4.0.</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>DWM opacity byte (0..255), floored at <see cref="MinOpacity"/> in practice.</summary>
    public byte Opacity { get; set; } = 255;

    /// <summary>Forward WM_RBUTTONDOWN/UP to the source (remapped through the rcSource transform).</summary>
    public bool RightClickPassthrough { get; set; }

    /// <summary>Auto-hide the mirror window when the source is minimized/hidden (WinEvent driven).</summary>
    public bool AutoHide { get; set; } = true;

    /// <summary>Fixed-box crop size (source physical px) for the loupe's one-click crop.</summary>
    public int FixedCropWidth { get; set; } = 220;
    public int FixedCropHeight { get; set; } = 160;

    public void ClampZoom() => Zoom = Math.Clamp(Zoom, MinZoom, MaxZoom);

    public MirrorUxState Clone() => new()
    {
        Zoom = Zoom,
        Opacity = Opacity,
        RightClickPassthrough = RightClickPassthrough,
        AutoHide = AutoHide,
        FixedCropWidth = FixedCropWidth,
        FixedCropHeight = FixedCropHeight
    };
}
