using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace UtevoLux.Features.Audio;

/// <summary>
/// The alert sound pump. Producers (hotkey/timer expiry, UI test) call <see cref="Enqueue"/>
/// from any thread; a single background worker drains the <see cref="ConcurrentQueue{T}"/> on a
/// ~100 ms cadence and drives one <see cref="ISoundBackend"/>.
///
/// The engine adapts to the backend's <see cref="ISoundBackend.SupportsConcurrentMixing"/>:
///  - CONCURRENT (NAudio mixer): every queued request is started immediately, so overlapping
///    alerts play TOGETHER. Each one-shot voice gets its own playback-deadline entry and the
///    watchdog force-stops just that voice (<see cref="ISoundBackend.Stop(IPlaybackHandle)"/>) if
///    it ever outlives its deadline, so a stuck/never-ending clip can't leak a mixer voice.
///  - SERIAL (MediaPlayer fallback): at most one voice plays at a time; the next request starts
///    only when the backend reports its slot free, and the single watchdog force-stops a one-shot
///    that outlived its deadline so the queue can never wedge.
/// In both modes looping sounds carry no deadline and are cleared only by <see cref="StopAll"/>
/// (the dismiss / mute path). The engine is deliberately backend-agnostic: NAudio primary,
/// MediaPlayer fallback.
/// </summary>
public sealed class SoundEngine : IDisposable
{
    private const int DrainCadenceMs = 100;

    private readonly ConcurrentQueue<SoundRequest> _queue = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly ISoundBackend _backend;
    private readonly Thread _worker;
    private readonly object _slotGate = new();

    private volatile bool _running = true;
    private volatile bool _flush;

    // Serial-path single-slot state (used only when the backend does not mix).
    private bool _hasCurrent;
    private long _deadlineTicks; // Environment.TickCount64 deadline; 0 == none (idle or loop)

    // Concurrent-path in-flight one-shot voices awaiting their watchdog deadline. Touched only by
    // the worker thread (Play, the watchdog sweep and the flush all run there), so it needs no lock.
    private readonly List<InflightVoice> _inflight = new();

    public SoundEngine(ISoundBackend backend)
    {
        _backend = backend;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "OTV-SoundEngine"
        };
        _worker.Start();
    }

    /// <summary>Name of the backend actually in use (for the status line).</summary>
    public string BackendName => _backend.Name;

    /// <summary>Queue a sound. With a mixing backend it starts alongside the others; with the serial
    /// fallback it waits its turn. Call <see cref="StopAll"/> to silence everything.</summary>
    public void Enqueue(SoundRequest request)
    {
        if (!_running || string.IsNullOrEmpty(request.FilePath))
            return;
        _queue.Enqueue(request);
        _wake.Set();
    }

    /// <summary>Silence everything now: drop the queue and stop every active (incl. looping) sound.</summary>
    public void StopAll()
    {
        while (_queue.TryDequeue(out _)) { }
        _flush = true;
        _wake.Set();
    }

    /// <summary>
    /// Picks the best backend available at runtime: the NAudio mixer when compiled in (OTV_NAUDIO),
    /// otherwise the always-present WPF MediaPlayer. Never throws — falls back on any failure.
    /// </summary>
    public static ISoundBackend CreateDefaultBackend()
    {
#if OTV_NAUDIO
        try { return new NAudioSoundBackend(); }
        catch { /* fall through to the MediaPlayer fallback */ }
#endif
        return new MediaPlayerSoundBackend();
    }

    private void WorkerLoop()
    {
        bool concurrent = _backend.SupportsConcurrentMixing;

        while (_running)
        {
            _wake.Wait(DrainCadenceMs);
            _wake.Reset();
            if (!_running)
                break;

            if (_flush)
            {
                _flush = false;
                while (_queue.TryDequeue(out _)) { }
                SafeStopAll();
                _inflight.Clear();
                lock (_slotGate)
                {
                    _hasCurrent = false;
                    _deadlineTicks = 0;
                }
            }

            if (concurrent)
                PumpConcurrent();
            else
                PumpSerial();
        }

        SafeStopAll();
    }

    /// <summary>Mixing backend: start every queued request at once and watchdog each one-shot voice.</summary>
    private void PumpConcurrent()
    {
        // Watchdog: force-stop any one-shot voice that outlived its deadline (a stuck clip that
        // never reported "done"). A voice that already finished naturally was auto-removed by the
        // mixer, so Stop() on its handle is a harmless no-op.
        long now = Environment.TickCount64;
        for (int i = _inflight.Count - 1; i >= 0; i--)
        {
            if (now > _inflight[i].DeadlineTicks)
            {
                SafeStop(_inflight[i].Handle);
                _inflight.RemoveAt(i);
            }
        }

        // Start EVERYTHING pending; the mixer plays the voices together.
        while (_queue.TryDequeue(out SoundRequest req))
        {
            IPlaybackHandle handle = SafePlay(req);
            if (!req.Loop) // loops have no deadline; StopAll clears them
                _inflight.Add(new InflightVoice(
                    handle, Environment.TickCount64 + SoundRequest.WatchdogCeilingMs));
        }
    }

    /// <summary>Serial backend: one voice at a time, gated on the backend's free slot.</summary>
    private void PumpSerial()
    {
        // Watchdog: force-stop a one-shot that outlived its deadline.
        long now = Environment.TickCount64;
        lock (_slotGate)
        {
            if (_hasCurrent && _deadlineTicks != 0 && now > _deadlineTicks)
            {
                SafeStopAll();
                _hasCurrent = false;
                _deadlineTicks = 0;
            }
        }

        // If the single slot is free, start the next queued sound.
        if (!_backend.IsBusy)
        {
            lock (_slotGate)
                _hasCurrent = false;

            if (_queue.TryDequeue(out SoundRequest req))
            {
                SafePlay(req);
                lock (_slotGate)
                {
                    _hasCurrent = true;
                    _deadlineTicks = req.Loop
                        ? 0
                        : Environment.TickCount64 + SoundRequest.WatchdogCeilingMs;
                }
            }
        }
        // else: still playing; the DrainCadenceMs timeout re-checks without busy-spinning.
    }

    private IPlaybackHandle SafePlay(SoundRequest req)
    {
        try { return _backend.Play(req); }
        catch { return NullPlaybackHandle.Instance; } // a backend fault must never kill the pump
    }

    private void SafeStop(IPlaybackHandle handle)
    {
        try { _backend.Stop(handle); }
        catch { /* ignore */ }
    }

    private void SafeStopAll()
    {
        try { _backend.Stop(); }
        catch { /* ignore */ }
    }

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        try { _worker.Join(500); }
        catch { /* ignore */ }

        try { _backend.Dispose(); }
        catch { /* ignore */ }

        _wake.Dispose();
    }

    /// <summary>One concurrent one-shot voice tracked for the playback-deadline watchdog.</summary>
    private readonly struct InflightVoice
    {
        public InflightVoice(IPlaybackHandle handle, long deadlineTicks)
        {
            Handle = handle;
            DeadlineTicks = deadlineTicks;
        }

        public IPlaybackHandle Handle { get; }
        public long DeadlineTicks { get; }
    }
}
