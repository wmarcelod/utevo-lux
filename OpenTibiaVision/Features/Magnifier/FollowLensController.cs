using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Magnifier;

/// <summary>
/// The follow-cursor lens engine. While the hold gesture is down it drives a borderless,
/// click-through DWM lens that magnifies the top-level window under the cursor:
///
///   * a 33 ms @Render timer re-picks the window under the cursor (skipping every window of our
///     own process, so the lens/shell/loupe are never magnified themselves);
///   * the thumbnail is RE-REGISTERED only when the picked source actually changes;
///   * rcSource is a small crop of size (destPx / zoom) centred on the cursor's client position,
///     clamped inside the source client (edge compensation: the crop never samples off-source);
///   * the lens window is centred on the cursor and clamped to the cursor's monitor (edge
///     compensation of the window placement) so it never runs off-screen;
///   * a change-guard suppresses redundant DWM pushes when the cursor (and thus rcSource) is still.
///
/// Zoom is changed by a dedicated WH_MOUSE_LL hook (installed only during the hold) that swallows
/// the wheel — see <see cref="LowLevelMouseHook"/>. Activation itself is a momentary keyboard
/// binding on the shell's separate, non-consuming magnifier hook.
/// </summary>
internal sealed class FollowLensController : IDisposable
{
    private readonly IAppServices _services;
    private readonly Func<MagnifierSettings> _settings;
    private readonly uint _ownPid;

    private MagnifierWindow? _lens;
    private LowLevelMouseHook? _wheelHook;
    private DispatcherTimer? _timer;

    private IntPtr _currentSource;
    private double _zoom;
    private bool _active;

    // change-guard: skip a DWM push when nothing that affects the view has changed.
    private RECT _lastSrc;
    private bool _lastVisible;
    private bool _haveLast;

    public FollowLensController(IAppServices services, Func<MagnifierSettings> settings)
    {
        _services = services;
        _settings = settings;
        _ownPid = (uint)Environment.ProcessId;
    }

    public bool IsActive => _active;

    // ---- activation (bound to the hold gesture; called on the UI thread) ----

    public void Activate()
    {
        if (_active)
            return;
        _active = true;

        MagnifierSettings s = _settings();
        _zoom = Clamp(s.DefaultZoom, s.ZoomMin, s.ZoomMax);
        _currentSource = IntPtr.Zero;
        _haveLast = false;
        _lastVisible = false;

        EnsureLens();
        _lens!.Show();
        _lens.SetShape(s.Shape, s.CornerRadius, s.RingThickness);
        _lens.ApplyClickThrough(true);

        _wheelHook ??= CreateWheelHook();
        _wheelHook.Install();

        _timer ??= CreateTimer();
        _timer.Start();

        // First frame after layout settles (Host.ActualWidth is valid), so the crop maths is sound.
        _lens.Dispatcher.BeginInvoke(new Action(() => Tick(null, EventArgs.Empty)),
            DispatcherPriority.Loaded);
    }

    public void Deactivate()
    {
        if (!_active)
            return;
        _active = false;

        _timer?.Stop();
        _wheelHook?.Uninstall();

        if (_lens is not null)
        {
            _lens.UpdateView(default, 0, false); // hide the thumbnail
            _lens.Unregister();
            _lens.Hide();
        }

        _currentSource = IntPtr.Zero;
        _haveLast = false;
        _lastVisible = false;
    }

    // ---- construction ----

    private void EnsureLens()
    {
        if (_lens is not null)
            return;
        // interactive == false: always click-through, moved every frame, never persisted.
        _lens = new MagnifierWindow(_services, interactive: false);
    }

    private DispatcherTimer CreateTimer()
    {
        var t = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        t.Tick += Tick;
        return t;
    }

    private LowLevelMouseHook CreateWheelHook()
    {
        var h = new LowLevelMouseHook();
        h.Wheel += OnWheel;
        return h;
    }

    private void OnWheel(int notches)
    {
        MagnifierSettings s = _settings();
        _zoom = Clamp(_zoom + notches * s.ZoomStep, s.ZoomMin, s.ZoomMax);
        _haveLast = false;                 // force a re-push
        Tick(null, EventArgs.Empty);       // reflect the new zoom immediately
    }

    // ---- hot path ----

