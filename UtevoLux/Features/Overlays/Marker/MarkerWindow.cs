using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Features.Overlays.Marker;

/// <summary>
/// A passive character-location marker (circle or arrow). It is DECORATION: user-parked and
/// static — it does NOT track the character. Locked = click-through; unlocked shows a selection
/// border and can be dragged to a new spot (base class handles the physical-pixel drag).
/// </summary>
public sealed class MarkerWindow : ClickThroughOverlayWindow
{
    private readonly MarkerConfig _config;
    private readonly Border _selection;
    private readonly Grid _shapeHost;

    public MarkerWindow(IAppServices services, MarkerConfig config) : base(services)
    {
        _config = config;

        _shapeHost = new Grid();
        _selection = new Border
        {
            BorderBrush = OverlayUi.Brush("AccentBrush", "#FF3FA9F5"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4),
            Child = _shapeHost,
        };
        Content = _selection;

        Width = _config.Size + 8;
        Height = _config.Size + 8;

        ApplyStyle();
    }

    public MarkerConfig Config => _config;

    protected override RECT InitialPlacementPhysical
    {
        get
        {
            int side = Services.Dpi.ToPhysical(_config.Size + 8, CurrentScale);
            return new RECT(_config.Left, _config.Top, _config.Left + side, _config.Top + side);
        }
    }

    protected override void OnLockChanged(bool locked)
    {
        _selection.BorderThickness = new Thickness(locked ? 0 : 2);
        _config.Locked = locked;
    }

    protected override void OnScaleChanged(double scale)
    {
        RECT r = GetBoundsPhysical();
        int side = Services.Dpi.ToPhysical(_config.Size + 8, scale);
        SetBoundsPhysical(r.Left, r.Top, side, side);
    }

    /// <summary>Rebuild the shape from the config (colour / opacity / shape / size).</summary>
    public void ApplyStyle()
    {
        _shapeHost.Children.Clear();

        var brush = OverlayColor.FrozenBrush(
            _config.Color, _config.Opacity, Color.FromArgb(0xFF, 0xE5, 0x53, 0x4B));

        FrameworkElement shape = _config.Shape.Equals("arrow", StringComparison.OrdinalIgnoreCase)
            ? BuildArrow(brush)
            : BuildCircle(brush);

        _shapeHost.Children.Add(shape);
        Width = _config.Size + 8;
        Height = _config.Size + 8;
    }

    private FrameworkElement BuildCircle(Brush brush)
    {
        double d = _config.Size;
        var ring = new Ellipse
        {
            Width = d,
            Height = d,
            Stroke = brush,
            StrokeThickness = Math.Max(2, d * 0.12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var dot = new Ellipse
        {
            Width = Math.Max(3, d * 0.18),
            Height = Math.Max(3, d * 0.18),
            Fill = brush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var g = new Grid { Width = d, Height = d };
        g.Children.Add(ring);
        g.Children.Add(dot);
        return g;
    }

    private FrameworkElement BuildArrow(Brush brush)
    {
        double d = _config.Size;
        // A downward-pointing arrow whose tip marks the spot.
        var geometry = Geometry.Parse("M0.5,0 L1,0.55 L0.65,0.55 L0.65,1 L0.35,1 L0.35,0.55 L0,0.55 Z");
        var path = new Path
        {
            Data = geometry,
            Fill = brush,
            Stretch = Stretch.Uniform,
            Width = d,
            Height = d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        return path;
    }

    public void PersistBounds()
    {
        RECT r = GetBoundsPhysical();
        if (r.Width <= 0) return;
        _config.Left = r.Left;
        _config.Top = r.Top;
    }
}
