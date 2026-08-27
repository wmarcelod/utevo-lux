// The primary sound backend uses NAudio's WaveOutEvent. NAudio is an EXTERNAL NuGet package that
// OpenTibiaVision.csproj now references, and integration has enabled it as the PRIMARY backend via:
//     <ItemGroup>
//       <PackageReference Include="NAudio" Version="2.2.1" />
//     </ItemGroup>
//     <PropertyGroup>
//       <DefineConstants>$(DefineConstants);OTV_NAUDIO</DefineConstants>
//     </PropertyGroup>
// With the package referenced and OTV_NAUDIO defined, this file compiles and
// SoundEngine.CreateDefaultBackend() prefers this class automatically. The #if OTV_NAUDIO guard is
// retained so the tree still builds if the package/define are ever removed, in which case the app
// falls back to MediaPlayerSoundBackend (a fully working WPF fallback) with zero missing symbols.
#if OTV_NAUDIO
using System;
using NAudio.Wave;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// Primary backend: NAudio <see cref="WaveOutEvent"/> driving an <see cref="AudioFileReader"/>
/// (wav/mp3/aiff), with a small <see cref="LoopStream"/> for gapless looping. All state changes
/// take a lock and a generation counter guards stale PlaybackStopped callbacks from a player
/// that was already replaced. The <see cref="SoundEngine"/> still owns queueing, serialization
/// and the watchdog; this class only ever drives ONE sound.
///
/// TODO (concurrent alerts): to mix several alerts at once, feed AudioFileReaders into an NAudio
/// MixingSampleProvider bound to a single shared WaveOutEvent instead of replacing the player.
/// </summary>
public sealed class NAudioSoundBackend : ISoundBackend
{
    private readonly object _gate = new();
    private WaveOutEvent? _out;
    private WaveStream? _stream;   // AudioFileReader (one-shot) or LoopStream wrapping it (loop)
    private AudioFileReader? _reader;
    private volatile bool _busy;
    private volatile bool _disposed;
    private int _generation;

    public string Name => "NAudio WaveOutEvent";

    public bool IsBusy => _busy;

    public void Play(SoundRequest request)
    {
        if (_disposed || string.IsNullOrEmpty(request.FilePath))
            return;

        lock (_gate)
        {
            StopLocked();
            int gen = ++_generation;

            AudioFileReader? reader = null;
            WaveStream? playable = null;
            WaveOutEvent? outp = null;
            try
            {
                reader = new AudioFileReader(request.FilePath)
                {
                    Volume = Math.Clamp(request.Volume, 0f, 1f)
                };
                playable = request.Loop ? new LoopStream(reader) : reader;

                outp = new WaveOutEvent();
                outp.PlaybackStopped += (_, _) => OnStopped(gen);
                outp.Init(playable);

                _reader = reader;
                _stream = playable;
                _out = outp;
                _busy = true;
                outp.Play();
            }
            catch
            {
                _busy = false;
                try { outp?.Dispose(); } catch { }
                try { playable?.Dispose(); } catch { }        // disposes reader when looping
                if (playable is null) { try { reader?.Dispose(); } catch { } }
                _out = null; _stream = null; _reader = null;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
            StopLocked();
    }

    private void StopLocked()
    {
        _busy = false;
        try { _out?.Stop(); } catch { }
        DisposeGraphLocked();
    }

    private void DisposeGraphLocked()
    {
        // LoopStream.Dispose disposes the wrapped reader; in the one-shot case _stream IS the
        // reader. Either way disposing _stream is sufficient — never dispose _reader twice.
        try { _out?.Dispose(); } catch { }
        try { _stream?.Dispose(); } catch { }
        _out = null; _stream = null; _reader = null;
    }

    private void OnStopped(int generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return; // stale callback from a player we already replaced
            _busy = false;
            DisposeGraphLocked();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_gate)
            StopLocked();
    }

    /// <summary>Wraps a source stream and rewinds it at EOF so playback loops seamlessly.</summary>
    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream _source;

        public LoopStream(WaveStream source) => _source = source;

        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;
        public override long Position
        {
            get => _source.Position;
            set => _source.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _source.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    if (_source.Position == 0)
                        break; // empty source; avoid a tight infinite loop
                    _source.Position = 0;
                }
                total += read;
            }
            return total;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _source.Dispose();
            base.Dispose(disposing);
        }
    }
}
#endif
