using System;
using System.Windows;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Overlays;

/// <summary>
/// The overlays' DPI helper (optimization principle 8: geometry lives in PHYSICAL pixels;
/// convert only at the WPF boundary). This is what the Grid overlay uses to fix the original's
/// non-100%-DPI misalignment: it pins its window over the source CLIENT rect in physical px via
/// SetWindowPos, then draws its lines in DIP computed from the SAME physical extent divided by
/// the overlay window's own monitor scale — so every line lands exactly on a physical multiple.
///
/// It delegates the scalar identity fast-path to <see cref="IDpiService"/> so 100%-DPI setups
/// pay zero conversion cost, and adds rect/size helpers the service does not expose.
/// </summary>
internal static class OverlayDpi
{
    /// <summary>Physical px -> DIP (identity fast-path at scale 1.0).</summary>
    public static double PxToDip(IDpiService dpi, double physical, double scale)
        => dpi.ToDip(physical, scale);

    /// <summary>DIP -> physical px (identity fast-path at scale 1.0).</summary>
    public static int DipToPx(IDpiService dpi, double dip, double scale)
        => dpi.ToPhysical(dip, scale);

    /// <summary>
    /// The DIP size of a physical rect for the given scale. Placement is done separately in
    /// physical px (SetWindowPos); this is only for laying out DIP content INSIDE the window.
    /// </summary>
    public static Size PhysicalToDipSize(IDpiService dpi, RECT physical, double scale)
        => new Size(dpi.ToDip(physical.Width, scale), dpi.ToDip(physical.Height, scale));
}
