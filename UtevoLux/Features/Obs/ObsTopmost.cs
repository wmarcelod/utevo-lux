using System;
using UtevoLux.Services;

namespace UtevoLux.Features.Obs;

/// <summary>
/// The one piece of behavior that makes an OBS mirror different from a normal mirror: an AGGRESSIVE
/// always-on-top re-assert that beats a capture tool's z-order. Ported 1:1 from the original
/// TibiaVision <c>WindowHelper.SetWindowAlwaysOnTopAggressive</c> — a capture/projector window
/// constantly fights for the top of the topmost band, so a plain <c>Topmost=true</c> is not enough;
/// the mirror must slam itself back to the very top periodically (see <see cref="ObsMirrorWindow"/>'s
/// ~2s timer).
///
/// Built entirely on the shared internal <see cref="NativeMethods"/> (same assembly) so no P/Invoke
/// is duplicated — only the two constants Win32 needs here that the shared file does not already carry.
/// </summary>
internal static class ObsTopmost
{
    // HWND_TOP == 0: the top of the NON-topmost band. The original pokes topmost -> top -> topmost
    // as a single sequence; the intermediate HWND_TOP nudge forces a real z-order recomputation
    // instead of the no-op the window manager may apply for topmost -> topmost.
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;

    // WS_EX_TOPMOST: re-set explicitly because a capture tool can strip it off our window.
    private const long WS_EX_TOPMOST = 0x00000008;

    // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE (== 0x13, the original's literal "19u"): re-order
    // z only — never move, resize, or steal focus from the game.
    private const uint ReassertFlags =
        NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE;

    /// <summary>
    /// Slam <paramref name="hwnd"/> to the very top of the topmost band (topmost -> top -> topmost)
    /// and re-apply WS_EX_TOPMOST. No-op when the window is hidden (nothing to fight for). Call once
    /// on show and again on every re-assert tick.
    /// </summary>
    public static void SetAlwaysOnTopAggressive(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd))
            return;

        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, ReassertFlags);
        NativeMethods.SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, ReassertFlags);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, ReassertFlags);

        long ex = NativeMethods.GetWindowLongEx(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongEx(hwnd, NativeMethods.GWL_EXSTYLE, ex | WS_EX_TOPMOST);
    }
}
