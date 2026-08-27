using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using UtevoLux.Core;
using UtevoLux.Features.Mirror;
using UtevoLux.Models;

namespace UtevoLux.Features.Obs;

/// <summary>
/// An OBS-bound mirror. It IS the fork's <see cref="MirrorWindow"/> — the DWM live-thumbnail host,
/// zoom/opacity/right-click-passthrough, the single context menu, aspect-lock resize, everything —
/// reused unchanged by subclassing. The only thing added is the original tool "OBS mirror"
/// behavior: an AGGRESSIVE always-on-top re-assert (slam to top now, then again every ~2s) so the
/// mirror stays above the capture tool's projector window, which itself keeps grabbing the top of
/// the z-order.
///
/// This mirrors the original <c>RegionMirrorWindow</c>, where an <c>_obsTopmostTimer</c> (2s) fires
/// <c>WindowHelper.SetWindowAlwaysOnTopAggressive</c> only for regions with <c>IsObsMirror</c>.
/// </summary>
public sealed class ObsMirrorWindow : MirrorWindow
{
    // 2s cadence, matching the original's DispatcherTimer(Interval = FromSeconds(2)).
    private static readonly TimeSpan ReassertInterval = TimeSpan.FromSeconds(2.0);

    private readonly DispatcherTimer _topmostTimer;
    private IntPtr _hwnd;
    private bool _paused;

    public ObsMirrorWindow(IAppServices services, IntPtr sourceHwnd, RegionConfig config, MirrorUxState ux)
        : base(services, sourceHwnd, config, ux)
    {
        _topmostTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ReassertInterval };
        _topmostTimer.Tick += (_, _) => Reassert();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        // Base places the window in physical px and registers the DWM thumbnail first.
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Reassert();
        _topmostTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer.Stop();
        base.OnClosed(e);
    }

    /// <summary>
    /// Suspend the re-assert while a transient tool window (the crop overlay / loupe) must sit above
    /// this mirror — otherwise the 2s slam would fight the crop UI for the top of the topmost band
    /// during a re-crop. The original stops <c>_obsTopmostTimer</c> for the same reason when it opens
    /// a crop window.
    /// </summary>
    public void PauseTopmost()
    {
        _paused = true;
        _topmostTimer.Stop();
    }

    /// <summary>Resume the re-assert and immediately slam back to the top.</summary>
    public void ResumeTopmost()
    {
        _paused = false;
        Reassert();
        _topmostTimer.Start();
    }

    private void Reassert()
    {
        if (_paused)
            return;
        ObsTopmost.SetAlwaysOnTopAggressive(_hwnd);
    }
}
