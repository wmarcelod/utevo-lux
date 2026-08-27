using System;
using System.Runtime.InteropServices;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// Win32 P/Invoke declarations used by the Mirror UX features that are NOT already in the shared
/// <see cref="NativeMethods"/> (which is foundation and must not be edited). Everything here is a
/// documented Microsoft Win32 function:
///  - SetWinEventHook/UnhookWinEvent : accessibility event hooks, used for the ~250 ms auto
///    show/hide bound to the source window (preferred over polling, principle 4).
///  - PostMessageW                   : right-click passthrough to the source window.
///  - GetCursorPos / ScreenToClient  : cursor -> source-client mapping for the crop loupe.
///  - IsIconic / IsWindow            : source presence test for auto show/hide.
/// </summary>
internal static class MirrorInterop
{
    // ---- WinEvent hooks (auto show/hide) ----

    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;

    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;

    public const int OBJID_WINDOW = 0;

    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // ---- Window presence ----

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    // ---- Right-click passthrough ----

    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const int MK_RBUTTON = 0x0002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ---- Cursor -> source client mapping (loupe) ----

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out NativeMethods.POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref NativeMethods.POINT lpPoint);

    /// <summary>Pack two 16-bit client coordinates into an LPARAM for a mouse message.</summary>
    public static IntPtr MakeLParam(int x, int y)
        => new IntPtr((y << 16) | (x & 0xFFFF));

    /// <summary>Signed low 16 bits (GET_X_LPARAM) of a mouse-message LPARAM.</summary>
    public static int LoWordSigned(IntPtr lParam) => unchecked((short)(lParam.ToInt64() & 0xFFFF));

    /// <summary>Signed high 16 bits (GET_Y_LPARAM) of a mouse-message LPARAM.</summary>
    public static int HiWordSigned(IntPtr lParam) => unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

    /// <summary>Source is present for mirroring: a live, visible, non-minimized window.</summary>
    public static bool IsSourcePresent(IntPtr hwnd)
        => hwnd != IntPtr.Zero && IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd) && !IsIconic(hwnd);
}
