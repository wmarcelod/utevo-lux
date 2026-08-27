using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using UtevoLux.Core;
using UtevoLux.Models;
using UtevoLux.Services;
using UtevoLux.UI;

namespace UtevoLux.Features.Mirror;

/// <summary>
/// A borderless, always-on-top window that mirrors a cropped region of a source window using the
/// DWM Thumbnail API — a live compositor copy, zero pixel work on the hot path (principle 1). The
/// crop is measured against the source CLIENT area (the game viewport). Geometry is physical px;
/// DPI is converted only here, at the WPF boundary, and re-latched on WM_DPICHANGED via ScaleGuard.
///
/// UX parity (this pass):
///  - Wheel zoom: shrink a centered rcSource (0.5..4x) — one property push per notch, no pixels.
///  - Opacity: pushed with the OPACITY-only DWM flag (no rect recompute).
///  - Aspect-locked corner resize: WM_SIZING adjusts the OS drag rect (SetWindowPos-driven, no
///    WPF layout lag) so Shift keeps the crop aspect.
///  - Right-click passthrough: WM_RBUTTONDOWN/UP remapped through the SAME rcSource transform DWM
///    uses, then PostMessage'd to the source (raw cursor is only correct at 1:1).
///  - Single context menu (right-click when passthrough off, middle-click always).
///  - Auto show/hide bound to the source (driven by SourceWindowWatcher).
/// </summary>
public partial class MirrorWindow : Window
{
    private const double BorderThicknessValue = 2;
    private const double ZoomNotch = 1.15;
    private const uint WM_SIZING = 0x0214;
    private const uint WM_MBUTTONUP = 0x0208;

    private readonly IAppServices _services;
    private readonly IntPtr _sourceHwnd;
    private readonly RegionConfig _config;
    private readonly MirrorUxState _ux;

    private RECT _crop;            // physical px, client-relative (base crop before zoom)
    private RECT _currentSource;   // cached zoomed rcSource (for the passthrough transform)
    private IntPtr _thumb;
    private IntPtr _selfHwnd;
    private HwndSource? _hwndSource;
    private ScaleGuard? _scaleGuard;
    private bool _locked;
    private bool _suppressPersist;

    /// <summary>Raised when the user moved/resized or lock changed, so the owner can persist geometry.</summary>
    public event Action? MirrorStateChanged;

    /// <summary>Raised when zoom/opacity changed here (wheel/menu), so the owner saves + refreshes.</summary>
    public event Action? UxChanged;

    public event Action? RecropDragRequested;
    public event Action? RecropLoupeRequested;
    public event Action? NewCropRequested;
    public event Action? RemoveRequested;
    public event Action? HideRequested;
    public event Action? LockToggleRequested;
    public event Action<bool>? PassthroughToggled;
    public event Action<bool>? AutoHideToggled;

    public MirrorWindow(IAppServices services, IntPtr sourceHwnd, RegionConfig config, MirrorUxState ux)
    {
        InitializeComponent();
        _services = services;
        _sourceHwnd = sourceHwnd;
        _config = config;
        _ux = ux;
        _ux.ClampZoom();
        _crop = CropFromConfig();

        SizeChanged += (_, _) => OnGeometryChanged();
        LocationChanged += (_, _) => OnGeometryChanged();
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    public bool IsLocked => _locked;
    public double Zoom => _ux.Zoom;

    private RECT CropFromConfig()
        => new RECT(_config.CropLeft, _config.CropTop, _config.CropRight, _config.CropBottom);

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

        // Place in PHYSICAL pixels so mixed-DPI monitors land exactly.
        _suppressPersist = true;
        NativeMethods.SetWindowPos(
            _selfHwnd, NativeMethods.HWND_TOPMOST,
            _config.MirrorLeft, _config.MirrorTop, _config.MirrorWidth, _config.MirrorHeight,
            NativeMethods.SWP_NOACTIVATE);
        _suppressPersist = false;

        _hwndSource = HwndSource.FromHwnd(_selfHwnd);
        _hwndSource?.AddHook(WndProc);

        RegisterThumbnail();
        ApplyLock(_locked);

        _scaleGuard = new ScaleGuard(this, _services.Dpi);
        _scaleGuard.DpiChanged += _ => UpdateThumbnail();
    }

    protected override void OnClosed(EventArgs e)
    {
        _scaleGuard?.Dispose();
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
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

        // Client bounds are only needed to clamp a zoomed-out (zoom < 1) rcSource in-bounds.
        RECT client = _services.Windows.GetClientBoundsInScreen(_sourceHwnd);
        _currentSource = MirrorCoordinateMapper.ComputeZoomedSource(_crop, _ux.Zoom, client.Width, client.Height);

        // clientAreaOnly:true => rcSource is interpreted in the source's CLIENT space.
        _services.Dwm.Update(_thumb, GetHostRectPhysical(), _currentSource,
            opacity: _ux.Opacity, visible: true, clientAreaOnly: true);
    }

