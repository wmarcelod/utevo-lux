using System;

namespace UtevoLux.Features.Overlays.Notes;

/// <summary>
/// Serializable description of one sticky note, persisted via the shared ISettingsStore
/// (atomic + 400 ms debounced). Placement is PHYSICAL screen pixels (the window is placed with
/// SetWindowPos and read back with GetWindowRect), so mixed-DPI setups stay exact.
///
/// Text and background opacity are INDEPENDENT (spec): each is baked into its own frozen brush
/// so a translucent card can carry fully opaque text, or vice-versa.
/// </summary>
public sealed class NoteConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = "";

    // Placement in physical screen pixels.
    public int Left { get; set; } = 320;
    public int Top { get; set; } = 320;
    public int Width { get; set; } = 240;
    public int Height { get; set; } = 140;

    // Colours as #AARRGGBB / #RRGGBB hex; alpha is overridden by the opacity fields below.
    public string BackColor { get; set; } = "#FF2C3340";
    public string TextColor { get; set; } = "#FFF3F5F9";

    public double BackOpacity { get; set; } = 0.85;
    public double TextOpacity { get; set; } = 1.0;

    public string FontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 16;

    public bool Locked { get; set; }
    public bool Visible { get; set; } = true;
}
