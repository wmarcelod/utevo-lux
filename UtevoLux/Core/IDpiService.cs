using System;
using UtevoLux.Services;

namespace UtevoLux.Core;

/// <summary>
/// Per-monitor-v2 DPI conversions. Geometry is stored in PHYSICAL pixels everywhere; the
/// only place we convert to/from WPF DIPs is at the WPF boundary, using the scale of the
/// window/monitor that actually owns the pixels (optimization principle 8).
/// </summary>
public interface IDpiService
{
    /// <summary>Scale factor for the window's current monitor (1.0 == 96 DPI == 100%). Cached.</summary>
    double GetScaleForWindow(IntPtr hwnd);

    /// <summary>Scale factor for the monitor a physical point sits on.</summary>
    double GetScaleForPoint(int physicalX, int physicalY);

    /// <summary>Drop any cached scale for a window (call from ScaleGuard on WM_DPICHANGED).</summary>
    void Invalidate(IntPtr hwnd);

    /// <summary>Physical px -> DIP. Identity fast-path when scale == 1.0.</summary>
    double ToDip(double physical, double scale);

    /// <summary>DIP -> physical px. Identity fast-path when scale == 1.0.</summary>
    int ToPhysical(double dip, double scale);
}
