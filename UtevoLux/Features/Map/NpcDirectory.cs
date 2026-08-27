using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtevoLux.Features.Map;

/// <summary>
/// Searchable directory of NPCs loaded from <c>Resources/map/npcs.json</c>. Each entry accepts
/// either a <c>p</c> array of [x,y,z] triples or a single x/y/z. Search ranks exact &gt; prefix
/// &gt; substring, each block alphabetical. Ported faithfully from the original TibiaVision.
/// </summary>
public class NpcDirectory
{
    private sealed class NpcDto
    {
        [JsonPropertyName("n")]
        public string? N { get; set; }

        [JsonPropertyName("l")]
        public string? L { get; set; }

        [JsonPropertyName("p")]
        public List<List<int>>? P { get; set; }

        [JsonPropertyName("x")]
        public int? X { get; set; }

        [JsonPropertyName("y")]
        public int? Y { get; set; }

        [JsonPropertyName("z")]
        public int? Z { get; set; }
    }

    private sealed class FileDto
    {
        [JsonPropertyName("npcs")]
        public List<NpcDto>? Npcs { get; set; }
    }

    private readonly List<NpcEntry> _entries;

    public int Count => _entries.Count;

    private NpcDirectory(List<NpcEntry> entries)
    {
        _entries = entries;
    }

    public static NpcDirectory LoadDefault()
    {
        string[] array = new string[2]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "map", "npcs.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "map", "npcs.json")
        };
        foreach (string path in array)
        {
            try
            {
                if (File.Exists(path))
                {
                    return FromJson(File.ReadAllText(path));
                }
            }
            catch
            {
            }
        }
        return new NpcDirectory(new List<NpcEntry>());
    }

    public static NpcDirectory FromJson(string json)
    {
        List<NpcEntry> list = new List<NpcEntry>();
        try
        {
            FileDto? fileDto = JsonSerializer.Deserialize<FileDto>(json);
            if (fileDto?.Npcs != null)
            {
                foreach (NpcDto npc in fileDto.Npcs)
                {
                    if (string.IsNullOrWhiteSpace(npc.N))
                    {
                        continue;
                    }
                    List<NpcPosition> list2 = new List<NpcPosition>();
                    if (npc.P != null)
                    {
                        foreach (List<int> item in npc.P)
                        {
                            if (item != null && item.Count == 3 && item[2] >= 0 && item[2] < 16)
                            {
                                list2.Add(new NpcPosition(item[0], item[1], item[2]));
                            }
                        }
                    }
                    else if (npc.X.HasValue && npc.Y.HasValue && npc.Z.HasValue && npc.Z >= 0 && npc.Z < 16)
                    {
                        list2.Add(new NpcPosition(npc.X.Value, npc.Y.Value, npc.Z.Value));
                    }
                    if (list2.Count != 0)
                    {
                        list.Add(new NpcEntry
                        {
                            Name = npc.N.Trim(),
                            Location = (npc.L?.Trim() ?? ""),
                            Positions = list2
                        });
                    }
                }
            }
        }
        catch
        {
        }
        return new NpcDirectory(list);
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
