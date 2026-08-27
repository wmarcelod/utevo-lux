using System;
using System.Collections.Concurrent;
using System.Threading;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// The alert sound pump. Producers (hotkey/timer expiry, UI test) call <see cref="Enqueue"/>
/// from any thread; a single background worker drains the <see cref="ConcurrentQueue{T}"/> on a
/// ~100 ms cadence and drives ONE <see cref="ISoundBackend"/>.
///
/// Design points (optimization principle 4 + robustness):
///  - SERIALIZED single-slot: at most one sound plays at a time; the next starts only when the
///    backend reports the slot free. (TODO: an NAudio MixingSampleProvider would give true
///    concurrent alerts — see <see cref="NAudioSoundBackend"/>.)
///  - Playback-deadline WATCHDOG: every one-shot gets a hard deadline; if the backend never
///    reports "done" (a stuck sound), the watchdog force-stops it so the queue can never wedge.
///  - Looping sounds occupy the slot with no deadline until <see cref="StopAll"/> (the dismiss /
///    mute path) clears them.
/// The engine is deliberately backend-agnostic: NAudio primary, MediaPlayer fallback.
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
    private bool _hasCurrent;
    private long _deadlineTicks; // Environment.TickCount64 deadline; 0 == none (idle or loop)

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

    /// <summary>Queue a sound. A new one-shot waits its turn; call <see cref="StopAll"/> to preempt.</summary>
    public void Enqueue(SoundRequest request)
    {
        if (!_running || string.IsNullOrEmpty(request.FilePath))
            return;
        _queue.Enqueue(request);
        _wake.Set();
    }

    /// <summary>Silence everything now: drop the queue and stop the active (incl. looping) sound.</summary>
    public void StopAll()
    {
        while (_queue.TryDequeue(out _)) { }
        _flush = true;
        _wake.Set();
    }

    /// <summary>
    /// Picks the best backend available at runtime: NAudio when compiled in (OTV_NAUDIO),
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
                SafeStop();
                lock (_slotGate)
                {
                    _hasCurrent = false;
                    _deadlineTicks = 0;
                }
            }

            // Watchdog: force-stop a one-shot that outlived its deadline.
            long now = Environment.TickCount64;
            lock (_slotGate)
            {
                if (_hasCurrent && _deadlineTicks != 0 && now > _deadlineTicks)
                {
                    SafeStop();
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

        SafeStop();
    }

    private void SafePlay(SoundRequest req)
    {
        try { _backend.Play(req); }
        catch { /* a backend fault must never kill the pump */ }
    }

    private void SafeStop()
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
}