    private void Tick(object? sender, EventArgs e)
    {
        if (!_active || _lens is null)
            return;

        if (!MagnifierNative.GetCursorPos(out MagnifierNative.POINT cur))
            return;

        IntPtr src = PickSource(cur);
        if (src == IntPtr.Zero)
        {
            HideContent();
            return;
        }

        if (src != _currentSource)
        {
            _currentSource = src;
            _lens.SetSource(src);          // RE-REGISTER only on source change
            _haveLast = false;
        }

        RECT client = _services.Windows.GetClientBoundsInScreen(src);
        if (client.Width <= 0 || client.Height <= 0)
        {
            HideContent();
            return;
        }

        RECT dest = _lens.GetHostRectPhysical();
        int destW = dest.Width, destH = dest.Height;
        if (destW < 8 || destH < 8)
            return; // layout not ready yet; the next timer tick will catch up

        MagnifierSettings s = _settings();

        // Source crop = destination / zoom, centred on the cursor's client-relative position.
        int srcW = Math.Max(1, (int)Math.Round(destW / _zoom));
        int srcH = Math.Max(1, (int)Math.Round(destH / _zoom));

        int cx = cur.X - client.Left;      // physical, client-relative == source client px
        int cy = cur.Y - client.Top;

        int left = Clamp(cx - srcW / 2, 0, Math.Max(0, client.Width - srcW));
        int top = Clamp(cy - srcH / 2, 0, Math.Max(0, client.Height - srcH));
        var rcSource = new RECT(left, top, left + srcW, top + srcH);

        // Follow the cursor (edge-compensated so the lens stays on its monitor).
        PlaceLens(cur, s);

        // Skip the push if the view is unchanged since the last frame.
        if (_haveLast && _lastVisible && RectEquals(rcSource, _lastSrc))
            return;

        _lens.UpdateView(rcSource, s.Opacity, true);
        _lastSrc = rcSource;
        _lastVisible = true;
        _haveLast = true;
    }

    private void HideContent()
    {
        if (_lens is null)
            return;
        if (!_haveLast || _lastVisible)
        {
            _lens.UpdateView(default, 0, false);
            _lastVisible = false;
            _haveLast = true;
        }
    }

    private void PlaceLens(MagnifierNative.POINT cur, MagnifierSettings s)
    {
        int size = Math.Max(40, s.LensSize);
        int x = cur.X - size / 2 + s.CursorOffsetX;
        int y = cur.Y - size / 2 + s.CursorOffsetY;

        var mi = new MagnifierNative.MONITORINFO { cbSize = Marshal.SizeOf<MagnifierNative.MONITORINFO>() };
        IntPtr mon = MagnifierNative.MonitorFromPoint(cur, MagnifierNative.MONITOR_DEFAULTTONEAREST);
        if (mon != IntPtr.Zero && MagnifierNative.GetMonitorInfoW(mon, ref mi))
        {
            x = Clamp(x, mi.rcMonitor.Left, Math.Max(mi.rcMonitor.Left, mi.rcMonitor.Right - size));
            y = Clamp(y, mi.rcMonitor.Top, Math.Max(mi.rcMonitor.Top, mi.rcMonitor.Bottom - size));
        }

        _lens!.SetPlacementPhysical(x, y, size, size);
    }

    // ---- source picking ----

    private IntPtr PickSource(MagnifierNative.POINT cur)
    {
        // Fast path: the window directly under the cursor, promoted to its top-level root.
        IntPtr hit = MagnifierNative.WindowFromPoint(cur);
        if (hit != IntPtr.Zero)
        {
            IntPtr root = MagnifierNative.GetAncestor(hit, MagnifierNative.GA_ROOT);
            if (IsCandidate(root, cur))
                return root;
        }

        // Fallback (e.g. the click-through lens was returned): walk the Z-order top -> bottom and
        // take the first visible non-own-process window whose rect contains the cursor.
        for (IntPtr w = MagnifierNative.GetTopWindow(IntPtr.Zero);
             w != IntPtr.Zero;
             w = MagnifierNative.GetWindow(w, MagnifierNative.GW_HWNDNEXT))
        {
            if (IsCandidate(w, cur))
                return w;
        }

        return IntPtr.Zero;
    }

    private bool IsCandidate(IntPtr hwnd, MagnifierNative.POINT cur)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        if (!MagnifierNative.IsWindowVisible(hwnd) || MagnifierNative.IsIconic(hwnd))
            return false;

        // Skip EVERY window of our own process (lens, shell, loupe, toasts) — the "skip self" rule.
        MagnifierNative.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid)
            return false;

        if (!MagnifierNative.GetWindowRect(hwnd, out RECT r) || r.Width <= 0 || r.Height <= 0)
            return false;

        return cur.X >= r.Left && cur.X < r.Right && cur.Y >= r.Top && cur.Y < r.Bottom;
    }

    // ---- helpers ----

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

    private static bool RectEquals(RECT a, RECT b)
        => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    public void Dispose()
    {
        _timer?.Stop();
        _wheelHook?.Dispose();
        if (_lens is not null)
        {
            _lens.Unregister();
            _lens.Close();
            _lens = null;
        }
    }
}
