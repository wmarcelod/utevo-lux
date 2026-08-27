using System;
using System.Windows;
using System.Windows.Interop;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Features.Mirror;

/// <summary>
/// A transparent, click-through crosshair drawn on top of the <see cref="LoupeWindow"/>. It is a
/// distinct window because DWM composites the loupe's live thumbnail over any WPF content inside
/// the loupe itself; a sibling window above it in the z-order is the only place the crosshair
/// stays visible. Moved in lockstep with the loupe (positioned LAST so it stays on top).
/// </summary>
public partial class LoupeReticle : Window
{
    private readonly IAppServices _services;
    private IntPtr _selfHwnd;

    public LoupeReticle(IAppServices services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _selfHwnd = new WindowInteropHelper(this).Handle;
        _services.Windows.SetOverlayChrome(_selfHwnd, true);
        _services.Windows.SetClickThrough(_selfHwnd, true);
    }

    public void MoveTo(RECT rectPhysical)
    {
        if (_selfHwnd == IntPtr.Zero)
            return;

        NativeMethods.SetWindowPos(
            _selfHwnd, NativeMethods.HWND_TOPMOST,
            rectPhysical.Left, rectPhysical.Top, rectPhysical.Width, rectPhysical.Height,
            NativeMethods.SWP_NOACTIVATE);
    }
}
