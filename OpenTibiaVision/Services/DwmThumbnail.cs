using System;
using System.Runtime.InteropServices;

namespace OpenTibiaVision.Services;

// Physical-pixel rectangle. Matches the Win32 RECT layout (left, top, right, bottom).
[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct SIZE
{
    public int cx;
    public int cy;
}

// Mirrors the documented DWM_THUMBNAIL_PROPERTIES structure. fVisible /
// fSourceClientAreaOnly are Win32 BOOL (4 bytes), so they are marshalled as such.
[StructLayout(LayoutKind.Sequential)]
public struct DWM_THUMBNAIL_PROPERTIES
{
    public uint dwFlags;
    public RECT rcDestination;
    public RECT rcSource;
    public byte opacity;
    [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
    [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
}

/// <summary>
/// Thin wrapper over the documented DWM Thumbnail API (dwmapi.dll).
///
/// The DWM thumbnail feature asks the Desktop Window Manager to composite a LIVE copy of
/// a source window (optionally cropped via rcSource) into a rectangle of a destination
/// window that the calling process owns. It is a pure compositor mirror: it never reads
/// process memory and never grabs pixels itself. This is the same mechanism the taskbar
/// uses for its hover previews.
/// </summary>
public static class DwmThumbnail
{
    // dwFlags bits for DWM_THUMBNAIL_PROPERTIES.
    public const uint DWM_TNP_RECTDESTINATION = 0x00000001;
    public const uint DWM_TNP_RECTSOURCE = 0x00000002;
    public const uint DWM_TNP_OPACITY = 0x00000004;
    public const uint DWM_TNP_VISIBLE = 0x00000008;
    public const uint DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

    // dwAttribute value for DwmGetWindowAttribute: the true visible frame bounds in
    // physical screen pixels. Unlike GetWindowRect, this excludes the invisible resize
    // borders Windows 10/11 add, so it aligns with the DWM thumbnail's own source origin.
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>Register hwndSource to be drawn into hwndDestination (owned by this process).</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

    /// <summary>Release a thumbnail relationship created by DwmRegisterThumbnail.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmUnregisterThumbnail(IntPtr thumb);

    /// <summary>Apply destination/source rects, opacity and visibility to a thumbnail.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmUpdateThumbnailProperties(IntPtr hThumb, ref DWM_THUMBNAIL_PROPERTIES props);

    /// <summary>Size (physical px) of the source content that would be shown at full scale.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmQueryThumbnailSourceSize(IntPtr hThumb, out SIZE size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    /// <summary>
    /// Returns the source window's visible bounds in physical screen pixels. Prefers the
    /// DWM extended frame bounds (excludes invisible borders); falls back to GetWindowRect.
    /// This is the reference rectangle we crop against, so it must match the thumbnail's
    /// own coordinate origin.
    /// </summary>
    public static RECT GetSourceBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT bounds,
                Marshal.SizeOf<RECT>()) == 0 && bounds.Width > 0 && bounds.Height > 0)
        {
            return bounds;
        }

        NativeMethods.GetWindowRect(hwnd, out RECT fallback);
        return fallback;
    }
}
