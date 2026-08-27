using System;
using System.Runtime.InteropServices;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Magnifier;

/// <summary>
/// Win32 P/Invoke used only by the Magnifier feature: cursor + hit-testing (to re-pick the
/// window under the cursor each frame), monitor bounds (edge compensation), GDI window regions
/// (rounded lens via SetWindowRgn), and the low-level MOUSE hook that swallows the wheel during
/// the hold. All are documented user32 / gdi32 / kernel32 functions.
///
/// Geometry uses <see cref="OpenTibiaVision.Services.RECT"/> so it matches IDwmService / IWindowService.
/// </summary>
internal static class MagnifierNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    // ---- Cursor + hit testing ----

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    public const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetTopWindow(IntPtr hWnd);

    public const uint GW_HWNDNEXT = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ---- Monitor bounds (edge-compensate the lens placement) ----

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    // ---- GDI window region (rounded / circular lens) ----

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2,
        int cornerWidth, int cornerHeight);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    // On success the system OWNS the region; do not delete it. Returns 0 on failure.
    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn,
        [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    // ---- Low-level MOUSE hook (WH_MOUSE_LL) ----
    // Installed ONLY during the hold; swallows the wheel (returns 1) so the game never scrolls
    // while zooming. Signature is identical to a keyboard LL proc.

    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEWHEEL = 0x020A;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData; // HIWORD = signed wheel delta
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookExW(int idHook, LowLevelMouseProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
