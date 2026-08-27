using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Features.Audio;

/// <summary>
/// The runtime visual-alert banner: a transparent, click-through, no-activate window that floats
/// over the game and never steals focus (WS_EX_LAYERED|TRANSPARENT + WS_EX_NOACTIVATE|TOOLWINDOW,
/// exactly like the shared Toast). It is created ONCE per timer and Show/Hidden — never closed
/// until shutdown (principle 3). Two dismissal modes: Fade (auto after a hold) and
/// StayUntilHotkey (visible until <see cref="Dismiss"/> is called by the module's dismiss hotkey).
///
/// Placement is in PHYSICAL px via SetWindowPos when the config gives an explicit position; the
/// drag-to-place twin (<see cref="AlertPlacerWindow"/>) is how the user sets that position.
/// </summary>
public sealed class AlertBannerWindow
{
    private readonly IAppServices _services;
    private readonly Window _window;
    private readonly ContentControl _host;
    private readonly DispatcherTimer _holdTimer;
    private IntPtr _hwnd;
    private bool _chromeApplied;

    public AlertBannerWindow(IAppServices services)
    {
        _services = services;

        _host = new ContentControl { HorizontalAlignment = HorizontalAlignment.Center };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Title = "Utevo Lux - Alerta",
            Content = _host
        };

        _window.SourceInitialized += (_, _) => ApplyChrome();

        _holdTimer = new DispatcherTimer();
        _holdTimer.Tick += OnHoldElapsed;
    }

    /// <summary>Show (or refresh) the banner for a timer expiry.</summary>
    public void Show(AlertConfig cfg, string text)
    {
        _host.Content = AlertVisual.Build(cfg, text, out _);

        _holdTimer.Stop();
        _window.BeginAnimation(UIElement.OpacityProperty, null);
        _window.Opacity = 1.0;

        if (!_window.IsVisible)
            _window.Show();
        if (!_chromeApplied)
            ApplyChrome();

        _window.UpdateLayout();
        Position(cfg);

        if (cfg.Mode == AlertMode.Fade)
        {
            _holdTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(200, cfg.DurationMs));
            _holdTimer.Start();
        }
        // StayUntilHotkey: no timer; stays until Dismiss().
    }

    /// <summary>Fade out and hide now (the dismiss hotkey / stop-all path).</summary>
    public void Dismiss()
    {
        _holdTimer.Stop();
        if (!_window.IsVisible)
            return;
        BeginFadeOut();
    }

    private void OnHoldElapsed(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        BeginFadeOut();
    }

    private void BeginFadeOut()
    {
        var anim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(400));
        anim.Completed += (_, _) =>
        {
            _window.Hide();
            _window.BeginAnimation(UIElement.OpacityProperty, null);
            _window.Opacity = 1.0;
        };
        _window.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void Position(AlertConfig cfg)
    {
        if (_hwnd == IntPtr.Zero)
            return;

        if (cfg.PosX >= 0 && cfg.PosY >= 0)
        {
            // Explicit physical-pixel placement (mixed-DPI exact); size stays content-driven.
            NativeMethods.SetWindowPos(
                _hwnd, NativeMethods.HWND_TOPMOST,
                cfg.PosX, cfg.PosY, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        else
        {
            // Auto: top-centre of the primary work area (DIP; WPF places correctly on the primary).
            Rect wa = SystemParameters.WorkArea;
            _window.Left = wa.Left + (wa.Width - _window.ActualWidth) / 2;
            _window.Top = wa.Top + 90;
        }
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
        _holdTimer.Stop();
        try { _window.Close(); } catch { /* ignore */ }
    }
}
