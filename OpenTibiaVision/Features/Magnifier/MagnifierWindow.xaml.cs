using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Magnifier;

/// <summary>
/// A borderless, always-on-top DWM window shared by BOTH magnifier variants:
///   - the follow-cursor lens (interactive == false: always click-through, moved every frame,
///     never persisted), and
///   - the fixed-crop loupe (interactive == true: draggable / resizable when unlocked, placement
///     persisted).
///
/// It owns exactly one DWM thumbnail (register ONCE on source change, then push property updates —
/// the GPU composites, the app copies zero pixels; optimization principle 1). The rounded /
/// circular shape is a GDI window region applied with SetWindowRgn and recomputed only on size /
/// DPI change. All placement is PHYSICAL px; DPI is converted only here, at the WPF boundary.
///
/// DWM CONSTRAINT: a DWM thumbnail shows the SOURCE WINDOW as the compositor holds it — i.e. as if
/// unoccluded. Whatever sits visually on top of the source on screen is NOT reflected in the
/// magnified view; the lens shows the source window's own content for the picked region.
/// </summary>
public partial class MagnifierWindow : Window
{
    private readonly IAppServices _services;
    private readonly bool _interactive;

    private IntPtr _thumb;
    private IntPtr _selfHwnd;
    private ScaleGuard? _scaleGuard;

    private LensShape _shape = LensShape.RoundedRect;
    private double _cornerRadius = 16;
    private double _ringThickness = 2;
    private bool _clickThrough;
    private bool _suppressPersist;

    /// <summary>Raised when the user moved / resized the (interactive) window — persist placement.</summary>
    public event Action? PlacementChanged;

    /// <summary>Raised on size / DPI change — the owner should re-push the view (rect recompute).</summary>
    public event Action? ViewInvalidated;

    public MagnifierWindow(IAppServices services, bool interactive)
    {
        InitializeComponent();
        _services = services;
        _interactive = interactive;

        SizeChanged += OnSizeChanged;
        if (_interactive)
            LocationChanged += OnLocationChanged;
    }

    public IntPtr SelfHwnd => _selfHwnd;
    public bool HasThumb => _thumb != IntPtr.Zero;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _selfHwnd = new WindowInteropHelper(this).Handle;

        // No-activate + tool-window: never steals focus, never in Alt+Tab.
        _services.Windows.SetOverlayChrome(_selfHwnd, true);
        Topmost = true;

        ApplyShapeVisuals();
        ApplyRegion();

        _scaleGuard = new ScaleGuard(this, _services.Dpi);
        _scaleGuard.DpiChanged += _ =>
        {
            ApplyRegion();
            ViewInvalidated?.Invoke();
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _scaleGuard?.Dispose();
        Unregister();
        base.OnClosed(e);
    }

    // ---- DWM thumbnail lifecycle (register once, then push updates) ----

    /// <summary>Point the single thumbnail at a new source (re-registers). False if it failed.</summary>
    public bool SetSource(IntPtr src)
    {
        Unregister();
        if (src == IntPtr.Zero || _selfHwnd == IntPtr.Zero)
            return false;
        _thumb = _services.Dwm.Register(_selfHwnd, src);
        return _thumb != IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_thumb != IntPtr.Zero)
        {
            _services.Dwm.Unregister(_thumb);
            _thumb = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Push one thumbnail update. <paramref name="rcSource"/> is client-relative PHYSICAL px
    /// (clientAreaOnly: it maps 1:1 onto the source's game viewport). rcDestination is derived
    /// from the Host element so it honours the accent ring.
    /// </summary>
    public void UpdateView(RECT rcSource, byte opacity, bool visible)
    {
        if (_thumb == IntPtr.Zero)
            return;
        _services.Dwm.Update(_thumb, GetHostRectPhysical(), rcSource, opacity, visible, clientAreaOnly: true);
    }

    /// <summary>Host element rect in PHYSICAL px relative to this window's client — rcDestination.</summary>
    public RECT GetHostRectPhysical()
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

    // ---- shape / rounded region ----

    public void SetShape(LensShape shape, double cornerRadius, double ringThickness)
    {
        _shape = shape;
        _cornerRadius = cornerRadius;
        _ringThickness = ringThickness;
        if (_selfHwnd != IntPtr.Zero)
        {
            ApplyShapeVisuals();
            ApplyRegion();
        }
    }

    private void ApplyShapeVisuals()
    {
        if (_shape == LensShape.Circle)
        {
            // Borderless: the elliptic region gives a crisp round edge; the ring would be clipped.
            RootBorder.BorderThickness = new Thickness(0);
            RootBorder.CornerRadius = new CornerRadius(0);
        }
        else
        {
            RootBorder.BorderThickness = new Thickness(_ringThickness);
            RootBorder.CornerRadius = new CornerRadius(_cornerRadius);
        }
    }

    private void ApplyRegion()
    {
        if (_selfHwnd == IntPtr.Zero)
            return;
        if (!MagnifierNative.GetWindowRect(_selfHwnd, out RECT r))
            return;

        int w = r.Width, h = r.Height;
        if (w <= 0 || h <= 0)
            return;

        // Region coordinates are physical px relative to the window's upper-left.
        IntPtr rgn = _shape == LensShape.Circle
            ? MagnifierNative.CreateEllipticRgn(0, 0, w, h)
            : MagnifierNative.CreateRoundRectRgn(0, 0, w, h, PhysCornerDiameter(), PhysCornerDiameter());

        if (rgn == IntPtr.Zero)
            return;

        // On success the system takes ownership of the region; on failure we still own it.
        if (MagnifierNative.SetWindowRgn(_selfHwnd, rgn, true) == 0)
            MagnifierNative.DeleteObject(rgn);
    }

    private int PhysCornerDiameter()
    {
        double scale = _services.Dpi.GetScaleForWindow(_selfHwnd);
        // CreateRoundRectRgn takes the corner ellipse's width/height (= 2 * radius).
        return Math.Max(2, (int)Math.Round(_cornerRadius * 2 * scale));
    }

    // ---- placement ----

    /// <summary>Place / size the window in PHYSICAL px (exact on mixed-DPI monitors).</summary>
    public void SetPlacementPhysical(int x, int y, int w, int h)
    {
        if (_selfHwnd == IntPtr.Zero)
            return;
        _suppressPersist = true;
        NativeMethods.SetWindowPos(_selfHwnd, NativeMethods.HWND_TOPMOST, x, y, w, h,
            NativeMethods.SWP_NOACTIVATE);
        _suppressPersist = false;
        // A size change triggers OnSizeChanged -> ApplyRegion. A pure move keeps the region
        // (it is client-relative and moves with the window), so no region work on the hot path.
    }

    // ---- click-through (lock) ----

    public void ApplyClickThrough(bool on)
    {
        _clickThrough = on;
        if (_selfHwnd == IntPtr.Zero)
            return;
        _services.Windows.SetClickThrough(_selfHwnd, on);
        Topmost = true;
        // WS_EX_LAYERED is toggled by click-through; re-assert the shape region afterwards.
        ApplyRegion();
    }

    // ---- events ----

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyRegion();
        if (_interactive && !_suppressPersist)
            PlacementChanged?.Invoke();
        ViewInvalidated?.Invoke();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_suppressPersist)
            PlacementChanged?.Invoke();
    }

    private void OnBodyMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_interactive || _clickThrough || e.ButtonState != MouseButtonState.Pressed)
            return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* button already released */ }
    }
}
