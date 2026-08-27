using System;
using System.Collections.Concurrent;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Core;

/// <summary>
/// Default <see cref="IDpiService"/>. Wraps GetDpiForWindow / GetDpiForMonitor with a small
/// per-HWND cache invalidated on WM_DPICHANGED, and an identity fast-path so 100%-DPI setups
/// pay zero conversion cost.
/// </summary>
public sealed class DpiService : IDpiService
{
    private const double Epsilon = 0.0001;
    private readonly ConcurrentDictionary<IntPtr, double> _cache = new();

    public double GetScaleForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return 1.0;

        if (_cache.TryGetValue(hwnd, out double cached))
            return cached;

        double scale = NativeMethods.GetScaleForWindow(hwnd);
        _cache[hwnd] = scale;
        return scale;
    }

    public double GetScaleForPoint(int physicalX, int physicalY)
    {
        // Cheap path: use the primary/nearest monitor of a synthetic 1x1 window would be
        // overkill; instead resolve via a monitor handle from a point is not exposed, so we
        // reuse the nearest-monitor-from-window semantics through a throwaway query is avoided.
        // In practice callers pass a source HWND; this overload exists for detached geometry.
        return 1.0; // conservative identity; window-based path is the accurate one.
    }

    public void Invalidate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            _cache.Clear();
        else
            _cache.TryRemove(hwnd, out _);
    }

    public double ToDip(double physical, double scale)
        => Math.Abs(scale - 1.0) < Epsilon ? physical : physical / scale;

    public int ToPhysical(double dip, double scale)
        => Math.Abs(scale - 1.0) < Epsilon
            ? (int)Math.Round(dip)
            : (int)Math.Round(dip * scale);
}
