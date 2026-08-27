using System;
using System.IO;

namespace UtevoLux.Features.Audio;

/// <summary>
/// Synthesizes tiny PCM WAV files so the feature has working alert sounds with zero
/// user-supplied audio. The generated files are plain 16-bit mono WAV — playable by BOTH the
/// NAudio backend and the WPF MediaPlayer fallback — cached under the settings root so they are
/// written once and reused. This keeps the sound library non-empty out of the box.
/// </summary>
public static class BeepSynth
{
    private const int SampleRate = 44_100;

    /// <summary>
    /// Ensures a WAV for the given tone exists at a stable path under <paramref name="cacheDir"/>
    /// and returns that path. Regenerates if the file is missing (e.g. cache cleared).
    /// </summary>
    public static string EnsureTone(string cacheDir, string key, double frequencyHz, int durationMs)
    {
        Directory.CreateDirectory(cacheDir);
        string safe = MakeSafe(key);
        string path = Path.Combine(cacheDir, $"beep_{safe}.wav");

        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 44)
                return path;

            WriteTone(path, frequencyHz, durationMs);
        }
        catch
        {
            // If synthesis fails (locked/full disk), return the path anyway; the backend will
            // simply no-op on a missing file rather than crash.
        }

        return path;
    }

    private static void WriteTone(string path, double frequencyHz, int durationMs)
    {
        int samples = Math.Max(1, (int)(SampleRate * (durationMs / 1000.0)));
        short[] pcm = new short[samples];

        // Soft attack/decay envelope so the tone does not click.
        int fade = Math.Min(samples / 4, SampleRate / 200 + 1);
        double twoPiF = 2.0 * Math.PI * frequencyHz;

        for (int i = 0; i < samples; i++)
        {
            double env = 1.0;
            if (i < fade) env = i / (double)fade;
            else if (i > samples - fade) env = (samples - i) / (double)fade;

            double sample = Math.Sin(twoPiF * i / SampleRate) * env * 0.6;
            pcm[i] = (short)(sample * short.MaxValue);
        }

        string tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new BinaryWriter(fs))
        {
            int byteCount = pcm.Length * sizeof(short);
            const short channels = 1;
            const short bitsPerSample = 16;
            int blockAlign = channels * (bitsPerSample / 8);
            int byteRate = SampleRate * blockAlign;

            // RIFF header
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + byteCount);
            w.Write(new[] { 'W', 'A', 'V', 'E' });

            // fmt chunk
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);                 // PCM fmt chunk size
            w.Write((short)1);           // audio format = PCM
            w.Write(channels);
            w.Write(SampleRate);
            w.Write(byteRate);
            w.Write((short)blockAlign);
            w.Write(bitsPerSample);

            // data chunk
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(byteCount);
            foreach (short s in pcm)
                w.Write(s);
        }

        File.Move(tmp, path, overwrite: true);
    }

    private static string MakeSafe(string key)
    {
        Span<char> buf = stackalloc char[key.Length];
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            buf[i] = char.IsLetterOrDigit(c) ? c : '_';
        }
        return new string(buf);
    }
}
