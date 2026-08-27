// The primary sound backend uses NAudio's mixer. NAudio is an EXTERNAL NuGet package that
// UtevoLux.csproj now references, and integration has enabled it as the PRIMARY backend via:
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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace UtevoLux.Features.Audio;

/// <summary>
/// Primary backend: true CONCURRENT mixing. A single shared <see cref="WaveOutEvent"/> is
/// initialised once against one <see cref="MixingSampleProvider"/> running at a fixed float format
/// and left playing for the life of the backend (it emits silence while idle, because
/// <see cref="MixingSampleProvider.ReadFully"/> is on). Every <see cref="Play"/> opens the clip
/// with an <see cref="AudioFileReader"/>, resamples/channel-converts it to the mixer format, and
/// adds it as one more mixer input, so overlapping alerts sound TOGETHER instead of cutting each
/// other off. A one-shot input reaches EOF and the mixer auto-removes it (raising
/// <see cref="MixingSampleProvider.MixerInputEnded"/>, where we dispose its reader); a looping
/// input never ends and is cleared only by <see cref="Stop()"/> (dismiss / mute) or the engine's
/// watchdog via <see cref="Stop(IPlaybackHandle)"/>. Per-voice volume rides on
/// <see cref="AudioFileReader.Volume"/> (the engine pre-multiplies master volume into the request).
///
/// Threading: <see cref="Play"/>/<see cref="Stop()"/>/<see cref="Stop(IPlaybackHandle)"/> are all
/// driven by the single <see cref="SoundEngine"/> worker thread; only <see cref="OnMixerInputEnded"/>
/// arrives on NAudio's audio thread. The mixer serialises its own input list internally, and
/// <c>_voices</c> is guarded by <c>_gate</c>. To avoid a lock-order inversion with the mixer's
/// internal lock, we never hold <c>_gate</c> while calling into the mixer.
/// </summary>
public sealed class NAudioSoundBackend : ISoundBackend
{
    // A fixed 44.1 kHz / stereo / 32-bit float mixer format. Every clip is converted to this so it
    // can share one device; 44.1 kHz matches the synthesized built-in beeps (BeepSynth), so those
    // (the common case) need no resampling.
    private const int MixSampleRate = 44_100;
    private const int MixChannels = 2;

    private readonly object _gate = new();
    private readonly IWavePlayer _output;
    private readonly MixingSampleProvider _mixer;

    // input-as-added-to-the-mixer -> its disposable resources. Keyed by the exact ISampleProvider
    // handed to AddMixerInput, which is what MixerInputEnded reports back.
    private readonly Dictionary<ISampleProvider, VoiceResources> _voices = new();

    private volatile bool _disposed;

    public NAudioSoundBackend()
    {
        _mixer = new MixingSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(MixSampleRate, MixChannels))
        {
            // Keep returning samples (silence) with no inputs so the device never stops on its own.
            ReadFully = true
        };
        _mixer.MixerInputEnded += OnMixerInputEnded;

