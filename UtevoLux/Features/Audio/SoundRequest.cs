namespace UtevoLux.Features.Audio;

/// <summary>
/// One unit of work handed to the <see cref="SoundEngine"/> queue. Immutable: the engine and
/// backends treat it as read-only, so it is safe to hand across the queue's producer/consumer
/// boundary without locking.
/// </summary>
public readonly record struct SoundRequest(
    string FilePath,
    float Volume,
    bool Loop)
{
    /// <summary>A rough upper bound (ms) for the playback-deadline watchdog on one-shots.</summary>
    public const int WatchdogCeilingMs = 10_000;
}
