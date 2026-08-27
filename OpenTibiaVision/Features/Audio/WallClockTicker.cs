using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// One shared, coarse wall-clock ticker (optimization principle 4). A single
/// <see cref="DispatcherTimer"/> fans out to every subscriber on each tick; it runs ONLY while
/// there is at least one subscriber, so nothing spins when no countdown is live. Subscribers
/// read absolute EndTimes off <see cref="Environment.TickCount64"/> (a monotonic millisecond
/// clock), which makes every countdown drift-immune regardless of tick jitter.
///
/// The module creates exactly two of these: a 25 ms instance for ALL countdown timers and a
/// 50 ms instance for ALL countdown bars.
/// </summary>
public sealed class WallClockTicker
{
    private readonly DispatcherTimer _timer;
    private readonly List<Action> _subscribers = new();
    private Action[] _snapshot = Array.Empty<Action>();
    private bool _dirty;

    public WallClockTicker(int intervalMs, DispatcherPriority priority = DispatcherPriority.Render)
    {
        _timer = new DispatcherTimer(priority)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, intervalMs))
        };
        _timer.Tick += OnTick;
    }

    /// <summary>Add a per-tick callback. Dispose the handle to remove it (auto-stops when empty).</summary>
    public IDisposable Subscribe(Action onTick)
    {
        _subscribers.Add(onTick);
        _dirty = true;
        if (!_timer.IsEnabled)
            _timer.Start();
        return new Subscription(this, onTick);
    }

    private void Unsubscribe(Action onTick)
    {
        if (_subscribers.Remove(onTick))
        {
            _dirty = true;
            if (_subscribers.Count == 0)
                _timer.Stop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_dirty)
        {
            _snapshot = _subscribers.ToArray();
            _dirty = false;
        }

        // Iterate a snapshot so a callback may subscribe/unsubscribe during the tick.
        foreach (Action cb in _snapshot)
        {
            try { cb(); }
            catch { /* one bad subscriber must never stall the shared tick */ }
        }
    }

    /// <summary>Stop the timer for good (app shutdown).</summary>
    public void Shutdown()
    {
        _timer.Stop();
        _subscribers.Clear();
        _snapshot = Array.Empty<Action>();
    }

    private sealed class Subscription : IDisposable
    {
        private WallClockTicker? _owner;
        private readonly Action _cb;

        public Subscription(WallClockTicker owner, Action cb)
        {
            _owner = owner;
            _cb = cb;
        }

        public void Dispose()
        {
            _owner?.Unsubscribe(_cb);
            _owner = null;
        }
    }
}
