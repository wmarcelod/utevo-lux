using System;

namespace UtevoLux.Features.Audio;

/// <summary>
/// An opaque handle to a single started voice, returned by <see cref="ISoundBackend.Play"/>.
/// The <see cref="SoundEngine"/> keeps it only so its playback-deadline watchdog can force-stop
/// that one voice (<see cref="ISoundBackend.Stop(IPlaybackHandle)"/>) without disturbing the
/// others. Callers never inspect it; a backend that cannot fail always returns a real handle and
/// a failed <see cref="ISoundBackend.Play"/> returns <see cref="NullPlaybackHandle.Instance"/>.
/// </summary>
public interface IPlaybackHandle { }

/// <summary>Sentinel returned when a <see cref="ISoundBackend.Play"/> starts no voice. Stopping it is a no-op.</summary>
public sealed class NullPlaybackHandle : IPlaybackHandle
{
    public static readonly NullPlaybackHandle Instance = new();
    private NullPlaybackHandle() { }
}

/// <summary>
/// The pluggable audio output. Two implementations exist: <see cref="MediaPlayerSoundBackend"/>
/// (always available, single-voice WPF <c>MediaPlayer</c>) and <see cref="NAudioSoundBackend"/>,
/// an NAudio mixer compiled in only when the NAudio package + the OTV_NAUDIO define are present
/// (see <see cref="SoundEngine.CreateDefaultBackend"/>).
///
/// A backend advertises <see cref="SupportsConcurrentMixing"/>: when true, each <see cref="Play"/>
/// ADDS a voice that plays alongside the others (true concurrent alerts); when false, each
/// <see cref="Play"/> REPLACES the single voice. The <see cref="SoundEngine"/> reads that flag to
/// choose between firing the whole queue at once (mixing) and its classic one-at-a-time queue.
/// Either way the engine owns queueing and the playback-deadline watchdog, so a backend only has
/// to start a voice, stop one or all voices, and report whether anything is still playing.
/// </summary>
public interface ISoundBackend : IDisposable
{
    /// <summary>
    /// True when <see cref="Play"/> mixes voices (concurrent alerts); false when it replaces the
    /// single voice. Fixed for the lifetime of the backend.
    /// </summary>
    bool SupportsConcurrentMixing { get; }

    /// <summary>
    /// Start playing <paramref name="request"/>. A concurrent backend adds a voice to the mix; a
    /// single-voice backend replaces whatever was playing. Returns a handle identifying the started
    /// voice (or <see cref="NullPlaybackHandle.Instance"/> if nothing started); never throws for a
    /// bad/missing file — it simply starts no voice.
    /// </summary>
    IPlaybackHandle Play(SoundRequest request);

    /// <summary>Stop the one voice identified by <paramref name="handle"/> (idempotent; ignores unknown/finished voices).</summary>
    void Stop(IPlaybackHandle handle);

    /// <summary>Stop every voice now (idempotent).</summary>
    void Stop();

    /// <summary>
    /// True while at least one voice is actively producing output. A single-voice backend uses this
    /// to tell the engine its one slot is free again; looping voices report busy until stopped.
    /// </summary>
    bool IsBusy { get; }

    /// <summary>Human-readable backend name, for the UI/status line.</summary>
    string Name { get; }
}
