namespace UtevoLux.Models;

/// <summary>
/// A rectangle expressed as fractions (0..1) of some reference area. The region-select
/// overlay produces fractions of the source window; the caller multiplies by the source's
/// physical size to get a pixel crop. Using fractions keeps region selection independent
/// of DPI and window resolution.
/// </summary>
public readonly record struct RectFraction(double X, double Y, double W, double H)
{
    public bool IsUsable => W > 0.01 && H > 0.01;
}
