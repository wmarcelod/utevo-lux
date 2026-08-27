using System;
using System.Windows.Media;

namespace UtevoLux.Features.Overlays;

/// <summary>
/// Colour helpers for the overlays: parse a stored hex string and build a FROZEN brush at an
/// independent opacity. Freezing every brush is optimization principle 2 (cross-thread-safe,
/// GPU-cacheable, no clone-on-render). Independent background-vs-text opacity is achieved by
/// baking the alpha into two SEPARATE brushes rather than setting Window/element Opacity (which
/// would fade both together).
/// </summary>
internal static class OverlayColor
{
    public static Color Parse(string hex, Color fallback)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) &&
                ColorConverter.ConvertFromString(hex) is Color c)
                return c;
        }
        catch
        {
            // fall through to fallback
        }
        return fallback;
    }

    /// <summary>A frozen SolidColorBrush from a hex string, with alpha overridden by opacity (0..1).</summary>
    public static SolidColorBrush FrozenBrush(string hex, double opacity, Color fallback)
    {
        Color c = Parse(hex, fallback);
        byte a = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255.0);
        var brush = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>A frozen opaque brush from a hex string (alpha forced to 255).</summary>
    public static SolidColorBrush FrozenOpaque(string hex, Color fallback)
        => FrozenBrush(hex, 1.0, fallback);
}
