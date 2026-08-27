using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UtevoLux.Features.Map;

/// <summary>
/// Searchable directory of monster spawns, decoded from <c>Resources/map/monster_spawns.dat</c>
/// via <see cref="SpawnDataCodec"/>. Search ranks exact &gt; prefix &gt; substring, each block
/// alphabetical.
/// </summary>
public class MonsterSpawnDirectory
{
    private readonly List<NpcEntry> _entries;

    public int Count => _entries.Count;

    private MonsterSpawnDirectory(List<NpcEntry> entries)
    {
        _entries = entries;
    }

    public static MonsterSpawnDirectory LoadDefault()
    {
        // PREFER the live third-party creature-spawn dataset from tibiaroute.com when it is
        // available this launch (freshly fetched, or the last cached copy). tibiaroute.com is a
        // THIRD-PARTY source whose shape may change without notice; TibiaRouteSpawnProvider is
        // written to never throw, so on any problem Load() returns null and we transparently fall
        // back to the bundled monster_spawns.dat below. (Fetch politeness/caching policy lives in
        // TibiaRouteSpawnProvider.)
        try
        {
            IReadOnlyList<NpcEntry>? live = TibiaRouteSpawnProvider.Shared.Load();
            if (live is { Count: > 0 })
            {
                return new MonsterSpawnDirectory(live.ToList());
            }
        }
        catch
        {
            // Defensive only: Load() is designed never to throw. Never let a live-source hiccup
            // cost the map its bundled .dat fallback.
        }

        string[] array = new string[2]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "map", "monster_spawns.dat"),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "map", "monster_spawns.dat")
        };
        foreach (string path in array)
        {
            try
            {
                if (File.Exists(path))
                {
                    return FromBytes(File.ReadAllBytes(path));
                }
            }
            catch
            {
            }
        }
        return new MonsterSpawnDirectory(new List<NpcEntry>());
    }

    public static MonsterSpawnDirectory FromBytes(byte[] data)
    {
        List<NpcEntry> list = new List<NpcEntry>();
        try
        {
            foreach (NpcEntry item in SpawnDataCodec.Decode(data))
            {
                list.Add(new NpcEntry
                {
                    Name = item.Name,
                    Location = ((item.Positions.Count == 1) ? "1 spawn point" : $"{item.Positions.Count:N0} spawn points"),
                    Positions = item.Positions,
                    IsSpawnData = true
                });
            }
        }
        catch
        {
            list.Clear();
        }
        return new MonsterSpawnDirectory(list);
    }

    public IReadOnlyList<NpcEntry> Search(string query, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(query) || max <= 0)
        {
            return Array.Empty<NpcEntry>();
        }
        string value = query.Trim();
        List<NpcEntry> list = new List<NpcEntry>();
        List<NpcEntry> list2 = new List<NpcEntry>();
        List<NpcEntry> list3 = new List<NpcEntry>();
        foreach (NpcEntry entry in _entries)
        {
            if (entry.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                list.Add(entry);
            }
            else if (entry.Name.StartsWith(value, StringComparison.OrdinalIgnoreCase))
            {
                list2.Add(entry);
            }
            else if (entry.Name.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                list3.Add(entry);
            }
        }
        return list.OrderBy<NpcEntry, string>((NpcEntry e) => e.Name, StringComparer.OrdinalIgnoreCase).Concat(list2.OrderBy<NpcEntry, string>((NpcEntry e) => e.Name, StringComparer.OrdinalIgnoreCase)).Concat(list3.OrderBy<NpcEntry, string>((NpcEntry e) => e.Name, StringComparer.OrdinalIgnoreCase))
            .Take(max)
            .ToList();
    }
}
