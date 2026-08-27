namespace UtevoLux.Features.Overlays.Glow;

/// <summary>
/// Serializable state for the cursor-glow ring. Sizes are in DIP (the ring should look the same
/// physical size on any monitor, so the window's PHYSICAL size is DIP x monitor-scale).
/// </summary>
public sealed class GlowConfig
{
    public bool Visible { get; set; }

    public string Color { get; set; } = "#FF3FA9F5";
    public double Opacity { get; set; } = 0.6;

    /// <summary>Outer ring diameter in DIP.</summary>
    public double OuterSize { get; set; } = 64;

    /// <summary>Ring stroke thickness in DIP.</summary>
    public double Thickness { get; set; } = 3;
}