    /// <summary>Host element rect in PHYSICAL px relative to this window's client area (rcDestination).</summary>
    private RECT GetHostRectPhysical()
    {
        double scale = _services.Dpi.GetScaleForWindow(_selfHwnd);

        Point topLeft = Host.TranslatePoint(new Point(0, 0), this);
        int left = _services.Dpi.ToPhysical(topLeft.X, scale);
        int top = _services.Dpi.ToPhysical(topLeft.Y, scale);
        int right = _services.Dpi.ToPhysical(topLeft.X + Host.ActualWidth, scale);
        int bottom = _services.Dpi.ToPhysical(topLeft.Y + Host.ActualHeight, scale);
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

    // ---- Lock / passthrough (click-through) ----

    public void ApplyLock(bool locked)
    {
        _locked = locked;
        if (_selfHwnd == IntPtr.Zero)
            return; // deferred until OnSourceInitialized

        // Click-through ONLY when locked and NOT passing right-clicks through: a passthrough
        // mirror must stay hit-testable to receive WM_RBUTTONDOWN/UP.
        bool clickThrough = locked && !_ux.RightClickPassthrough;
        _services.Windows.SetClickThrough(_selfHwnd, clickThrough);

        // In passthrough mode the window stays hit-testable but must NOT steal focus from the
        // game when right-clicked (no-activate + tool-window). Normal modes keep default chrome
        // so unlocked drag behaves as before.
        _services.Windows.SetOverlayChrome(_selfHwnd, _ux.RightClickPassthrough);

        RootBorder.BorderThickness = new Thickness(locked ? 0 : BorderThicknessValue);
        Topmost = true;

        Dispatcher.BeginInvoke(new Action(UpdateThumbnail), DispatcherPriority.Loaded);
        MirrorStateChanged?.Invoke();
    }

    /// <summary>Toggle right-click passthrough; re-applies lock so click-through state stays correct.</summary>
    public void SetPassthrough(bool on)
    {
        _ux.RightClickPassthrough = on;
        ApplyLock(_locked);
    }

    // ---- zoom / opacity ----

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? ZoomNotch : 1.0 / ZoomNotch;
        SetZoom(_ux.Zoom * factor);
        e.Handled = true;
    }

    public void SetZoom(double zoom)
    {
        _ux.Zoom = Math.Clamp(zoom, MirrorUxState.MinZoom, MirrorUxState.MaxZoom);
        UpdateThumbnail();
        UxChanged?.Invoke();
    }

    public void ResetZoom() => SetZoom(1.0);

    public void SetOpacity(byte opacity)
    {
        _ux.Opacity = Math.Max(MirrorUxState.MinOpacity, opacity);
        // OPACITY-only fast path: one byte, no rect recompute (principle 1).
        _services.Dwm.SetOpacity(_thumb, _ux.Opacity);
        UxChanged?.Invoke();
    }

    // ---- scale% (window size relative to the crop's native pixels) ----

    /// <summary>Resize the window to a physical size, pushing SetWindowPos so the OS applies it
    /// immediately even if a WPF layout pass would lag; SizeChanged then persists + repaints.</summary>
    public void SetWindowSizePhysical(int width, int height)
    {
        if (_selfHwnd == IntPtr.Zero)
            return;
        width = Math.Max(40, width);
        height = Math.Max(30, height);
        NativeMethods.SetWindowPos(
            _selfHwnd, IntPtr.Zero, 0, 0, width, height,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    // ---- auto show/hide ----

    /// <summary>Show/hide the window with the source's presence WITHOUT touching persisted Visible.</summary>
    public void SetSourcePresence(bool present)
    {
        if (present)
        {
            if (!IsVisible)
            {
                Show();
                Topmost = true;
            }
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    // ---- Move (drag) in unlocked mode ----

    private void OnBodyMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked || e.ButtonState != MouseButtonState.Pressed)
            return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* button already released */ }
    }

    // ---- Win32 message hook: passthrough + middle-click menu + aspect-lock resize ----

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch ((uint)msg)
        {
            case MirrorInterop.WM_RBUTTONDOWN:
                if (_ux.RightClickPassthrough)
                {
                    ForwardRightButton(MirrorInterop.WM_RBUTTONDOWN, lParam, down: true);
                    handled = true;
                }
                else
                {
                    handled = true; // swallow; the menu opens on button-up
                }
                break;

            case MirrorInterop.WM_RBUTTONUP:
                if (_ux.RightClickPassthrough)
                {
                    ForwardRightButton(MirrorInterop.WM_RBUTTONUP, lParam, down: false);
                }
                else
                {
                    OpenContextMenu();
                }
                handled = true;
                break;

            case WM_MBUTTONUP:
                OpenContextMenu();
                handled = true;
                break;

            case WM_SIZING:
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    ConstrainAspect(wParam, lParam);
                break;
        }
        return IntPtr.Zero;
    }

