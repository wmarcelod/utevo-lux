using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// Always-available fallback backend built on the WPF <see cref="MediaPlayer"/>, used only when
/// the NAudio mixer fails to initialise. It is SINGLE-VOICE (<see cref="SupportsConcurrentMixing"/>
/// is false): a new <see cref="Play"/> replaces whatever was playing, so overlapping alerts fall
/// back to the engine's one-at-a-time queue rather than mixing. MediaPlayer is a
/// <see cref="DispatcherObject"/>, so every operation is marshalled onto the UI dispatcher
/// captured at construction; the <see cref="SoundEngine"/> worker thread calls these freely.
/// Looping is realized by re-playing on <c>MediaEnded</c> (a ~gapless re-arm), which the engine
/// caps with its own watchdog. <see cref="IsBusy"/> is a volatile flag flipped on the dispatcher
/// thread and read by the worker.
/// </summary>
public sealed class MediaPlayerSoundBackend : ISoundBackend
{
    // One voice, so one shared handle: any Stop(handle) simply stops that single voice.
    private static readonly IPlaybackHandle Voice = new SingleVoiceHandle();

    private readonly Dispatcher _dispatcher;
    private MediaPlayer? _player;
    private volatile bool _busy;
    private volatile bool _loop;
    private volatile bool _disposed;

    public MediaPlayerSoundBackend()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public string Name => "WPF MediaPlayer";

    public bool SupportsConcurrentMixing => false;

    public bool IsBusy => _busy;

    public IPlaybackHandle Play(SoundRequest request)
    {
        if (_disposed || string.IsNullOrEmpty(request.FilePath))
            return NullPlaybackHandle.Instance;

        _busy = true;
        _loop = request.Loop;

        _dispatcher.InvokeAsync(() =>
        {
            if (_disposed)
                return;
            try
            {
                MediaPlayer p = EnsurePlayer();
                p.Stop();
                p.Volume = Math.Clamp(request.Volume, 0f, 1f);
                p.Open(new Uri(request.FilePath, UriKind.Absolute));
                p.Play();
            }
            catch
            {
                _busy = false;
                _loop = false;
            }
        });

        return Voice;
    }

    // Single voice: stopping "this handle" is the same as stopping everything.
    public void Stop(IPlaybackHandle handle) => Stop();

    public void Stop()
    {
        _loop = false;
        if (_disposed)
        {
            _busy = false;
            return;
        }

        _dispatcher.InvokeAsync(() =>
        {
            try { _player?.Stop(); }
            catch { /* ignore */ }
            _busy = false;
        });
    }

    private MediaPlayer EnsurePlayer()
    {
        if (_player is not null)
            return _player;

        _player = new MediaPlayer();
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        return _player;
    }

    private void OnMediaEnded(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        if (_loop && _player is not null)
        {
            try
            {
                _player.Position = TimeSpan.Zero;
                _player.Play();
                return; // stays busy while looping
            }
            catch { /* fall through to idle */ }
        }

        _busy = false;
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        _busy = false;
        _loop = false;
    }

    public void Dispose()
    {
        _disposed = true;
        _loop = false;
        _busy = false;

        // Tear the player down on its own thread.
        try
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_player is not null)
                    {
                        _player.MediaEnded -= OnMediaEnded;
                        _player.MediaFailed -= OnMediaFailed;
                        _player.Stop();
                        _player.Close();
                        _player = null;
                    }
                }
                catch { /* ignore */ }
            });
        }
        catch { /* dispatcher gone during shutdown */ }
    }

    /// <summary>Identity handle for this backend's single voice.</summary>
    private sealed class SingleVoiceHandle : IPlaybackHandle { }
}
