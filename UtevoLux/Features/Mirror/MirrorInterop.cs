using System;
using System.Runtime.InteropServices;
using UtevoLux.Services;

namespace UtevoLux.Features.Mirror;

/// <summary>
/// Win32 P/Invoke declarations used by the Mirror UX features that are NOT already in the shared
/// <see cref="NativeMethods"/> (which is foundation and must not be edited). Everything here is a
/// documented Microsoft Win32 function:
///  - SetWinEventHook/UnhookWinEvent : accessibility event hooks, used for the ~250 ms auto
///    show/hide bound to the source window (preferred over polling, principle 4).
///  - PostMessageW / SetCursorPos    : right-click passthrough to the source window (SetCursorPos
///    moves the physical cursor to the mapped point so a game reading GetCursorPos / raw input
///    locates the click correctly).
///  - GetCursorPos / ScreenToClient  : cursor -> source-client mapping for the crop loupe.
///  - IsIconic / IsWindow            : source presence test for auto show/hide.
///  - GetForegroundWindow / GetCurrentProcessId : foreground-ownership test so the mirror follows
///    focus (visible while the source OR one of our own windows is foreground; hidden when a fully
///    unrelated app is foreground).
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

    // ---- Window presence / foreground ownership ----

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentProcessId();

    // ---- Right-click passthrough ----

    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const int MK_RBUTTON = 0x0002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Moves the physical cursor (PHYSICAL screen px under Per-Monitor-v2 awareness) so the game
    // sees the click where it was mapped, not where the mirror was clicked.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

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

    /// <summary>Source window is live for mirroring: exists, visible, and not minimized.</summary>
    public static bool IsSourceLive(IntPtr hwnd)
        => hwnd != IntPtr.Zero && IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd) && !IsIconic(hwnd);

    /// <summary><paramref name="hwnd"/> belongs to THIS process (our shell, a mirror, an overlay).</summary>
    public static bool IsProcessOwned(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid != 0 && pid == GetCurrentProcessId();
    }

    /// <summary>
    /// The auto-show decision for a mirror bound to <paramref name="hwnd"/>: it should be visible
    /// only while the source is live AND the foreground window is either that source or one of THIS
    /// process's own windows (shell / mirror / overlay). This is the focus-follow fix: an Alt-Tab to
    /// a fully unrelated app hides the mirror even though the source itself stays visible, while
    /// interacting with the fork's own windows never hides it. Presence still covers minimize/close.
    /// </summary>
    public static bool IsSourcePresent(IntPtr hwnd)
    {
        if (!IsSourceLive(hwnd))
            return false;

        IntPtr foreground = GetForegroundWindow();
        return foreground == hwnd || IsProcessOwned(foreground);
    }
}
