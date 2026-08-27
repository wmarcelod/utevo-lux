using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenTibiaVision.Core;

/// <summary>
/// Default <see cref="ISettingsStore"/>: a single JSON object of { key: value }, held in
/// memory and persisted atomically with a 400 ms debounce. Per-key access is O(1) in memory;
/// the whole object is rewritten on flush (settings are tiny, so this is cheaper than N files).
/// Thread-safe for concurrent Get/Set.
/// </summary>
public sealed class SettingsStore : ISettingsStore, IDisposable
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly AtomicJsonFile _file;
    private readonly Dictionary<string, JsonElement> _map;

    public SettingsStore(string filePath)
    {
        _file = new AtomicJsonFile(filePath);
        _map = LoadMap(_file);
    }

    /// <summary>The default app store at %APPDATA%\OpenTibiaVision\settings.json.</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenTibiaVision");

    public static SettingsStore CreateDefault() =>
        new(Path.Combine(DefaultRoot, "settings.json"));

    public string RootDirectory => Path.GetDirectoryName(_file.Path) ?? DefaultRoot;
    public string FilePath => _file.Path;

    private static Dictionary<string, JsonElement> LoadMap(AtomicJsonFile file)
    {
        try
        {
            string? raw = file.ReadRaw();
            if (string.IsNullOrWhiteSpace(raw))
                return new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(raw);
            return map is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(map, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
    }

    public T Get<T>(string key, T fallback)
        => TryGet(key, out T value) ? value : fallback;

    public bool TryGet<T>(string key, out T value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out JsonElement element))
            {
                try
                {
                    T? deserialized = element.Deserialize<T>();
                    if (deserialized is not null)
                    {
                        value = deserialized;
                        return true;
                    }
                }
                catch
                {
                    // shape drifted; treat as missing
                }
            }
        }

        value = default!;
        return false;
    }

    public void Set<T>(string key, T value)
    {
        if (value is null)
        {
            Remove(key);
            return;
        }

        lock (_gate)
        {
            _map[key] = JsonSerializer.SerializeToElement(value);
            Persist();
        }
    }

    public void Remove(string key)
    {
        lock (_gate)
        {
            if (_map.Remove(key))
                Persist();
        }
    }

    public bool Contains(string key)
    {
        lock (_gate)
            return _map.ContainsKey(key);
    }

    public void Flush() => _file.Flush();

    private void Persist()
    {
        // Called under _gate.
        string json = JsonSerializer.Serialize(_map, WriteOptions);
        _file.QueueWrite(json);
    }

    public void Dispose() => _file.Dispose();
}
