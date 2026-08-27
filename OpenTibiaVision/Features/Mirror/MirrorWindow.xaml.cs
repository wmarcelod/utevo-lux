using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenTibiaVision.Core;
using OpenTibiaVision.Models;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// A borderless, always-on-top window that mirrors a cropped region of a source window using
/// the DWM Thumbnail API — a live compositor copy, zero pixel work on the hot path
/// (optimization principle 1). The crop is measured against the source CLIENT area (the game
/// viewport). Placement/geometry are physical pixels; DPI is converted only here, at the WPF
/// boundary, and re-latched on WM_DPICHANGED via a ScaleGuard.
///
/// Locked = click-through (WS_EX_LAYERED | WS_EX_TRANSPARENT) so it floats over the game;
/// unlocked shows a drag border and can be moved/resized.
/// </summary>
public partial class MirrorWindow : Window
{
    private const double BorderThicknessValue = 2;

    private readonly IAppServices _services;
    private readonly IntPtr _sourceHwnd;
    private readonly RegionConfig _config;

    private RECT _crop; // physical px, client-relative
    private IntPtr _thumb;
    private IntPtr _selfHwnd;
    private ScaleGuard? _scaleGuard;
    private bool _locked;
    private bool _suppressPersist;

    /// <summary>Raised when the user moved/resized or lock changed, so the owner can persist.</summary>
    public event Action? MirrorStateChanged;

    public MirrorWindow(IAppServices services, IntPtr sourceHwnd, RECT crop, RegionConfig config)
    {
        InitializeComponent();
        _services = services;
        _sourceHwnd = sourceHwnd;
        _crop = crop;
        _config = config;

        SizeChanged += (_, _) => OnGeometryChanged();
        LocationChanged += (_, _) => OnGeometryChanged();
    }

    public bool IsLocked => _locked;

    /// <summary>Replace the source crop rectangle (physical px, client-relative) live.</summary>
    public void UpdateCrop(RECT crop)
    {
        _crop = crop;
        UpdateThumbnail();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _selfHwnd = new WindowInteropHelper(this).Handle;

        // Place in PHYSICAL pixels so mixed-DPI monitors land exactly. WPF Left/Top are DIPs
        // and ambiguous across monitors; SetWindowPos is not.
        _suppressPersist = true;
        NativeMethods.SetWindowPos(
            _selfHwnd, NativeMethods.HWND_TOPMOST,
            _config.MirrorLeft, _config.MirrorTop, _config.MirrorWidth, _config.MirrorHeight,
            NativeMethods.SWP_NOACTIVATE);
        _suppressPersist = false;

        RegisterThumbnail();
        ApplyLock(_locked);

        _scaleGuard = new ScaleGuard(this, _services.Dpi);
        _scaleGuard.DpiChanged += _ => UpdateThumbnail();
    }

    protected override void OnClosed(EventArgs e)
    {
        _scaleGuard?.Dispose();
        UnregisterThumbnail();
        base.OnClosed(e);
    }

    // ---- DWM thumbnail lifecycle ----

    private void RegisterThumbnail()
    {
        if (_sourceHwnd == IntPtr.Zero || _selfHwnd == IntPtr.Zero)
            return;

        UnregisterThumbnail();
        _thumb = _services.Dwm.Register(_selfHwnd, _sourceHwnd);
        if (_thumb != IntPtr.Zero)
            UpdateThumbnail();
    }

    private void UnregisterThumbnail()
    {
        if (_thumb != IntPtr.Zero)
        {
            _services.Dwm.Unregister(_thumb);
            _thumb = IntPtr.Zero;
        }
    }

    private void UpdateThumbnail()
    {
        if (_thumb == IntPtr.Zero)
            return;

        // clientAreaOnly:true => rcSource is interpreted in the source's CLIENT space, so the
        // crop (client-relative physical px) maps 1:1 onto the game viewport.
        _services.Dwm.Update(_thumb, GetHostRectPhysical(), _crop,
            opacity: 255, visible: true, clientAreaOnly: true);
    }

    /// <summary>
    /// Host element rect in PHYSICAL px relative to this window's client area — what
    /// rcDestination expects. WPF works in DIPs; scale by this window's monitor DPI.
    /// </summary>
    private RECT GetHostRectPhysical()
    {
        double scale = _services.Dpi.GetScaleForWindow(_selfHwnd);

        Point topLeft = Host.TranslatePoint(new Point(0, 0), this);
        double width = Host.ActualWidth;
        double height = Host.ActualHeight;

        int left = _services.Dpi.ToPhysical(topLeft.X, scale);
        int top = _services.Dpi.ToPhysical(topLeft.Y, scale);
        int right = _services.Dpi.ToPhysical(topLeft.X + width, scale);
        int bottom = _services.Dpi.ToPhysical(topLeft.Y + height, scale);
        return new RECT(left, top, right, bottom);
    }

    // ---- geometry persistence (physical px) ----

    private void OnGeometryChanged()
    {
        UpdateThumbnail();
        if (_suppressPersist || _selfHwnd == IntPtr.Zero)
            return;

        if (NativeMethods.GetWindowRect(_selfHwnd, out RECT r) && r.Width > 0 && r.Height > 0)
        {
            _config.MirrorLeft = r.Left;
            _config.MirrorTop = r.Top;
            _config.MirrorWidth = r.Width;
            _config.MirrorHeight = r.Height;
        }
        MirrorStateChanged?.Invoke();
    }

    // ---- Lock / unlock (click-through) ----

    public void ApplyLock(bool locked)
    {
        _locked = locked;
        if (_selfHwnd == IntPtr.Zero)
            return; // deferred until OnSourceInitialized

        _services.Windows.SetClickThrough(_selfHwnd, locked);
        RootBorder.BorderThickness = new Thickness(locked ? 0 : BorderThicknessValue);
        Topmost = true;

        // Border thickness change resizes Host; refresh destination after layout.
        Dispatcher.BeginInvoke(new Action(UpdateThumbnail), DispatcherPriority.Loaded);
        MirrorStateChanged?.Invoke();
    }

    // ---- Move (drag) in unlocked mode ----

    private void OnBodyMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked || e.ButtonState != MouseButtonState.Pressed)
            return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* button already released */ }
    }
}
