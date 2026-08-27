using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Overlays.GridOverlay;

/// <summary>
/// A grid pinned over the source CLIENT area. It LIVE-FOLLOWS the bound source window: a
/// low-frequency <see cref="DispatcherTimer"/> (~150 ms) re-reads the source client rect and, when
/// it moved/resized, repositions/resizes this overlay to match (physical px). Because it covers the
/// whole game viewport it stays click-through at all times (an interactive full-viewport overlay
/// would swallow every game click), so it is not draggable — its geometry follows the source and
/// its appearance is managed from the dashboard.
///
/// DPI fix: the window is placed at the physical source rect via SetWindowPos, and the cell size
/// (physical px) is divided by THIS window's monitor scale to get the DIP step. Lines then land on
/// physical multiples even at 125% / 150% DPI, unlike the original. A re-follow that resizes the
/// window raises SizeChanged -> Redraw; one that crosses to a different-DPI monitor raises
/// WM_DPICHANGED -> OnScaleChanged -> Redraw, so the physical->DIP conversion is preserved on every
/// move.
/// </summary>
public sealed class GridWindow : ClickThroughOverlayWindow
{
    // Re-follow cadence. Low frequency keeps it cheap (a GetClientRect + ClientToScreen per tick,
    // no pixel work) while staying visually attached as the source window is dragged/resized.
    private static readonly TimeSpan FollowInterval = TimeSpan.FromMilliseconds(150);

    private readonly GridConfig _config;
    private readonly IntPtr _sourceHwnd;
    private readonly Border _outline;
    private readonly GridCanvas _canvas;
    private DispatcherTimer? _followTimer;

    public GridWindow(IAppServices services, GridConfig config, IntPtr sourceHwnd) : base(services)
    {
        _config = config;
        _sourceHwnd = sourceHwnd;

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); // creates the HWND + applies InitialPlacementPhysical

        // Start live-following the source. Nothing to follow without a resolved source HWND
        // (e.g. restore could not re-identify the window) — the overlay then stays at the snapshot.
        if (_sourceHwnd != IntPtr.Zero)
        {
            _followTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FollowInterval };
            _followTimer.Tick += OnFollowTick;
            _followTimer.Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_followTimer is not null)
        {
            _followTimer.Stop();
            _followTimer.Tick -= OnFollowTick;
            _followTimer = null;
        }
        base.OnClosed(e);
    }

    /// <summary>Re-read the source client rect; if it moved/resized, follow it (physical px).</summary>
    private void OnFollowTick(object? sender, EventArgs e)
    {
        RECT client = Services.Windows.GetClientBoundsInScreen(_sourceHwnd);

        // Source gone/minimized (GetClientBoundsInScreen returns an empty rect): keep the last
        // placement rather than snapping the grid to (0,0).
        if (client.Width <= 0 || client.Height <= 0)
            return;

        RECT current = GetBoundsPhysical();
        if (client.Left == current.Left && client.Top == current.Top &&
            client.Width == current.Width && client.Height == current.Height)
            return; // unchanged — no SetWindowPos, no repaint

        // Follow. A size change raises SizeChanged -> Redraw; a cross-monitor move raises
        // WM_DPICHANGED -> OnScaleChanged -> Redraw. A same-size move needs no repaint (the grid
        // pattern is window-relative and the DIP step is unchanged on the same monitor).
        SetBoundsPhysical(client.Left, client.Top, client.Width, client.Height);

        // Keep the persisted snapshot in sync so a later restore re-pins at the right place.
        _config.SnapLeft = client.Left;
        _config.SnapTop = client.Top;
        _config.SnapWidth = client.Width;
        _config.SnapHeight = client.Height;
    }

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
