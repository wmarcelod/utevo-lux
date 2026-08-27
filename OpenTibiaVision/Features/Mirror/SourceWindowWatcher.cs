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
/// One process-wide out-of-context WinEvent hook feeds a HashSet of watched HWNDs. The callback
/// arrives on the thread that installed the hook (the UI thread), so subscribers may touch WPF
/// directly. The debounce timer runs on that same dispatcher.
/// </summary>
public sealed class SourceWindowWatcher : IDisposable
{
    private readonly HashSet<IntPtr> _watched = new();
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

        if (_watched.Add(hwnd) && _watched.Count == 1)
            EnsureHooks();
    }

    public void Unwatch(IntPtr hwnd)
    {
        if (_watched.Remove(hwnd) && _watched.Count == 0)
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
        if (hwnd == IntPtr.Zero || !_watched.Contains(hwnd))
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
