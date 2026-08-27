using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// Watches source windows for show/hide/minimize/restore/destroy AND every foreground change, and
/// raises a single coalesced signal ~250 ms after activity settles, so mirrors can auto show/hide
/// bound to their source (principle 4: prefer SetWinEventHook over polling; layer a debounce over
/// the burst).
///
/// Foreground handling is why this is not a pure presence watcher: a mirror must hide when the user
/// Alt-Tabs to an UNRELATED app even though the source stays visible, and re-show when focus returns
/// to the source or to one of the fork's OWN windows. Foreground changes ripple globally, so we react
/// to ALL of them (any hwnd) and let each subscriber decide via <see cref="MirrorInterop.IsSourcePresent"/>;
/// presence events (minimize/restore/destroy/hide) are still filtered to the watched sources. The
/// system hook deliberately does NOT skip our own process, so focus landing on the shell / a mirror /
/// an overlay is delivered too (that is what keeps interacting with our own windows from hiding them,
/// and lets a hidden mirror re-appear when the fork regains focus).
///
/// One process-wide out-of-context WinEvent hook feeds a refcounted map of watched HWNDs
/// (Dictionary&lt;HWND,int&gt;): the same source hwnd can back several mirror rows, so each
/// <see cref="Watch"/> increments and each <see cref="Unwatch"/> decrements that hwnd's count,
/// and a source is only truly dropped when its count reaches zero. Without the refcount, a second
/// row watching an already-watched hwnd would be a silent no-op and the first row's Unwatch would
/// tear the shared hooks down while the second row still needs them. The callback arrives on the
/// thread that installed the hook (the UI thread), so subscribers may touch WPF directly. The
/// debounce timer runs on that same dispatcher.
/// </summary>
public sealed class SourceWindowWatcher : IDisposable
{
    // hwnd -> number of live subscribers watching it. Hooks are process-wide, installed while at
    // least one distinct hwnd is watched (map non-empty) and released when the last one drops.
    private readonly Dictionary<IntPtr, int> _watched = new();
    private readonly DispatcherTimer _debounce;
    private readonly MirrorInterop.WinEventProc _proc; // kept alive so the GC won't collect it

    private IntPtr _systemHook;
    private IntPtr _objectHook;
    private bool _disposed;

    /// <summary>Raised (UI thread) after activity settles; subscribers re-check their own source.</summary>
    public event Action? PresenceMayHaveChanged;

    public SourceWindowWatcher()
    {
        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _debounce.Tick += OnDebounceTick;
        _proc = OnWinEvent;
    }

    public void Watch(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || _disposed)
            return;

        if (_watched.TryGetValue(hwnd, out int count))
        {
            // Already watched by another row: bump its refcount, hooks stay as-is.
            _watched[hwnd] = count + 1;
            return;
        }

        // First subscriber for this hwnd; if it is the first watched hwnd overall, install hooks.
        _watched[hwnd] = 1;
        if (_watched.Count == 1)
            EnsureHooks();
    }

    public void Unwatch(IntPtr hwnd)
    {
        if (!_watched.TryGetValue(hwnd, out int count))
            return;

        if (count > 1)
        {
            // Other rows still watch this hwnd: just decrement, keep the hooks.
            _watched[hwnd] = count - 1;
            return;
        }

        // Last subscriber for this hwnd; drop it and release hooks once nothing is watched.
        _watched.Remove(hwnd);
        if (_watched.Count == 0)
            ReleaseHooks();
    }

    private void EnsureHooks()
    {
        if (_systemHook == IntPtr.Zero)
        {
            // NOT SKIPOWNPROCESS: we need foreground events for our OWN windows too, so focus
            // landing on the shell / a mirror / an overlay is re-evaluated (kept visible) instead
            // of being invisible to the hook.
            _systemHook = MirrorInterop.SetWinEventHook(
                MirrorInterop.EVENT_SYSTEM_FOREGROUND, MirrorInterop.EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero, _proc, 0, 0,
                MirrorInterop.WINEVENT_OUTOFCONTEXT);
        }

        if (_objectHook == IntPtr.Zero)
        {
            _objectHook = MirrorInterop.SetWinEventHook(
                MirrorInterop.EVENT_OBJECT_DESTROY, MirrorInterop.EVENT_OBJECT_HIDE,
                IntPtr.Zero, _proc, 0, 0,
                MirrorInterop.WINEVENT_OUTOFCONTEXT | MirrorInterop.WINEVENT_SKIPOWNPROCESS);
        }
    }

    private void ReleaseHooks()
    {
        if (_systemHook != IntPtr.Zero)
        {
            MirrorInterop.UnhookWinEvent(_systemHook);
            _systemHook = IntPtr.Zero;
        }
        if (_objectHook != IntPtr.Zero)
        {
            MirrorInterop.UnhookWinEvent(_objectHook);
            _objectHook = IntPtr.Zero;
        }
        _debounce.Stop();
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        // Window-level events only; ignore caret/child accessibility noise.
        if (idObject != MirrorInterop.OBJID_WINDOW)
            return;

        // A foreground change (from ANY window, including our own) may flip a mirror's visibility,
        // so always re-evaluate; the subscriber's IsSourcePresent decides source vs. own vs. unrelated.
        if (eventType == MirrorInterop.EVENT_SYSTEM_FOREGROUND)
        {
            SignalDebounced();
            return;
        }

        // Presence events (minimize/restore/destroy/hide) only matter for the sources we watch.
        if (hwnd == IntPtr.Zero || !_watched.ContainsKey(hwnd))
            return;

        SignalDebounced();
    }

    /// <summary>Coalesce a burst of events: restart the one-shot debounce.</summary>
    private void SignalDebounced()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        PresenceMayHaveChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _debounce.Tick -= OnDebounceTick;
        _watched.Clear();
        ReleaseHooks();
        PresenceMayHaveChanged = null;
    }
}
