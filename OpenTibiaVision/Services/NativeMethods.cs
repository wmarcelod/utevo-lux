using System;
using System.Runtime.InteropServices;

namespace OpenTibiaVision.Services;

/// <summary>
/// Public Win32 P/Invoke declarations (user32) used across the app.
/// DWM-specific declarations live in <see cref="DwmThumbnail"/>.
/// All APIs here are documented Microsoft Win32 functions.
/// </summary>
internal static class NativeMethods
{
    // ---- Window enumeration / text ----

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ---- DWM cloaking (detect UWP/WinUI placeholder windows) ----

    // dwAttribute for DwmGetWindowAttribute. A non-zero returned value means the window is
    // DWM-cloaked: still "visible" per IsWindowVisible, but not actually composited on screen.
    // Modern UWP/WinUI apps back their real frame with cloaked placeholder windows
    // (ApplicationFrameHost hosts / off-desktop duplicates); those must be filtered out.
    public const int DWMWA_CLOAKED = 14;

    // Overload returning a DWORD attribute (the cloak state). This is distinct from the
    // RECT-returning DwmGetWindowAttribute used for extended frame bounds in DwmThumbnail.
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    /// <summary>True if the window is DWM-cloaked (an invisible placeholder), false otherwise.</summary>
    public static bool IsWindowCloaked(IntPtr hWnd)
    {
        // On failure DwmGetWindowAttribute returns a non-zero HRESULT; treat as not cloaked.
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
            return cloaked != 0;
        return false;
    }

    // ---- Geometry ----

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    // ---- DPI ----

    // Returns the DPI of the monitor the window is on (96 == 100%). Requires the process
    // to be Per-Monitor-v2 aware (declared in app.manifest) for correct per-monitor values.
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>DPI scale factor for a window (1.0 == 96 DPI == 100%).</summary>
    public static double GetScaleForWindow(IntPtr hWnd)
    {
        uint dpi = GetDpiForWindow(hWnd);
        if (dpi == 0) dpi = 96; // GetDpiForWindow returns 0 for an invalid handle
        return dpi / 96.0;
    }

    // ---- Extended window styles (used for click-through lock / tool overlays) ----

    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_LAYERED = 0x00080000;
    public const long WS_EX_TRANSPARENT = 0x00000020;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;

    public const uint LWA_ALPHA = 0x2;

    // 64-bit safe accessors. On x64 (our default runtime) the ...Ptr entry points exist.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static long GetWindowLongEx(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex).ToInt64()
            : GetWindowLong32(hWnd, nIndex);
    }

    public static void SetWindowLongEx(IntPtr hWnd, int nIndex, long value)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, nIndex, new IntPtr(value));
        else
            SetWindowLong32(hWnd, nIndex, (int)value);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    // ---- Positioning (used to place the region-select overlay in physical pixels) ----

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;

    // ---- Low-level keyboard hook (WH_KEYBOARD_LL) ----
    // Used by HotkeyManager. The hook is global and NON-consuming: the proc always calls
    // CallNextHookEx so the keystroke continues to the game unchanged.

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn,
        IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // ---- Async key state (query live modifier state from a global hook) ----

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public const int VK_CONTROL = 0x11;
    public const int VK_SHIFT = 0x10;
    public const int VK_MENU = 0x12;   // Alt
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    // ---- Monitor / DPI ----

    public const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    // Shcore: per-monitor DPI. MDT_EFFECTIVE_DPI = 0.
    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// DPI scale factor for the monitor a physical point sits on (1.0 == 96 DPI == 100%).
    /// Mirrors <see cref="GetScaleForWindow"/> but resolves the monitor from a point rather
    /// than a window. Falls back to 1.0 for an invalid monitor or a failed DPI query.
    /// </summary>
    public static double GetScaleForPoint(int physicalX, int physicalY)
    {
        IntPtr monitor = MonitorFromPoint(new POINT { X = physicalX, Y = physicalY }, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return 1.0;

        if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint _) != 0 || dpiX == 0)
            return 1.0; // 96 DPI == 100%

        return dpiX / 96.0;
    }
}
