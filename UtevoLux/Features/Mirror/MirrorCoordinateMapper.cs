using System;
using UtevoLux.Services;

namespace UtevoLux.Features.Mirror;

/// <summary>
/// Pure geometry for the DWM mirror: the zoomed source sub-rect (rcSource) and the
/// mirror-point -> source-client mapping used by the right-click passthrough.
///
/// All rectangles are PHYSICAL pixels. Crop rects are relative to the source CLIENT area
/// (fSourceClientAreaOnly), so mapped points are directly usable as source client coordinates.
/// </summary>
public static class MirrorCoordinateMapper
{
    /// <summary>
    /// The rcSource to push for a given crop and content zoom (principle 1: zoom == smaller
    /// centered rcSource). zoom &gt; 1 shrinks toward the crop center; zoom &lt; 1 grows outward and
    /// is clamped to the source client bounds so it never samples outside the viewport.
    /// </summary>
    public static RECT ComputeZoomedSource(RECT crop, double zoom, int clientWidth, int clientHeight)
    {
        if (zoom <= 0)
            zoom = 1.0;

        double cropW = Math.Max(1, crop.Width);
        double cropH = Math.Max(1, crop.Height);

        double w = cropW / zoom;
        double h = cropH / zoom;

        double cx = crop.Left + cropW / 2.0;
        double cy = crop.Top + cropH / 2.0;

        double left = cx - w / 2.0;
        double top = cy - h / 2.0;

        // Clamp to [0, client] when we have valid client bounds (only bites for zoom < 1).
        if (clientWidth > 0)
        {
            if (w >= clientWidth) { left = 0; w = clientWidth; }
            else left = Math.Clamp(left, 0, clientWidth - w);
        }
        else if (left < 0) left = 0;

        if (clientHeight > 0)
        {
            if (h >= clientHeight) { top = 0; h = clientHeight; }
            else top = Math.Clamp(top, 0, clientHeight - h);
        }
        else if (top < 0) top = 0;

        int l = (int)Math.Round(left);
        int t = (int)Math.Round(top);
        int r = l + Math.Max(1, (int)Math.Round(w));
        int b = t + Math.Max(1, (int)Math.Round(h));
        return new RECT(l, t, r, b);
    }

    /// <summary>
    /// Map a point given in the mirror window's CLIENT physical pixels to the corresponding
    /// point in the SOURCE window's client physical pixels, through the SAME rcDestination ->
    /// rcSource transform DWM uses to composite. This is the fix for the passthrough: the raw
    /// cursor is only correct at 1:1 (dest == source, zoom == 1); otherwise it must be scaled
    /// by the destination/source ratio and offset by the crop origin.
    /// </summary>
    /// <returns>true if the point fell inside the mirrored region.</returns>
    public static bool TryMapMirrorPointToSourceClient(
        int mirrorClientX, int mirrorClientY, RECT rcDestination, RECT rcSource, out int srcX, out int srcY)
    {
        srcX = 0;
        srcY = 0;

        int destW = rcDestination.Width;
        int destH = rcDestination.Height;
        if (destW <= 0 || destH <= 0)
            return false;

        double fx = (mirrorClientX - rcDestination.Left) / (double)destW;
        double fy = (mirrorClientY - rcDestination.Top) / (double)destH;

        if (fx < 0 || fx > 1 || fy < 0 || fy > 1)
            return false;

        srcX = (int)Math.Round(rcSource.Left + fx * rcSource.Width);
        srcY = (int)Math.Round(rcSource.Top + fy * rcSource.Height);
        return true;
    }

    /// <summary>A crop box of the given size (source px) centered on a client point, clamped in-bounds.</summary>
    public static RECT CenteredBox(int centerX, int centerY, int boxW, int boxH, int clientWidth, int clientHeight)
    {
        boxW = Math.Max(8, boxW);
        boxH = Math.Max(8, boxH);

        if (clientWidth > 0) boxW = Math.Min(boxW, clientWidth);
        if (clientHeight > 0) boxH = Math.Min(boxH, clientHeight);

        int left = centerX - boxW / 2;
        int top = centerY - boxH / 2;

        if (clientWidth > 0) left = Math.Clamp(left, 0, Math.Max(0, clientWidth - boxW));
        else if (left < 0) left = 0;

        if (clientHeight > 0) top = Math.Clamp(top, 0, Math.Max(0, clientHeight - boxH));
        else if (top < 0) top = 0;

        return new RECT(left, top, left + boxW, top + boxH);
    }
}
