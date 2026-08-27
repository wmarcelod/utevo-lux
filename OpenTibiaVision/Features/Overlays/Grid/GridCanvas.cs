using System;
using System.Windows;
using System.Windows.Media;

namespace OpenTibiaVision.Features.Overlays.GridOverlay;

/// <summary>
/// Draws the grid lines in one OnRender pass with a single FROZEN pen (no per-line UIElements):
/// cheap, GPU-cacheable, and only ever redrawn when the pin rect / cell size / DPI changes.
///
/// <see cref="StepDip"/> is the cell size already converted to DIP for the pinned monitor, so
/// the element simply walks 0..ActualWidth/ActualHeight in DIP and every line falls on a
/// physical-pixel multiple. Guidelines + snap-to-device-pixels keep the 1px lines crisp.
/// </summary>
public sealed class GridCanvas : FrameworkElement
{
    private Pen? _pen;
    private double _stepDip = 32;

    public GridCanvas()
    {
        SnapsToDevicePixels = true;
        IsHitTestVisible = false; // never intercept input, even if the window is interactive
    }

    public void Configure(Pen pen, double stepDip)
    {
        _pen = pen;
        _stepDip = Math.Max(1.0, stepDip);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_pen is null)
            return;

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        double half = _pen.Thickness / 2.0;

        // Guidelines must be pushed BEFORE drawing so the 1px lines snap to device pixels.
        var guidelines = new GuidelineSet();
        for (double x = 0; x <= w + 0.5; x += _stepDip)
            guidelines.GuidelinesX.Add(x + half);
        for (double y = 0; y <= h + 0.5; y += _stepDip)
            guidelines.GuidelinesY.Add(y + half);
        dc.PushGuidelineSet(guidelines);

        for (double x = 0; x <= w + 0.5; x += _stepDip)
            dc.DrawLine(_pen, new Point(x, 0), new Point(x, h));
        for (double y = 0; y <= h + 0.5; y += _stepDip)
            dc.DrawLine(_pen, new Point(0, y), new Point(w, y));

        dc.Pop();
    }
}