    private void ForwardRightButton(uint message, IntPtr lParam, bool down)
    {
        if (_sourceHwnd == IntPtr.Zero)
            return;

        int mx = MirrorInterop.LoWordSigned(lParam);
        int my = MirrorInterop.HiWordSigned(lParam);

        if (!MirrorCoordinateMapper.TryMapMirrorPointToSourceClient(
                mx, my, GetHostRectPhysical(), _currentSource, out int sx, out int sy))
            return;

        // Move the physical cursor to the mapped point (in SCREEN px) BEFORE posting: PostMessage
        // only fills the message's client coords, so a game that reads GetCursorPos / raw input would
        // otherwise see the cursor still parked over the mirror and mislocate the click. sx/sy are
        // source-CLIENT physical px; ClientToScreen converts them to physical screen px for SetCursorPos
        // (both correct under our Per-Monitor-v2 awareness).
        var screenPt = new NativeMethods.POINT { X = sx, Y = sy };
        if (NativeMethods.ClientToScreen(_sourceHwnd, ref screenPt))
            MirrorInterop.SetCursorPos(screenPt.X, screenPt.Y);

        IntPtr w = down ? new IntPtr(MirrorInterop.MK_RBUTTON) : IntPtr.Zero;
        MirrorInterop.PostMessageW(_sourceHwnd, message, w, MirrorInterop.MakeLParam(sx, sy));
    }

    /// <summary>Adjust the OS drag rect (screen px) so the window keeps the crop's aspect ratio.</summary>
    private void ConstrainAspect(IntPtr wParam, IntPtr lParam)
    {
        int cropW = Math.Max(1, _crop.Width);
        int cropH = Math.Max(1, _crop.Height);
        double aspect = cropW / (double)cropH;

        var r = Marshal.PtrToStructure<RECT>(lParam);
        int width = r.Width;
        int newHeight = Math.Max(1, (int)Math.Round(width / aspect));

        // wParam edge codes: 3=TOP,4=TOPLEFT,5=TOPRIGHT anchor the bottom; others anchor the top.
        int edge = wParam.ToInt32();
        bool anchorBottom = edge == 3 || edge == 4 || edge == 5;
        if (anchorBottom)
            r.Top = r.Bottom - newHeight;
        else
            r.Bottom = r.Top + newHeight;

        Marshal.StructureToPtr(r, lParam, false);
    }

    // ---- single context menu ----

    public void OpenContextMenu()
    {
        ContextMenu menu = BuildContextMenu();
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private ContextMenu BuildContextMenu()
    {
        var bg = ThemeAccess.Brush("SurfaceAltBrush", "#FF232833");
        var fg = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");

        var menu = new ContextMenu { Background = bg, Foreground = fg };

        menu.Items.Add(Item($"Zoom +  ({_ux.Zoom * 100:0}%)", () => SetZoom(_ux.Zoom * ZoomNotch), fg));
        menu.Items.Add(Item("Zoom -", () => SetZoom(_ux.Zoom / ZoomNotch), fg));
        menu.Items.Add(Item("Zoom 100%", ResetZoom, fg));
        menu.Items.Add(new Separator());

        var opacity = new MenuItem { Header = "Opacidade", Foreground = fg, Background = bg };
        foreach (int pct in new[] { 100, 75, 50, 25 })
            opacity.Items.Add(Item($"{pct}%", () => SetOpacity((byte)Math.Round(pct * 2.55)), fg));
        menu.Items.Add(opacity);

        var scale = new MenuItem { Header = "Escala", Foreground = fg, Background = bg };
        foreach (int pct in new[] { 50, 75, 100, 150, 200 })
        {
            int p = pct;
            scale.Items.Add(Item($"{p}%", () => ApplyScalePercent(p), fg));
        }
        menu.Items.Add(scale);
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Refazer recorte (arrastar)", () => RecropDragRequested?.Invoke(), fg));
        menu.Items.Add(Item("Refazer recorte (loupe)", () => RecropLoupeRequested?.Invoke(), fg));
        menu.Items.Add(Item("Novo espelho desta fonte", () => NewCropRequested?.Invoke(), fg));
        menu.Items.Add(new Separator());

        menu.Items.Add(Check("Passagem de clique direito", _ux.RightClickPassthrough,
            on => PassthroughToggled?.Invoke(on), fg));
        menu.Items.Add(Check("Auto-ocultar com a fonte", _ux.AutoHide,
            on => AutoHideToggled?.Invoke(on), fg));
        menu.Items.Add(Check("Travar", _locked, _ => LockToggleRequested?.Invoke(), fg));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Ocultar", () => HideRequested?.Invoke(), fg));
        menu.Items.Add(Item("Remover", () => RemoveRequested?.Invoke(), fg));
        return menu;
    }

    private void ApplyScalePercent(int percent)
    {
        int width = Math.Max(1, (int)Math.Round(_crop.Width * percent / 100.0));
        int height = Math.Max(1, (int)Math.Round(_crop.Height * percent / 100.0));
        SetWindowSizePhysical(width, height);
    }

    private static MenuItem Item(string header, Action action, System.Windows.Media.Brush fg)
    {
        var mi = new MenuItem { Header = header, Foreground = fg };
        mi.Click += (_, _) => action();
        return mi;
    }

    private static MenuItem Check(string header, bool isChecked, Action<bool> action,
        System.Windows.Media.Brush fg)
    {
        var mi = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked, Foreground = fg };
        mi.Click += (_, _) => action(mi.IsChecked);
        return mi;
    }
}
