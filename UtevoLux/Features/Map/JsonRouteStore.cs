using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace UtevoLux.Features.Map;

/// <summary>
/// JSON-file route store with crash-safe writes (tmp -> copy current to .bak -> atomic move),
/// falling back to the .bak on a corrupt/missing primary. Thread-safe. Defaults to
/// <c>%APPDATA%\UtevoLux\routes.json</c> (renamed for this app); the MapWindow
/// stage may inject a path built from <c>IAppServices.Settings.RootDirectory</c> instead. Clean-room reimplementation.
/// </summary>
public class JsonRouteStore : IRouteStore
{
    private class RouteFile
    {
        public int Version { get; set; } = 1;

        public List<MapRoute> Routes { get; set; } = new List<MapRoute>();
    }

    private readonly string _filePath;

    private readonly object _gate = new object();

    private List<MapRoute> _routes;

    public event EventHandler? RoutesChanged;

    public JsonRouteStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UtevoLux", "routes.json");
        _routes = LoadFromDisk();
    }

    public IReadOnlyList<MapRoute> GetAll()
    {
        lock (_gate)
        {
            return _routes.ToList();
        }
    }

    public void Add(MapRoute route)
    {
        if (route != null && route.Points != null && route.Points.Count != 0)
        {
            lock (_gate)
            {
                _routes.Add(route);
                SaveToDisk();
            }
            this.RoutesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Remove(Guid id)
    {
        bool flag;
        lock (_gate)
        {
            flag = _routes.RemoveAll((MapRoute r) => r.Id == id) > 0;
            if (flag)
            {
                SaveToDisk();
            }
        }
        if (flag)
        {
            this.RoutesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private List<MapRoute> LoadFromDisk()
    {
        List<MapRoute>? list = TryReadFile(_filePath);
        if (list != null)
        {
            return list;
        }
        return TryReadFile(_filePath + ".bak") ?? new List<MapRoute>();
    }

    private static List<MapRoute>? TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            return JsonSerializer.Deserialize<RouteFile>(File.ReadAllText(path))?.Routes;
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
            string contents = JsonSerializer.Serialize(new RouteFile
            {
                Routes = _routes
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
