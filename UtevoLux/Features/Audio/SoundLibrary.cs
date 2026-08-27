using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using UtevoLux.Core;

namespace UtevoLux.Features.Audio;

/// <summary>
/// The named-sound catalog. Persists a list of <see cref="SoundEntry"/> in the shared settings
/// store and guarantees at least the built-in synthesized beeps exist (so alerts work out of the
/// box with no user files). Built-in WAVs are (re)generated under {settings-root}\Sounds via
/// <see cref="BeepSynth"/>; user entries reference files the user picked. Exposed as an
/// <see cref="ObservableCollection{T}"/> so the page binds to it directly.
/// </summary>
public sealed class SoundLibrary
{
    private const string SoundsKey = "audio.sounds";

    private readonly ISettingsStore _settings;
    private readonly string _cacheDir;

    public SoundLibrary(ISettingsStore settings)
    {
        _settings = settings;
        _cacheDir = Path.Combine(settings.RootDirectory, "Sounds");
        Load();
    }

    public ObservableCollection<SoundEntry> Entries { get; } = new();

    private void Load()
    {
        List<SoundEntry> stored = _settings.Get(SoundsKey, new List<SoundEntry>());

        if (stored.Count == 0)
            stored = DefaultEntries();

        foreach (SoundEntry entry in stored)
        {
            if (entry.BuiltIn)
                EnsureBuiltInFile(entry);
            Entries.Add(entry);
        }

        // Persist any freshly-seeded defaults / regenerated built-in paths.
        Save();
    }

    private static List<SoundEntry> DefaultEntries() => new()
    {
        new SoundEntry { Name = "Beep agudo",  BuiltIn = true, BuiltInFrequencyHz = 1180, BuiltInDurationMs = 200 },
        new SoundEntry { Name = "Beep grave",  BuiltIn = true, BuiltInFrequencyHz = 620,  BuiltInDurationMs = 260 },
        new SoundEntry { Name = "Alerta duplo", BuiltIn = true, BuiltInFrequencyHz = 880, BuiltInDurationMs = 460 }
    };

    private void EnsureBuiltInFile(SoundEntry entry)
    {
        entry.FilePath = BeepSynth.EnsureTone(
            _cacheDir, entry.Id, entry.BuiltInFrequencyHz, entry.BuiltInDurationMs);
    }

    public void Save() => _settings.Set(SoundsKey, Entries.ToList());

    /// <summary>Resolve a sound id to a playable absolute path, or "" if unknown/missing.</summary>
    public string ResolvePath(string soundId)
    {
        if (string.IsNullOrEmpty(soundId))
            return "";

        SoundEntry? entry = Entries.FirstOrDefault(e => e.Id == soundId);
        if (entry is null)
            return "";

        if (entry.BuiltIn && (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath)))
            EnsureBuiltInFile(entry);

        return entry.FilePath;
    }

    public SoundEntry? Find(string soundId) => Entries.FirstOrDefault(e => e.Id == soundId);

    /// <summary>The id used when a timer has no explicit sound assigned.</summary>
    public string DefaultSoundId => Entries.Count > 0 ? Entries[0].Id : "";

    public SoundEntry Add(string name, string filePath)
    {
        var entry = new SoundEntry { Name = name, FilePath = filePath, BuiltIn = false };
        Entries.Add(entry);
        Save();
        return entry;
    }

    public void Remove(SoundEntry entry)
    {
        if (Entries.Remove(entry))
            Save();
    }
}
