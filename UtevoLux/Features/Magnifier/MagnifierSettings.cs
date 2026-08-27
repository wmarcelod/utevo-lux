using System.Windows.Input;

namespace UtevoLux.Features.Magnifier;

/// <summary>Window/region shape for a magnifier, applied via SetWindowRgn.</summary>
public enum LensShape
{
    /// <summary>Rounded rectangle: a clean accent ring + rounded corners (the default look).</summary>
    RoundedRect,

    /// <summary>A true circle (elliptic region). Borderless, crisp round edge, no colored ring.</summary>
    Circle
}

/// <summary>
/// Serializable settings for the Magnifier module, persisted whole under one settings key. All
/// pixel geometry is PHYSICAL px (converted only at the WPF boundary, optimization principle 8);
/// zoom is a pure ratio (destination px / source px).
/// </summary>
public sealed class MagnifierSettings
{
    // ---- Follow-cursor lens ----

    /// <summary>Lens window edge length, physical px (square).</summary>
    public int LensSize { get; set; } = 260;

    public LensShape Shape { get; set; } = LensShape.RoundedRect;

    /// <summary>Corner radius (DIP) for the rounded-rect shape.</summary>
    public int CornerRadius { get; set; } = 16;

    /// <summary>Accent ring thickness (DIP) for the rounded-rect shape.</summary>
    public int RingThickness { get; set; } = 2;

    /// <summary>Wheel-zoom clamp and step (spec: 1.5–6.0 in 0.25 increments).</summary>
    public double ZoomMin { get; set; } = 1.5;
    public double ZoomMax { get; set; } = 6.0;
    public double ZoomStep { get; set; } = 0.25;

    /// <summary>Zoom applied each time the lens is activated.</summary>
    public double DefaultZoom { get; set; } = 2.5;

    /// <summary>Thumbnail opacity (0–255). Pushed with the OPACITY-only fast path unchanged.</summary>
    public byte Opacity { get; set; } = 255;

    /// <summary>Offset (physical px) of the lens center from the cursor. Zero = centered on cursor.</summary>
    public int CursorOffsetX { get; set; }
    public int CursorOffsetY { get; set; }

    // Hold-to-activate gesture (serialized as the underlying enum numeric values).
    public Key HoldKey { get; set; } = Key.M;
    public ModifierKeys HoldModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;

    // ---- Fixed-crop loupe ----

    public LoupeConfig Loupe { get; set; } = new();
}

/// <summary>
/// The fixed-crop magnifier: a placed, live DWM view of a FIXED sub-rect of a chosen source
/// window's client area at a set zoom. Unlike the follow lens the crop does not track the cursor,
/// so the DWM props are pushed once (and on change) — the compositor keeps the view live for free.
/// </summary>
public sealed class LoupeConfig
{
    /// <summary>Best-effort source identity for re-binding after a restart.</summary>
    public string SourceTitle { get; set; } = "";

    /// <summary>Crop centre as a fraction (0..1) of the source CLIENT area.</summary>
    public double CenterX { get; set; } = 0.5;
    public double CenterY { get; set; } = 0.5;

    public double Zoom { get; set; } = 3.0;
    public LensShape Shape { get; set; } = LensShape.RoundedRect;
    public byte Opacity { get; set; } = 255;

    // Placement in PHYSICAL screen px (SetWindowPos / GetWindowRect, mixed-DPI exact).
    public int Left { get; set; } = 120;
    public int Top { get; set; } = 120;
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 320;

    public bool Locked { get; set; }
    public bool Visible { get; set; }
}
