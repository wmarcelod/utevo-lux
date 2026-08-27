using System;
using OpenTibiaVision.Core;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// The shared runtime context handed to every <see cref="TimerRowViewModel"/>: the sound pump,
/// the sound library, the two shared wall-clock tickers (25 ms for countdowns, 50 ms for bars),
/// and the master mute/volume the user controls from the page. Bundling these avoids a fat row
/// constructor and keeps a single source of truth for the master audio state.
/// </summary>
public sealed class AudioRuntime
{
    public AudioRuntime(
        IAppServices services,
        SoundEngine sound,
        SoundLibrary library,
        WallClockTicker countdownTicker,
        WallClockTicker barTicker)
    {
        Services = services;
        Sound = sound;
        Library = library;
        CountdownTicker = countdownTicker;
        BarTicker = barTicker;
    }

    public IAppServices Services { get; }
    public SoundEngine Sound { get; }
    public SoundLibrary Library { get; }

    /// <summary>25 ms ticker shared by ALL countdown timers.</summary>
    public WallClockTicker CountdownTicker { get; }

    /// <summary>50 ms ticker shared by ALL countdown bars.</summary>
    public WallClockTicker BarTicker { get; }

    public double MasterVolume { get; set; } = 1.0;
    public bool Muted { get; set; }

    /// <summary>Final linear volume for a per-timer level, honoring master mute/volume.</summary>
    public float EffectiveVolume(double perTimerVolume)
        => Muted ? 0f : (float)Math.Clamp(MasterVolume * perTimerVolume, 0.0, 1.0);
}
