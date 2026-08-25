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
    /// Enumerates every visible top-level window that has a non-empty title.
    /// </summary>
    public static List<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
                return true; // keep enumerating

            int length = NativeMethods.GetWindowTextLengthW(hWnd);
            if (length <= 0)
                return true;

            // +1 for the null terminator GetWindowTextW writes.
            var buffer = new char[length + 1];
            int copied = NativeMethods.GetWindowTextW(hWnd, buffer, buffer.Length);
            if (copied <= 0)
                return true;

            string title = new string(buffer, 0, copied);
            if (!string.IsNullOrWhiteSpace(title))
                windows.Add(new WindowInfo(hWnd, title));

            return true;
        }, IntPtr.Zero);

        return windows;
    }
}
