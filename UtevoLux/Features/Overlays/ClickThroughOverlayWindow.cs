using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Features.Overlays;

/// <summary>
/// Shared base for every floating HUD overlay (notes, grid, cursor-glow, marker).
///
/// Chrome (constant, set once):
///   WindowStyle=None + AllowsTransparency=True (per-pixel alpha, so rounded/transparent
///   content composites correctly) + Topmost + ShowInTaskbar=False + ShowActivated=False.
///   At HWND creation we add WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW (via IWindowService) so the
///   overlay NEVER steals focus from the game and never shows in Alt+Tab.
///
/// Lock (runtime, the one hot toggle):
///   locked   => click-through: add WS_EX_TRANSPARENT so the mouse falls through to the game.
///   unlocked => interactive: clear WS_EX_TRANSPARENT; the overlay can be dragged (and derived
///               types may add a resize grip) and shows its selection border.
///   We toggle ONLY WS_EX_TRANSPARENT (never SetLayeredWindowAttributes) so WPF keeps managing
///   the per-pixel alpha of an AllowsTransparency window — the Toast's LWA path would flatten
///   our transparency.
///
/// Geometry is PHYSICAL pixels throughout (optimization principle 8): placement uses
/// SetWindowPos, read-back uses GetWindowRect, and drag is tracked with GetCursorPos — all in
/// physical space, so mixed-DPI monitors land exactly. DPI changes are latched via ScaleGuard.
///
/// Because the window is WS_EX_NOACTIVATE it cannot take keyboard focus; therefore overlays are
/// display + reposition surfaces only. All text/color/font editing happens in the module's
/// dashboard page (in the shell), never in the floating window.
/// </summary>
public abstract class ClickThroughOverlayWindow : Window
{
    protected IAppServices Services { get; }

    private IntPtr _hwnd;
    private ScaleGuard? _scaleGuard;
    private bool _locked = true;

    // Physical-pixel manual drag (robust on a no-activate window, and DPI-exact).
    private bool _dragging;
    private NativeMethods.POINT _dragAnchorCursor;
    private RECT _dragAnchorBounds;

    /// <summary>Raised (UI thread) after the user moved/resized the overlay or lock changed.</summary>
    public event Action? OverlayStateChanged;

    protected ClickThroughOverlayWindow(IAppServices services)
    {
        Services = services;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public bool IsLocked => _locked;

    /// <summary>Whether this overlay can be repositioned by dragging when unlocked.</summary>
    protected virtual bool Draggable => true;

    /// <summary>Placement (physical px) applied once when the HWND is first created.</summary>
    protected abstract RECT InitialPlacementPhysical { get; }

    /// <summary>Derived hook: react to lock changes (toggle selection border / edit affordances).</summary>
    protected virtual void OnLockChanged(bool locked) { }

    /// <summary>Derived hook: the monitor scale changed (recompute physical geometry if needed).</summary>
    protected virtual void OnScaleChanged(double scale) { }

    /// <summary>The overlay's HWND (valid after it is shown).</summary>
    protected IntPtr Handle => _hwnd;

    /// <summary>Scale of the monitor this overlay currently sits on (cached, DPI-latched).</summary>
    protected double CurrentScale => _hwnd == IntPtr.Zero ? 1.0 : Services.Dpi.GetScaleForWindow(_hwnd);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        if (_hwnd == IntPtr.Zero)
            return;

        RECT p = InitialPlacementPhysical;
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            p.Left, p.Top, Math.Max(1, p.Width), Math.Max(1, p.Height),
            NativeMethods.SWP_NOACTIVATE);

        // Never activate, never in Alt+Tab.
        Services.Windows.SetOverlayChrome(_hwnd, true);

        // Apply the current lock state now that the HWND exists.
        ApplyLock(_locked);

        _scaleGuard = new ScaleGuard(this, Services.Dpi);
        _scaleGuard.DpiChanged += OnScaleChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        _scaleGuard?.Dispose();
        _scaleGuard = null;
        base.OnClosed(e);
    }

    // ---- lock / click-through ----

    public void ApplyLock(bool locked)
    {
        _locked = locked;
        if (_hwnd != IntPtr.Zero)
            WindowFinder.SetExStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT, locked);
        OnLockChanged(locked);
        OverlayStateChanged?.Invoke();
    }

    // ---- physical-pixel geometry ----

    /// <summary>Current window bounds in physical screen px (empty if not yet shown).</summary>
    public RECT GetBoundsPhysical()
        => _hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(_hwnd, out RECT r) ? r : default;

    /// <summary>Move/resize the window in physical px. Pass <paramref name="keepSize"/> to move only.</summary>
    public void SetBoundsPhysical(int left, int top, int width, int height, bool keepSize = false)
    {
        if (_hwnd == IntPtr.Zero)
            return;
        uint flags = NativeMethods.SWP_NOACTIVATE;
        if (keepSize)
            flags |= NativeMethods.SWP_NOSIZE;
        NativeMethods.SetWindowPos(
            _hwnd, NativeMethods.HWND_TOPMOST,
            left, top, Math.Max(1, width), Math.Max(1, height), flags);
    }

    // ---- manual drag (unlocked) ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // A derived affordance (e.g. resize grip) that handled the event opts out of window drag.
        if (_locked || !Draggable || e.Handled)
            return;
        if (!OverlayNative.GetCursorPos(out _dragAnchorCursor))
            return;

        _dragAnchorBounds = GetBoundsPhysical();
        _dragging = CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || !OverlayNative.GetCursorPos(out NativeMethods.POINT c))
            return;

        int left = _dragAnchorBounds.Left + (c.X - _dragAnchorCursor.X);
        int top = _dragAnchorBounds.Top + (c.Y - _dragAnchorCursor.Y);
        SetBoundsPhysical(left, top, _dragAnchorBounds.Width, _dragAnchorBounds.Height, keepSize: true);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
            return;
        _dragging = false;
        ReleaseMouseCapture();
        OverlayStateChanged?.Invoke(); // persist the new physical position
        e.Handled = true;
    }

    /// <summary>Derived resize grips call this after a resize to persist and refresh.</summary>
    protected void RaiseStateChanged() => OverlayStateChanged?.Invoke();
}
