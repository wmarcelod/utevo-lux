using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// A per-timer countdown BAR overlay: a transparent, click-through, no-activate window whose fill
/// depletes toward a chosen edge and flashes on expiry. It rides the shared 50 ms wall-clock
/// ticker (principle 4) and reads its remaining fraction from a provider delegate, so it holds no
/// timing state of its own and stays drift-immune. Created ONCE per timer and Show/Hidden
/// (principle 3). Placed in PHYSICAL px so it lands exactly over a mirror on any monitor.
///
/// It is "per-mirror" by placement: drag it (via the config's position) to sit over the mirror it
/// belongs to. (A future cross-module hook could auto-follow a specific MirrorWindow; that would
/// live in the foundation, not here.)
/// </summary>
public sealed class CountdownBarWindow
{
    private const int FlashFlipMs = 150;

    private readonly IAppServices _services;
    private readonly WallClockTicker _ticker;
    private readonly Window _window;
    private readonly Border _root;
    private readonly Rectangle _fill;

    private IDisposable? _tickSub;
    private IntPtr _hwnd;
    private bool _chromeApplied;

    private BarConfig _cfg = new();
    private Func<(double fraction, bool expired)>? _provider;

    private SolidColorBrush _fillBrush = Brushes.DeepSkyBlue;
    private SolidColorBrush _flashBrush = Brushes.Red;
    private bool _flashOn;
    private long _lastFlipTicks;

    public CountdownBarWindow(IAppServices services, WallClockTicker barTicker)
    {
        _services = services;
        _ticker = barTicker;

        _fill = new Rectangle { RadiusX = 2, RadiusY = 2 };

        _root = new Border
        {
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = new Grid { Children = { _fill } }
        };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Title = "OpenTibiaVision - Barra",
            Content = _root
        };

        _window.SourceInitialized += (_, _) => ApplyChrome();
    }

    /// <summary>Show/refresh the bar, tracking <paramref name="provider"/> for its remaining fraction.</summary>
    public void Show(BarConfig cfg, Func<(double fraction, bool expired)> provider)
    {
        _cfg = cfg;
        _provider = provider;

        _fillBrush = AlertVisual.Brush(cfg.FillHex, "#FF4CC2FF");
        _flashBrush = AlertVisual.Brush(cfg.FlashHex, "#FFFF5252");
        _root.Background = AlertVisual.Brush(cfg.TrackHex, "#66000000");
        _fill.Fill = _fillBrush;
        _flashOn = false;

        ApplyDepletionAnchor(cfg.DepleteFrom);

        if (!_window.IsVisible)
            _window.Show();
        if (!_chromeApplied)
            ApplyChrome();

        PositionPhysical(cfg);

        _tickSub ??= _ticker.Subscribe(OnTick);
        OnTick(); // paint an initial frame immediately
    }

    public void Hide()
    {
        _tickSub?.Dispose();
        _tickSub = null;
        if (_window.IsVisible)
            _window.Hide();
    }

    private void ApplyDepletionAnchor(BarSide side)
    {
        switch (side)
        {
            case BarSide.Left:
                _fill.HorizontalAlignment = HorizontalAlignment.Left;
                _fill.VerticalAlignment = VerticalAlignment.Stretch;
                break;
            case BarSide.Right:
                _fill.HorizontalAlignment = HorizontalAlignment.Right;
                _fill.VerticalAlignment = VerticalAlignment.Stretch;
                break;
            case BarSide.Top:
                _fill.HorizontalAlignment = HorizontalAlignment.Stretch;
                _fill.VerticalAlignment = VerticalAlignment.Top;
                break;
            case BarSide.Bottom:
                _fill.HorizontalAlignment = HorizontalAlignment.Stretch;
                _fill.VerticalAlignment = VerticalAlignment.Bottom;
                break;
        }
    }

    private void OnTick()
    {
        if (_provider is null)
            return;

        (double fraction, bool expired) = _provider();
        double frac = Math.Clamp(fraction, 0.0, 1.0);

        double w = _root.ActualWidth;
        double h = _root.ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        bool horizontal = _cfg.DepleteFrom is BarSide.Left or BarSide.Right;
        if (horizontal)
        {
            _fill.Width = Math.Max(0, w * frac);
            _fill.ClearValue(FrameworkElement.HeightProperty); // stretch vertically
        }
        else
        {
            _fill.Height = Math.Max(0, h * frac);
            _fill.ClearValue(FrameworkElement.WidthProperty); // stretch horizontally
        }

        if (expired && _cfg.FlashOnExpiry)
        {
            long now = Environment.TickCount64;
            if (now - _lastFlipTicks >= FlashFlipMs)
            {
                _lastFlipTicks = now;
                _flashOn = !_flashOn;
                _fill.Fill = _flashOn ? _flashBrush : _fillBrush;
            }
        }
        else if (_flashOn)
        {
            _flashOn = false;
            _fill.Fill = _fillBrush;
        }
    }

    private void PositionPhysical(BarConfig cfg)
    {
        if (_hwnd == IntPtr.Zero)
            return;
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            cfg.PosX, cfg.PosY, Math.Max(8, cfg.Width), Math.Max(6, cfg.Height),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        _window.UpdateLayout();
    }

    private void ApplyChrome()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero)
            return;
        _services.Windows.SetClickThrough(_hwnd, true);
        _services.Windows.SetOverlayChrome(_hwnd, true);
        _chromeApplied = true;
    }

    /// <summary>Real close on app shutdown.</summary>
    public void Shutdown()
    {
        _tickSub?.Dispose();
        _tickSub = null;
        try { _window.Close(); } catch { /* ignore */ }
    }
}
