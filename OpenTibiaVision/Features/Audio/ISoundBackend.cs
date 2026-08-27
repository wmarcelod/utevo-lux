using System;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// The pluggable audio output. Two implementations exist: <see cref="MediaPlayerSoundBackend"/>
/// (always available, WPF <c>MediaPlayer</c>) and an NAudio <c>WaveOutEvent</c> backend that is
/// compiled in only when the NAudio package + the OTV_NAUDIO define are present (see
/// <see cref="SoundEngine.CreateDefaultBackend"/>). The <see cref="SoundEngine"/> owns all
/// queueing, serialization and the watchdog, so a backend only has to start/stop one sound and
/// report whether it is still busy.
/// </summary>
public interface ISoundBackend : IDisposable
{
    /// <summary>Start playing <paramref name="request"/>. Any currently playing sound is replaced.</summary>
    void Play(SoundRequest request);

    /// <summary>Stop whatever is playing now (idempotent).</summary>
    void Stop();

    /// <summary>
    /// True while a sound is actively producing output. The engine polls this to know when the
    /// single playback slot is free again (looping sounds report busy until <see cref="Stop"/>).
    /// </summary>
    bool IsBusy { get; }

    /// <summary>Human-readable backend name, for the UI/status line.</summary>
    string Name { get; }
}
