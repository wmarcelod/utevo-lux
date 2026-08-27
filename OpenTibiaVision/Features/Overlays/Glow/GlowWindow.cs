using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Overlays.Glow;

/// <summary>
/// A cursor-following glow: three concentric rounded Borders that track the pointer. The follow
/// loop is <see cref="CompositionTarget.Rendering"/> (once per frame, ~16 ms at 60 fps — the
/// "@Render" tick of optimization principle 4) reading GetCursorPos and pushing ONE SetWindowPos
/// move. It is always click-through and never draggable (pure decoration), and the render hook
/// is attached only while the window is visible, so it costs nothing when hidden.
///
/// The rings are laid out in DIP; the window's PHYSICAL size is DIP x monitor-scale, so the glow
/// keeps a constant on-screen size across mixed-DPI monitors (re-sized on WM_DPICHANGED).
/// </summary>
public sealed class GlowWindow : ClickThroughOverlayWindow
{
    private readonly GlowConfig _config;
    private readonly Grid _rings;
    private bool _following;
    private int _physW = 64;
    private int _physH = 64;

    public GlowWindow(IAppServices services, GlowConfig config) : base(services)
    {
        _config = config;
        _rings = new Grid { IsHitTestVisible = false };
        Content = _rings;

        Width = _config.OuterSize;
        Height = _config.OuterSize;

        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) StartFollow();
            else StopFollow();
        };

        BuildRings();
    }

    protected override bool Draggable => false;

    protected override RECT InitialPlacementPhysical
    {
        get
        {
            ComputePhysicalSize();
            OverlayNative.GetCursorPos(out NativeMethods.POINT c);
            return new RECT(c.X - _physW / 2, c.Y - _physH / 2, c.X + _physW / 2, c.Y + _physH / 2);
        }
    }

    protected override void OnScaleChanged(double scale)
    {
        ComputePhysicalSize();
        // Re-apply size at the current position.
        RECT r = GetBoundsPhysical();
        SetBoundsPhysical(r.Left, r.Top, _physW, _physH);
    }

    // ---- rings ----

    /// <summary>Rebuild the three rings (call after a colour / size / thickness change).</summary>
    public void BuildRings()
    {
        _rings.Children.Clear();

        Color baseColor = OverlayColor.Parse(_config.Color, Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5));
        double outer = Math.Max(8, _config.OuterSize);

        // Three concentric rings at 100% / 68% / 36% of the outer size, fading inward.
        AddRing(outer, _config.Opacity, baseColor);
        AddRing(outer * 0.68, _config.Opacity * 0.8, baseColor);
        AddRing(outer * 0.36, _config.Opacity * 0.6, baseColor);

        Width = outer;
        Height = outer;
        ComputePhysicalSize();
    }

    private void AddRing(double diameter, double opacity, Color color)
    {
        byte a = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var brush = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
        brush.Freeze();

        var ring = new Border
        {
            Width = diameter,
            Height = diameter,
            CornerRadius = new CornerRadius(diameter / 2),
            BorderBrush = brush,
            BorderThickness = new Thickness(_config.Thickness),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _rings.Children.Add(ring);
    }

    private void ComputePhysicalSize()
    {
        double scale = CurrentScale;
        _physW = Math.Max(1, Services.Dpi.ToPhysical(_config.OuterSize, scale));
        _physH = _physW;
    }

    // ---- follow loop (16 ms @Render) ----

    private void StartFollow()
    {
        if (_following) return;
        _following = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopFollow()
    {
        if (!_following) return;
        _following = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!OverlayNative.GetCursorPos(out NativeMethods.POINT c))
            return;
        // ONE move per frame; size unchanged (keepSize) so this is a pure reposition.
        SetBoundsPhysical(c.X - _physW / 2, c.Y - _physH / 2, _physW, _physH, keepSize: true);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopFollow();
        base.OnClosed(e);
    }
}
