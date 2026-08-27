using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenTibiaVision.Services;

/// <summary>A top-level window: its handle and title.</summary>
public readonly record struct WindowInfo(IntPtr Hwnd, string Title)
{
    // Shown in the source picker.
    public override string ToString() => Title;
}

/// <summary>
/// Locates candidate source windows: the Tibia client specifically, or any visible
/// top-level window (so the mirror can be tested against Notepad, a browser, etc.).
/// </summary>
public static class WindowFinder
{
    private const string TibiaTitlePrefix = "Tibia - ";

    /// <summary>
    /// Finds the running Tibia client: a process named "Client" or "Tibia" whose main
    /// window title starts with "Tibia - ". Returns IntPtr.Zero if not found.
    /// </summary>
    public static IntPtr FindTibia()
    {
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string name = process.ProcessName; // no ".exe"
                bool nameMatches =
                    name.Equals("Client", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Tibia", StringComparison.OrdinalIgnoreCase);
                if (!nameMatches)
                    continue;

                IntPtr handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero)
                    continue;

                string title = process.MainWindowTitle;
                if (title.StartsWith(TibiaTitlePrefix, StringComparison.Ordinal))
                    return handle;
            }
            catch
            {
                // Some system processes deny access to ProcessName/MainWindow*; skip them.
            }
            finally
            {
                process.Dispose();
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Enumerates real, user-facing top-level windows: visible, non-empty title, not our own
    /// windows, not tool windows, not zero-size. Modern UWP/WinUI apps (Win11 Notepad, Settings)
    /// back their real frame with DWM-cloaked placeholder windows that still report
    /// IsWindowVisible; those are skipped via DWMWA_CLOAKED so the real window appears exactly
    /// once instead of being missed or duplicated. Results are de-duplicated by handle and title.
    /// </summary>
    public static List<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();
        var seenHandles = new HashSet<IntPtr>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint ownProcessId = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true; // keep enumerating

            // Skip DWM-cloaked placeholder windows (the invisible ApplicationFrameHost /
            // off-desktop duplicates behind modern UWP/WinUI apps). The real hosted window
            // is not cloaked, so it survives this filter.
            if (NativeMethods.IsWindowCloaked(hWnd))
                return true;

            // Skip our own windows: the overlay / control UI must never mirror itself.
            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == ownProcessId)
                return true;

            // Skip tool windows (palettes / overlays) — never real source targets.
            long exStyle = NativeMethods.GetWindowLongEx(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true;

            // Skip zero-size windows (message-only / off-screen placeholders).
            if (!NativeMethods.GetWindowRect(hWnd, out RECT rect) || rect.Width <= 0 || rect.Height <= 0)
                return true;

            int length = NativeMethods.GetWindowTextLengthW(hWnd);
            if (length <= 0)
                return true;

            // +1 for the null terminator GetWindowTextW writes.
            var buffer = new char[length + 1];
            int copied = NativeMethods.GetWindowTextW(hWnd, buffer, buffer.Length);
            if (copied <= 0)
                return true;

            string title = new string(buffer, 0, copied);
            if (string.IsNullOrWhiteSpace(title))
                return true;

            // De-duplicate by handle and by title (a single UWP app can surface the same
            // title through more than one non-cloaked host window).
            if (!seenHandles.Add(hWnd) || !seenTitles.Add(title))
                return true;

            windows.Add(new WindowInfo(hWnd, title));
            return true;
        }, IntPtr.Zero);

        return windows;
    }

    // ---- Client <-> screen rects (physical px) ----

    /// <summary>
    /// The window's CLIENT area in physical screen pixels: GetClientRect gives the size,
    /// ClientToScreen(0,0) gives the top-left. This is the Tibia game viewport we crop against
    /// (matches DWM fSourceClientAreaOnly), unlike the extended frame bounds which include the
    /// title bar / borders. Returns an empty RECT if the window handle is invalid.
    /// </summary>
    public static RECT GetClientBoundsInScreen(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return default;

        if (!NativeMethods.GetClientRect(hwnd, out RECT client))
            return default;

        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
            return default;

        return new RECT(origin.X, origin.Y, origin.X + client.Width, origin.Y + client.Height);
    }

    // ---- Extended-style toggles ----

    /// <summary>Add or clear one or more WS_EX_* bits on a window.</summary>
    public static void SetExStyle(IntPtr hwnd, long bits, bool on)
    {
        if (hwnd == IntPtr.Zero)
            return;

        long ex = NativeMethods.GetWindowLongEx(hwnd, NativeMethods.GWL_EXSTYLE);
        long updated = on ? ex | bits : ex & ~bits;
        if (updated != ex)
            NativeMethods.SetWindowLongEx(hwnd, NativeMethods.GWL_EXSTYLE, updated);
    }

    /// <summary>Click-through: WS_EX_LAYERED | WS_EX_TRANSPARENT (mouse falls through to the game).</summary>
    public static void SetClickThrough(IntPtr hwnd, bool on)
    {
        SetExStyle(hwnd, NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT, on);
        if (on)
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LWA_ALPHA);
    }

    /// <summary>No-activate + tool window: overlay never steals focus and never shows in Alt+Tab.</summary>
    public static void SetOverlayChrome(IntPtr hwnd, bool on)
    {
        SetExStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW, on);
    }
}