        _output = new WaveOutEvent { DesiredLatency = 120 };
        _output.Init(_mixer);
        _output.Play(); // runs continuously; individual alerts are added/removed as mixer inputs
    }

    public string Name => "NAudio Mixer (WaveOutEvent)";

    public bool SupportsConcurrentMixing => true;

    public bool IsBusy
    {
        get { lock (_gate) return _voices.Count > 0; }
    }

    public IPlaybackHandle Play(SoundRequest request)
    {
        if (_disposed || string.IsNullOrEmpty(request.FilePath))
            return NullPlaybackHandle.Instance;

        AudioFileReader? reader = null;
        try
        {
            reader = new AudioFileReader(request.FilePath)
            {
                // Per-voice level (master volume already folded in by the engine).
                Volume = Math.Clamp(request.Volume, 0f, 1f)
            };

            // A loop never reaches EOF (so the mixer never auto-removes it); a one-shot plays once.
            ISampleProvider source = request.Loop
                ? new LoopingSampleProvider(reader)
                : reader;

            ISampleProvider input = ConvertToMixerFormat(source);
            var voice = new VoiceResources(reader);

            // Record BEFORE adding to the mixer so a fast EOF can't fire MixerInputEnded for an
            // untracked input. Do not hold _gate across the AddMixerInput call (lock ordering).
            lock (_gate)
            {
                if (_disposed)
                {
                    voice.Dispose();
                    return NullPlaybackHandle.Instance;
                }
                _voices[input] = voice;
            }

            _mixer.AddMixerInput(input);
            return new MixerVoiceHandle(input);
        }
        catch
        {
            // Bad format / unreadable file / unsupported channel count: start no voice.
            try { reader?.Dispose(); } catch { }
            return NullPlaybackHandle.Instance;
        }
    }

    public void Stop(IPlaybackHandle handle)
    {
        if (handle is not MixerVoiceHandle h)
            return; // NullPlaybackHandle or foreign handle: nothing to stop

        try { _mixer.RemoveMixerInput(h.Input); } catch { }
        Untrack(h.Input)?.Dispose(); // worker thread: dispose inline
    }

    public void Stop()
    {
        // Clear the mixer first (its own lock), then dispose the readers outside _gate.
        try { _mixer.RemoveAllMixerInputs(); } catch { }

        List<VoiceResources> orphans;
        lock (_gate)
        {
            orphans = _voices.Values.ToList();
            _voices.Clear();
        }
        foreach (VoiceResources v in orphans)
            v.Dispose();
    }

    private void OnMixerInputEnded(object? sender, SampleProviderEventArgs e)
    {
        // Audio thread: the mixer already removed this finished input. Untrack it here, but push the
        // reader's Dispose (which closes a file) onto the thread pool so no file I/O runs on the
        // audio callback and risks an underrun in the other voices still mixing.
        VoiceResources? voice = Untrack(e.SampleProvider);
        if (voice is not null)
            ThreadPool.QueueUserWorkItem(static state => ((VoiceResources)state!).Dispose(), voice);
    }

    /// <summary>Remove a voice from the tracking map (idempotent); returns it so the caller disposes it.</summary>
    private VoiceResources? Untrack(ISampleProvider input)
    {
        lock (_gate)
            return _voices.Remove(input, out VoiceResources? found) ? found : null;
    }

    // Convert an arbitrary clip to the mixer's fixed sample rate + channel count. Everything stays
    // 32-bit IEEE float (AudioFileReader, WdlResamplingSampleProvider, the channel converters all
    // emit float), which is exactly what MixingSampleProvider.AddMixerInput requires.
    private static ISampleProvider ConvertToMixerFormat(ISampleProvider input)
    {
        if (input.WaveFormat.SampleRate != MixSampleRate)
            input = new WdlResamplingSampleProvider(input, MixSampleRate);

        int channels = input.WaveFormat.Channels;
        if (channels == MixChannels)
            return input;
        if (channels == 1 && MixChannels == 2)
            return new MonoToStereoSampleProvider(input);
        if (channels == 2 && MixChannels == 1)
            return new StereoToMonoSampleProvider(input);

        // Uncommon layout (e.g. 5.1). Let Play's catch turn this into "start no voice".
        throw new NotSupportedException(
            $"Cannot map {channels}-channel audio to the {MixChannels}-channel mixer.");
    }

    public void Dispose()
    {
        _disposed = true;

        _mixer.MixerInputEnded -= OnMixerInputEnded;
        try { _output.Stop(); } catch { }
        try { _mixer.RemoveAllMixerInputs(); } catch { }

        List<VoiceResources> orphans;
        lock (_gate)
        {
            orphans = _voices.Values.ToList();
            _voices.Clear();
        }
        foreach (VoiceResources v in orphans)
            v.Dispose();

        try { _output.Dispose(); } catch { }
    }

    /// <summary>Owns the disposable resources behind one mixer voice; disposes them at most once.</summary>
    private sealed class VoiceResources
    {
        private AudioFileReader? _reader;

        public VoiceResources(AudioFileReader reader) => _reader = reader;

        public void Dispose()
        {
            AudioFileReader? r = Interlocked.Exchange(ref _reader, null);
            try { r?.Dispose(); } catch { }
        }
    }

    /// <summary>The handle the engine keeps so its watchdog can stop this one voice.</summary>
    private sealed class MixerVoiceHandle : IPlaybackHandle
    {
        public MixerVoiceHandle(ISampleProvider input) => Input = input;
        public ISampleProvider Input { get; }
    }

    /// <summary>
    /// Wraps a source and rewinds it at EOF so a looping alert plays seamlessly and never signals
    /// end-of-stream (so the mixer keeps it until <see cref="Stop()"/> removes it).
    /// </summary>
    private sealed class LoopingSampleProvider : ISampleProvider
    {
        private readonly AudioFileReader _source;

        public LoopingSampleProvider(AudioFileReader source) => _source = source;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _source.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    if (_source.Position == 0)
                        break; // empty source; avoid a tight infinite loop
                    _source.Position = 0; // rewind and keep filling this buffer
                }
                total += read;
            }
            return total;
        }
    }
}
#endif
