using System;
using System.Runtime.InteropServices;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Overlays;

/// <summary>
/// The few extra Win32 P/Invokes the overlays need on top of the shared
/// <see cref="NativeMethods"/> surface. Kept local to Features\Overlays so the feature is
/// self-contained (no shared/foundation file is edited). All are documented user32 functions.
/// </summary>
internal static class OverlayNative
{
    /// <summary>Live cursor position in PHYSICAL screen pixels (virtual-desktop origin).</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out NativeMethods.POINT lpPoint);
}
