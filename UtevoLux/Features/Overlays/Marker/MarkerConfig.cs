using System;

namespace UtevoLux.Features.Overlays.Marker;

/// <summary>
/// Serializable state for the passive character-location marker. Placement is PHYSICAL px; size
/// is DIP (constant on-screen size across DPI). The marker is DECORATION: it is user-parked and
/// does NOT track the character — it simply stays where the user drops it.
/// </summary>
public sealed class MarkerConfig
{
    public bool Visible { get; set; }

    // Placement in physical screen pixels (top-left of the marker window).
    public int Left { get; set; } = 400;
    public int Top { get; set; } = 400;

    /// <summary>Marker box size in DIP.</summary>
    public double Size { get; set; } = 40;

    /// <summary>"circle" or "arrow".</summary>
    public string Shape { get; set; } = "circle";

    public string Color { get; set; } = "#FFE5534B";
    public double Opacity { get; set; } = 0.9;

    public bool Locked { get; set; }
}
