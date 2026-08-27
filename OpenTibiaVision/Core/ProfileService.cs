using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenTibiaVision.Core;

/// <summary>
/// Default <see cref="IProfileService"/>. Profiles live at {root}\Profiles\{name}.json; the
/// active profile name is a "last_profile" key in the global settings store. Each profile is a
/// lazily-created <see cref="SettingsStore"/> (atomic + debounced). Switching disposes the old
/// store (flushing it) and opens the new one.
/// </summary>
public sealed class ProfileService : IProfileService, IDisposable
{
    private const string DefaultProfileName = "Padrao";
    private const string LastProfileKey = "last_profile";

    private readonly ISettingsStore _global;
    private readonly string _profilesDir;
    private readonly object _gate = new();

    private SettingsStore _current;
    private string _activeName;

    public event Action? ActiveProfileChanged;

    public ProfileService(ISettingsStore global, string profilesDir)
    {
        _global = global;
        _profilesDir = profilesDir;
        Directory.CreateDirectory(_profilesDir);

        string last = _global.Get(LastProfileKey, DefaultProfileName);
        if (!ProfileExists(last))
            last = Profiles.FirstOrDefault() ?? DefaultProfileName;

        _activeName = last;
        _current = OpenStore(last);
        _global.Set(LastProfileKey, _activeName);
    }

    public IReadOnlyList<string> Profiles
    {
        get
        {
            try
            {
                var names = Directory.EnumerateFiles(_profilesDir, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (names.Count == 0)
                    names.Add(DefaultProfileName);
                return names;
            }
            catch
            {
                return new[] { DefaultProfileName };
            }
        }
    }

    public string ActiveProfile
    {
        get { lock (_gate) return _activeName; }
    }

    public ISettingsStore Current
    {
        get { lock (_gate) return _current; }
    }

    public void Create(string name)
    {
        name = Sanitize(name);
        if (ProfileExists(name))
            return;
        // Touch the file so it appears in the list.
        using var store = new SettingsStore(PathFor(name));
        store.Set("created_utc", DateTime.UtcNow);
        store.Flush();
    }

    public void Switch(string name)
    {
        name = Sanitize(name);
        lock (_gate)
        {
            if (string.Equals(name, _activeName, StringComparison.OrdinalIgnoreCase) && ProfileExists(name))
                return;

            _current.Flush();
            _current.Dispose();

            _activeName = name;
            _current = OpenStore(name);
        }

        _global.Set(LastProfileKey, name);
        ActiveProfileChanged?.Invoke();
    }

    public void Delete(string name)
    {
        name = Sanitize(name);
        lock (_gate)
        {
            if (string.Equals(name, _activeName, StringComparison.OrdinalIgnoreCase))
                return; // never delete the active profile
        }

        TryDeleteFiles(name);
    }

    public void Rename(string oldName, string newName)
    {
        oldName = Sanitize(oldName);
        newName = Sanitize(newName);
        if (!ProfileExists(oldName) || ProfileExists(newName))
            return;

        bool renamingActive;
        lock (_gate)
            renamingActive = string.Equals(oldName, _activeName, StringComparison.OrdinalIgnoreCase);

        if (renamingActive)
        {
            lock (_gate)
            {
                _current.Flush();
                _current.Dispose();
            }
        }

        try { File.Move(PathFor(oldName), PathFor(newName), overwrite: false); } catch { /* best effort */ }

        if (renamingActive)
        {
            lock (_gate)
            {
                _activeName = newName;
                _current = OpenStore(newName);
            }
            _global.Set(LastProfileKey, newName);
            ActiveProfileChanged?.Invoke();
        }
    }

    private SettingsStore OpenStore(string name) => new(PathFor(name));

    private string PathFor(string name) => Path.Combine(_profilesDir, name + ".json");

    private bool ProfileExists(string name) => File.Exists(PathFor(name));

    private void TryDeleteFiles(string name)
    {
        foreach (string p in new[] { PathFor(name), PathFor(name) + ".bak", PathFor(name) + ".tmp" })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* ignore */ }
        }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DefaultProfileName;
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _current.Flush();
            _current.Dispose();
        }
    }
}
