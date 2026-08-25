using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Views;

/// <summary>
/// A borderless, always-on-top window that mirrors a cropped region of a source window
/// using the DWM Thumbnail API. The mirror is a live compositor copy - no pixel grabbing.
///
/// Locked mode makes the window click-through (WS_EX_LAYERED | WS_EX_TRANSPARENT) so it
/// floats over the game without intercepting input; unlocked mode shows a drag border and
/// allows moving (DragMove) and resizing (WindowChrome edges).
/// </summary>
public partial class MirrorWindow : Window
{
    private const double BorderThicknessValue = 2;

    private readonly IntPtr _sourceHwnd;
    private RECT _crop;
    private IntPtr _thumb;
    private IntPtr _selfHwnd;
    private bool _locked;

    /// <summary>Raised when the user moved/resized or lock state changed, so the owner can persist.</summary>
    public event Action? MirrorStateChanged;

    public MirrorWindow(IntPtr sourceHwnd, RECT crop)
    {
        InitializeComponent();
        _sourceHwnd = sourceHwnd;
        _crop = crop;

        SizeChanged += (_, _) =>
        {
            UpdateThumbnail();
            MirrorStateChanged?.Invoke();
        };
        LocationChanged += (_, _) =>
        {
            // Destination rect is client-relative so it does not change on move, but the
            // spec asks us to refresh on move too, and we persist the new position.
            UpdateThumbnail();
            MirrorStateChanged?.Invoke();
        };
    }

    public bool IsLocked => _locked;

    /// <summary>Replace the source crop rectangle (physical px, source-relative) live.</summary>
    public void UpdateCrop(RECT crop)
    {
        _crop = crop;
        UpdateThumbnail();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _selfHwnd = new WindowInteropHelper(this).Handle;
        RegisterThumbnail();
        ApplyLock(_locked); // re-apply if lock was requested before the HWND existed
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterThumbnail();
        base.OnClosed(e);
    }

    // ---- DWM thumbnail lifecycle ----

    private void RegisterThumbnail()
    {
        if (_sourceHwnd == IntPtr.Zero || _selfHwnd == IntPtr.Zero)
            return;

        UnregisterThumbnail();

        int hr = DwmThumbnail.DwmRegisterThumbnail(_selfHwnd, _sourceHwnd, out _thumb);
        if (hr == 0)
            UpdateThumbnail();
        else
            _thumb = IntPtr.Zero;
    }

    private void UnregisterThumbnail()
    {
        if (_thumb != IntPtr.Zero)
        {
            DwmThumbnail.DwmUnregisterThumbnail(_thumb);
            _thumb = IntPtr.Zero;
        }
    }

    private void UpdateThumbnail()
    {
        if (_thumb == IntPtr.Zero)
            return;

        RECT destination = GetHostRectPhysical();

        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DwmThumbnail.DWM_TNP_RECTDESTINATION |
                      DwmThumbnail.DWM_TNP_RECTSOURCE |
                      DwmThumbnail.DWM_TNP_OPACITY |
                      DwmThumbnail.DWM_TNP_VISIBLE,
            rcDestination = destination,
            rcSource = _crop,
            opacity = 255,
            fVisible = true,
            // Crop is measured against the visible frame bounds, so we mirror the whole
            // (visible) window, not just the client area.
            fSourceClientAreaOnly = false
        };

        DwmThumbnail.DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    /// <summary>
    /// Host element rectangle expressed in PHYSICAL pixels relative to this window's client
    /// area - which is what DWM_THUMBNAIL_PROPERTIES.rcDestination expects. WPF works in
    /// DIPs, so we scale by the window's DPI (GetDpiForWindow).
    /// </summary>
    private RECT GetHostRectPhysical()
    {
        double scale = NativeMethods.GetScaleForWindow(_selfHwnd);

        // Host offset within the window (accounts for the drag border) and its size, in DIPs.
        Point topLeft = Host.TranslatePoint(new Point(0, 0), this);
        double width = Host.ActualWidth;
        double height = Host.ActualHeight;

        int left = (int)Math.Round(topLeft.X * scale);
        int top = (int)Math.Round(topLeft.Y * scale);
        int right = (int)Math.Round((topLeft.X + width) * scale);
        int bottom = (int)Math.Round((topLeft.Y + height) * scale);

        return new RECT(left, top, right, bottom);
    }

    // ---- Lock / unlock (click-through) ----

    public void ApplyLock(bool locked)
    {
        _locked = locked;

        if (_selfHwnd == IntPtr.Zero)
            return; // deferred until OnSourceInitialized

        long exStyle = NativeMethods.GetWindowLongEx(_selfHwnd, NativeMethods.GWL_EXSTYLE);

        if (locked)
        {
            exStyle |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLongEx(_selfHwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            // A window that just became WS_EX_LAYERED can render blank until its layer
            // attributes are set; force fully opaque. The DWM thumbnail still composites.
            NativeMethods.SetLayeredWindowAttributes(_selfHwnd, 0, 255, NativeMethods.LWA_ALPHA);
            RootBorder.BorderThickness = new Thickness(0);
        }
        else
        {
            exStyle &= ~(NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT);
            NativeMethods.SetWindowLongEx(_selfHwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            RootBorder.BorderThickness = new Thickness(BorderThicknessValue);
        }

        Topmost = true;

        // Border thickness change resizes Host; refresh the destination rect after layout.
        Dispatcher.BeginInvoke(new Action(UpdateThumbnail), DispatcherPriority.Loaded);
        MirrorStateChanged?.Invoke();
    }

    // ---- Move (drag) in unlocked mode ----

    private void OnBodyMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_locked)
            return;

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove throws if the button was already released; ignore.
            }
        }
    }
}
