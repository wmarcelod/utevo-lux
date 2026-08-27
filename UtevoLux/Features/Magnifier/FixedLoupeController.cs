using System;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Features.Magnifier;

/// <summary>
/// The fixed-crop magnifier variant: a placed, live DWM view of a FIXED sub-rect of a chosen
/// source window's client area at a set zoom. Because the crop does not follow the cursor, the
/// DWM properties are pushed once (and again only on a zoom / centre / placement / DPI change) —
/// the compositor keeps the content live for free, so there is no per-frame timer here.
///
/// The loupe window is interactive: draggable / resizable while unlocked, click-through while
/// locked. Placement is persisted in physical px.
/// </summary>
internal sealed class FixedLoupeController : IDisposable
{
    private readonly IAppServices _services;
    private readonly MagnifierSettings _settings;
    private readonly Action _persist;

    private MagnifierWindow? _win;
    private IntPtr _source;

    public FixedLoupeController(IAppServices services, MagnifierSettings settings, Action persist)
    {
        _services = services;
        _settings = settings;
        _persist = persist;
    }

    public bool IsVisible => _settings.Loupe.Visible && _win is not null;
    public bool IsLocked => _settings.Loupe.Locked;
    public bool HasSource => _source != IntPtr.Zero;

    // ---- source ----

    public void SetSource(IntPtr hwnd, string title)
    {
        _source = hwnd;
        _settings.Loupe.SourceTitle = title ?? "";
        if (_win is not null && hwnd != IntPtr.Zero)
        {
            _win.SetSource(hwnd);
            Refresh();
        }
        _persist();
    }

    // ---- visibility ----

    public void Show()
    {
        if (_source == IntPtr.Zero)
        {
            _services.Info("UtevoLux", "Selecione uma janela fonte para a lupa fixa.");
            return;
        }

        LoupeConfig c = _settings.Loupe;
        EnsureWindow();

        c.Visible = true;
        _win!.Show();                                       // re-show if previously hidden
        _win.SetPlacementPhysical(c.Left, c.Top, c.Width, c.Height);
        _win.SetShape(c.Shape, _settings.CornerRadius, _settings.RingThickness);
        _win.SetSource(_source);
        _win.ApplyClickThrough(c.Locked);
        Refresh();
        _persist();
    }

    public void Hide()
    {
        _settings.Loupe.Visible = false;
        if (_win is not null)
        {
            _win.UpdateView(default, 0, false);
            _win.Unregister();
            _win.Hide();
        }
        _persist();
    }

    public void Toggle()
    {
        if (_settings.Loupe.Visible && _win is not null)
            Hide();
        else
            Show();
    }

    // ---- adjustments (re-push the view; DWM otherwise keeps it live for free) ----

    public void SetLock(bool locked)
    {
        _settings.Loupe.Locked = locked;
        _win?.ApplyClickThrough(locked);
        _persist();
    }

    public void SetZoom(double zoom)
    {
        _settings.Loupe.Zoom = zoom;
        Refresh();
        _persist();
    }

    public void SetCenter(double x, double y)
    {
        _settings.Loupe.CenterX = Clamp01(x);
        _settings.Loupe.CenterY = Clamp01(y);
        Refresh();
        _persist();
    }

    public void SetShape(LensShape shape)
    {
        _settings.Loupe.Shape = shape;
        _win?.SetShape(shape, _settings.CornerRadius, _settings.RingThickness);
        Refresh();
        _persist();
    }

    // ---- lifecycle ----

    private void EnsureWindow()
    {
        if (_win is not null)
            return;

        // interactive == true: draggable / resizable while unlocked, placement persisted.
        // The HWND is realized (and placed) by the caller's _win.Show() + SetPlacementPhysical.
        _win = new MagnifierWindow(_services, interactive: true);
        _win.PlacementChanged += OnPlacementChanged;
        _win.ViewInvalidated += Refresh;
        _win.Closed += OnWindowClosed;
    }

    /// <summary>Close the window WITHOUT flipping the persisted Visible flag (app shutdown).</summary>
    public void CloseKeepState()
    {
        if (_win is not null)
        {
            _win.PlacementChanged -= OnPlacementChanged;
            _win.ViewInvalidated -= Refresh;
            _win.Closed -= OnWindowClosed;
            _win.Unregister();
            _win.Close();
            _win = null;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _settings.Loupe.Visible = false;
        _win = null;
        _persist();
    }

    private void OnPlacementChanged()
    {
        if (_win is null || _win.SelfHwnd == IntPtr.Zero)
            return;

        if (MagnifierNative.GetWindowRect(_win.SelfHwnd, out RECT r) && r.Width > 0 && r.Height > 0)
        {
            LoupeConfig c = _settings.Loupe;
            c.Left = r.Left;
            c.Top = r.Top;
            c.Width = r.Width;
            c.Height = r.Height;
        }

        Refresh(); // a resize changes the destination -> re-push the crop
        _persist();
    }

    /// <summary>Recompute the fixed crop and push a single thumbnail update.</summary>
    public void Refresh()
    {
        if (_win is null || !_settings.Loupe.Visible || _source == IntPtr.Zero || !_win.HasThumb)
            return;

        RECT client = _services.Windows.GetClientBoundsInScreen(_source);
        if (client.Width <= 0 || client.Height <= 0)
        {
            _win.UpdateView(default, 0, false);
            return;
        }

        LoupeConfig c = _settings.Loupe;
        RECT dest = _win.GetHostRectPhysical();
        int destW = Math.Max(1, dest.Width);
        int destH = Math.Max(1, dest.Height);

        double zoom = Clamp(c.Zoom, _settings.ZoomMin, _settings.ZoomMax);
        int srcW = Math.Max(1, (int)Math.Round(destW / zoom));
        int srcH = Math.Max(1, (int)Math.Round(destH / zoom));

        int ccx = (int)Math.Round(Clamp01(c.CenterX) * client.Width);
        int ccy = (int)Math.Round(Clamp01(c.CenterY) * client.Height);

        int left = Clamp(ccx - srcW / 2, 0, Math.Max(0, client.Width - srcW));
        int top = Clamp(ccy - srcH / 2, 0, Math.Max(0, client.Height - srcH));

        _win.UpdateView(new RECT(left, top, left + srcW, top + srcH), c.Opacity, true);
    }

    // ---- helpers ----

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    public void Dispose() => CloseKeepState();
}
