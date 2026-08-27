using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UtevoLux.Features.Map;

/// <summary>
/// Searchable directory of rare creatures, merged from the auto-generated
/// <c>Resources/map/rare_creatures.json</c> and a hand-curated <c>rare_creatures_manual.json</c>
/// (later files override by name). Search ranks exact &gt; prefix &gt; substring, each block
/// alphabetical. Ported faithfully from the original TibiaVision.
/// </summary>
public class RareCreatureDirectory
{
    private sealed class CreatureDto
    {
        [JsonPropertyName("n")]
        public string? N { get; set; }

        [JsonPropertyName("l")]
        public string? L { get; set; }

        [JsonPropertyName("p")]
        public List<List<int>>? P { get; set; }
    }

    private sealed class FileDto
    {
        [JsonPropertyName("creatures")]
        public List<CreatureDto>? Creatures { get; set; }
    }

    private readonly List<NpcEntry> _entries;

    public int Count => _entries.Count;

    private RareCreatureDirectory(List<NpcEntry> entries)
    {
        _entries = entries;
    }

    public static RareCreatureDirectory LoadDefault()
    {
        return FromContents(ReadFile("rare_creatures.json"), ReadFile("rare_creatures_manual.json"));
    }

    public static RareCreatureDirectory FromContents(params string?[] jsonFiles)
    {
        Dictionary<string, NpcEntry> dictionary = new Dictionary<string, NpcEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (string? text in jsonFiles)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }
            foreach (NpcEntry item in FromJson(text))
            {
                dictionary[item.Name] = item;
            }
        }
        return new RareCreatureDirectory(dictionary.Values.ToList());
    }

    private static string? ReadFile(string fileName)
    {
        string[] array = new string[2]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "map", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "map", fileName)
        };
        foreach (string path in array)
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch
            {
            }
        }
        return null;
    }

    public static List<NpcEntry> FromJson(string json)
    {
        List<NpcEntry> list = new List<NpcEntry>();
        try
        {
            FileDto? fileDto = JsonSerializer.Deserialize<FileDto>(json);
            if (fileDto?.Creatures != null)
            {
                foreach (CreatureDto creature in fileDto.Creatures)
                {
                    if (string.IsNullOrWhiteSpace(creature.N) || creature.P == null)
                    {
                        continue;
                    }
                    List<NpcPosition> list2 = new List<NpcPosition>();
                    foreach (List<int> item in creature.P)
                    {
                        if (item != null && item.Count == 3 && item[2] >= 0 && item[2] < 16)
                        {
                            list2.Add(new NpcPosition(item[0], item[1], item[2]));
                        }
                    }
                    if (list2.Count != 0)
                    {
                        list.Add(new NpcEntry
                        {
                            Name = creature.N.Trim(),
                            Location = (creature.L?.Trim() ?? ""),
                            Positions = list2
                        });
                    }
                }
            }
        }
        catch
        {
        }
        return list;
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
