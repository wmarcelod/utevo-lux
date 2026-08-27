using System;
using System.Windows;
using System.Windows.Interop;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Features.Mirror;

/// <summary>
/// The crop loupe: a second, opaque DWM-thumbnail window that magnifies the source (~4x) around
/// the cursor while picking a crop. Register the thumbnail ONCE; every cursor move pushes one
/// DWM_THUMBNAIL_PROPERTIES with a small centered rcSource (principle 1 — zero pixel work). No
/// activation, tool-window, click-through, so it never intercepts the pick overlay's mouse.
/// </summary>
public partial class LoupeWindow : Window
{
    private readonly IAppServices _services;
    private readonly IntPtr _sourceHwnd;

    private IntPtr _thumb;
    private IntPtr _selfHwnd;

    public LoupeWindow(IAppServices services, IntPtr sourceHwnd)
    {
        InitializeComponent();
        _services = services;
        _sourceHwnd = sourceHwnd;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _selfHwnd = new WindowInteropHelper(this).Handle;

        // No-activate + tool + click-through: the loupe is a passive display; all input belongs
        // to the pick overlay beneath it.
        _services.Windows.SetOverlayChrome(_selfHwnd, true);
        _services.Windows.SetClickThrough(_selfHwnd, true);

        if (_sourceHwnd != IntPtr.Zero)
        {
            _thumb = _services.Dwm.Register(_selfHwnd, _sourceHwnd);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_thumb != IntPtr.Zero)
        {
            _services.Dwm.Unregister(_thumb);
            _thumb = IntPtr.Zero;
        }
        base.OnClosed(e);
    }

    /// <summary>
    /// Position the loupe (physical px) and show <paramref name="sourceBox"/> (source client px)
    /// magnified to fill it. Called on every cursor move while picking.
    /// </summary>
    public void Update(RECT windowRectPhysical, RECT sourceBox)
    {
        if (_selfHwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            _selfHwnd, NativeMethods.HWND_TOPMOST,
            windowRectPhysical.Left, windowRectPhysical.Top,
            windowRectPhysical.Width, windowRectPhysical.Height,
            NativeMethods.SWP_NOACTIVATE);

        if (_thumb == IntPtr.Zero)
            return;

        _services.Dwm.Update(_thumb, HostRectPhysical(), sourceBox,
            opacity: 255, visible: true, clientAreaOnly: true);
    }

    private RECT HostRectPhysical()
    {
        double scale = _services.Dpi.GetScaleForWindow(_selfHwnd);
        Point topLeft = Host.TranslatePoint(new Point(0, 0), this);
        int left = _services.Dpi.ToPhysical(topLeft.X, scale);
        int top = _services.Dpi.ToPhysical(topLeft.Y, scale);
        int right = _services.Dpi.ToPhysical(topLeft.X + Host.ActualWidth, scale);
        int bottom = _services.Dpi.ToPhysical(topLeft.Y + Host.ActualHeight, scale);
        return new RECT(left, top, right, bottom);
    }
}
