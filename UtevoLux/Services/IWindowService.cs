using System;
using System.Collections.Generic;

namespace UtevoLux.Services;

/// <summary>
/// Discovery + geometry + ex-style toggles for source and overlay windows. Wraps the static
/// <see cref="WindowFinder"/> so feature modules receive it through IAppServices.
/// </summary>
public interface IWindowService
{
    /// <summary>Every visible top-level window with a title (excluding our own is the caller's job).</summary>
    IReadOnlyList<WindowInfo> ListWindows();

    /// <summary>The running Tibia client window, or IntPtr.Zero.</summary>
    IntPtr FindTibia();

    /// <summary>Client (game viewport) bounds in physical screen px. Crop against THIS.</summary>
    RECT GetClientBoundsInScreen(IntPtr hwnd);

    /// <summary>Visible frame bounds in physical px (title bar + borders included).</summary>
    RECT GetSourceBounds(IntPtr hwnd);

    /// <summary>Mouse click-through (WS_EX_LAYERED | WS_EX_TRANSPARENT).</summary>
    void SetClickThrough(IntPtr hwnd, bool on);

    /// <summary>No-activate + tool-window chrome for overlays/toasts.</summary>
    void SetOverlayChrome(IntPtr hwnd, bool on);
}

/// <summary>Default implementation delegating to <see cref="WindowFinder"/>.</summary>
public sealed class WindowService : IWindowService
{
    public IReadOnlyList<WindowInfo> ListWindows() => WindowFinder.ListWindows();
    public IntPtr FindTibia() => WindowFinder.FindTibia();
    public RECT GetClientBoundsInScreen(IntPtr hwnd) => WindowFinder.GetClientBoundsInScreen(hwnd);
    public RECT GetSourceBounds(IntPtr hwnd) => DwmThumbnail.GetSourceBounds(hwnd);
    public void SetClickThrough(IntPtr hwnd, bool on) => WindowFinder.SetClickThrough(hwnd, on);
    public void SetOverlayChrome(IntPtr hwnd, bool on) => WindowFinder.SetOverlayChrome(hwnd, on);
}
