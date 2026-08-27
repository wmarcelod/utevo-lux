using System.Windows;
using System.Windows.Media;

namespace UtevoLux.UI;

/// <summary>
/// Helpers for code-built windows to read theme tokens with hardcoded fallbacks, so a window
/// created before (or without) the merged dictionaries still renders correctly.
/// </summary>
internal static class ThemeAccess
{
    public static SolidColorBrush Brush(string key, string fallbackHex)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush b)
            return b;

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex)!);
        brush.Freeze();
        return brush;
    }

    public static FontFamily Font(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key) is FontFamily f)
            return f;
        return new FontFamily(fallback);
    }

    public static Geometry? Icon(string key)
        => Application.Current?.TryFindResource(key) as Geometry;
}
