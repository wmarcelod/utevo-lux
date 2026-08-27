using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Overlays.GridOverlay;

/// <summary>
/// A grid pinned over the source CLIENT area. It snapshots the client rect (physical px) at
/// spawn and never follows afterwards. Because it covers the whole game viewport it stays
/// click-through at all times (an interactive full-viewport overlay would swallow every game
/// click), so it is not draggable — its geometry/appearance are managed from the dashboard.
///
/// DPI fix: the window is placed at the physical snapshot rect via SetWindowPos, and the cell
/// size (physical px) is divided by THIS window's monitor scale to get the DIP step. Lines then
/// land on physical multiples even at 125% / 150% DPI, unlike the original.
/// </summary>
public sealed class GridWindow : ClickThroughOverlayWindow
{
    private readonly GridConfig _config;
    private readonly Border _outline;
    private readonly GridCanvas _canvas;

    public GridWindow(IAppServices services, GridConfig config) : base(services)
    {
        _config = config;

        _canvas = new GridCanvas();
        _outline = new Border
        {
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Child = _canvas,
        };
        Content = _outline;

        SizeChanged += (_, _) => Redraw();
    }

    protected override bool Draggable => false; // pinned to the game

    protected override RECT InitialPlacementPhysical
        => new(_config.SnapLeft, _config.SnapTop,
               _config.SnapLeft + _config.SnapWidth, _config.SnapTop + _config.SnapHeight);

    protected override void OnScaleChanged(double scale) => Redraw();

    /// <summary>Recompute pen + DIP step from the config and repaint.</summary>
    public void Redraw()
    {
        double scale = CurrentScale;

        var brush = OverlayColor.FrozenBrush(
            _config.LineColor, _config.LineOpacity, Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5));
        var pen = new Pen(brush, _config.LineThickness);
        pen.Freeze();

        _outline.BorderBrush = brush;

        // PHYSICAL cell size -> DIP for THIS monitor via the overlay DPI helper. This is the
        // non-100%-DPI fix: dividing the physical step by the scale makes lines fall on physical
        // multiples at 125% / 150% DPI, unlike drawing the step as raw DIP.
        double stepDip = OverlayDpi.PxToDip(Services.Dpi, Math.Max(1, _config.GridSize), scale);
        _canvas.Configure(pen, stepDip);
    }
}
