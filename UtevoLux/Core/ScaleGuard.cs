using System;
using System.Windows;
using System.Windows.Interop;
using UtevoLux.Services;

namespace UtevoLux.Core;

/// <summary>
/// Watches a window for WM_DPICHANGED and re-latches DPI state: it invalidates the cached
/// scale for that HWND and raises <see cref="DpiChanged"/> so the window can recompute any
/// physical<->DIP geometry (DWM destination rects, mirror placement). Attach one per window
/// that owns physical-pixel geometry (shell + each mirror).
/// </summary>
public sealed class ScaleGuard : IDisposable
{
    private const int WM_DPICHANGED = 0x02E0;

    private readonly Window _window;
    private readonly IDpiService _dpi;
    private HwndSource? _source;
    private IntPtr _hwnd;

    /// <summary>Raised (on the UI thread) with the new scale after a DPI change.</summary>
    public event Action<double>? DpiChanged;

    public ScaleGuard(Window window, IDpiService dpi)
    {
        _window = window;
        _dpi = dpi;

        if (window.IsLoaded || new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Attach();
        else
            window.SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _window.SourceInitialized -= OnSourceInitialized;
        Attach();
    }

    private void Attach()
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero)
            return;

        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DPICHANGED)
        {
            _dpi.Invalidate(_hwnd);
            // LOWORD(wParam) is the new X DPI.
            uint dpiX = (uint)(wParam.ToInt64() & 0xFFFF);
            double scale = dpiX == 0 ? _dpi.GetScaleForWindow(_hwnd) : dpiX / 96.0;
            DpiChanged?.Invoke(scale);
            // Not marking handled: WPF's own DPI handling still positions the window.
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _window.SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WndProc);
        _source = null;
    }
}
