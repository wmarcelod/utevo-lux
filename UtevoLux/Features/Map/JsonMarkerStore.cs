using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UtevoLux.Features.Map;

/// <summary>
/// JSON-file marker store with crash-safe writes (tmp -> copy current to .bak -> atomic move),
/// falling back to the .bak on a corrupt/missing primary. Thread-safe. Defaults to
/// <c>%APPDATA%\UtevoLux\markers.json</c> (renamed for this app); the MapWindow
/// stage may inject a path built from <c>IAppServices.Settings.RootDirectory</c> instead. Clean-room reimplementation.
/// </summary>
public class JsonMarkerStore : IMarkerStore
{
    private class MarkerFile
    {
        public int Version { get; set; } = 1;

        public List<MapMarker> Markers { get; set; } = new List<MapMarker>();
    }

    private readonly string _filePath;

    private readonly object _gate = new object();

    private List<MapMarker> _markers;

    public event EventHandler? MarkersChanged;

    public JsonMarkerStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UtevoLux", "markers.json");
        _markers = LoadFromDisk();
    }

    public IReadOnlyList<MapMarker> GetAll()
    {
        lock (_gate)
        {
            return _markers.ToList();
        }
    }

    public IEnumerable<MapMarker> GetForFloor(int z)
    {
        lock (_gate)
        {
            return _markers.Where((MapMarker m) => m.Z == z).ToList();
        }
    }

    public void Add(MapMarker marker)
    {
        if (marker != null)
        {
            lock (_gate)
            {
                _markers.Add(marker);
                SaveToDisk();
            }
            this.MarkersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Update(MapMarker marker)
    {
        if (marker == null)
        {
            return;
        }
        lock (_gate)
        {
            int num = _markers.FindIndex((MapMarker m) => m.Id == marker.Id);
            if (num < 0)
            {
                return;
            }
            _markers[num] = marker;
            SaveToDisk();
        }
        this.MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid id)
    {
        bool flag;
        lock (_gate)
        {
            flag = _markers.RemoveAll((MapMarker m) => m.Id == id) > 0;
            if (flag)
            {
                SaveToDisk();
            }
        }
        if (flag)
        {
            this.MarkersChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private List<MapMarker> LoadFromDisk()
    {
        List<MapMarker>? list = TryReadFile(_filePath);
        if (list != null)
        {
            return list;
        }
        return TryReadFile(_filePath + ".bak") ?? new List<MapMarker>();
    }

    private static List<MapMarker>? TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            return JsonSerializer.Deserialize<MarkerFile>(File.ReadAllText(path))?.Markers;
        }
        catch
        {
            return null;
        }
    }

    private void SaveToDisk()
    {
        try
        {
            string? directoryName = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
            string contents = JsonSerializer.Serialize(new MarkerFile
            {
                Markers = _markers
            }, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            string text = _filePath + ".tmp";
            File.WriteAllText(text, contents);
            if (File.Exists(_filePath))
            {
                File.Copy(_filePath, _filePath + ".bak", overwrite: true);
            }
            File.Move(text, _filePath, overwrite: true);
        }
        catch
        {
        }
    }
}
