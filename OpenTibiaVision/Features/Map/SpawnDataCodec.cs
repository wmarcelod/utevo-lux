using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Codec for the packed monster-spawn dataset (<c>Resources/map/monster_spawns.dat</c>). The on-disk
/// container is AES-256-CBC (PKCS7, IV prepended) over gzip over a compact binary "TVSP" record
/// stream. The AES key is derived from three embedded 16-byte constants (see <see cref="BuildKey"/>).
/// Ported faithfully from the original TibiaVision so the copied .dat decodes byte-for-byte; reuse
/// of the original decode path is sanctioned for this personal-use fork.
/// Also parses the JSON exclusion / monster-locations formats used to build the .dat.
/// </summary>
public static class SpawnDataCodec
{
    private sealed class RawSpawn
    {
        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }

        [JsonPropertyName("z")]
        public int Z { get; set; }

        [JsonPropertyName("spawntime")]
        public int SpawnTime { get; set; }
    }

    private sealed class RawCreature
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("spawns")]
        public List<RawSpawn>? Spawns { get; set; }
    }

    public sealed record SpawnExclusion(string Name, int X1, int X2, int Y1, int Y2, int Z1, int Z2)
    {
        public bool Contains(int x, int y, int z)
        {
            if (x >= X1 && x <= X2 && y >= Y1 && y <= Y2 && z >= Z1)
            {
                return z <= Z2;
            }
            return false;
        }
    }

    private sealed class ExclusionDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("x1")]
        public int X1 { get; set; }

        [JsonPropertyName("x2")]
        public int X2 { get; set; }

        [JsonPropertyName("y1")]
        public int Y1 { get; set; }

        [JsonPropertyName("y2")]
        public int Y2 { get; set; }

        [JsonPropertyName("z1")]
        public int Z1 { get; set; }

        [JsonPropertyName("z2")]
        public int Z2 { get; set; }
    }

    private sealed class ExclusionFileDto
    {
        [JsonPropertyName("exclusions")]
        public List<ExclusionDto>? Exclusions { get; set; }

        [JsonPropertyName("excludedCreatures")]
        public List<string>? ExcludedCreatures { get; set; }
    }

    private const uint Magic = 1347638868u;

    private const byte Version = 1;

    public static byte[] Encode(IReadOnlyList<NpcEntry> creatures)
    {
        if (creatures == null)
        {
            throw new ArgumentNullException("creatures");
        }
        using MemoryStream memoryStream = new MemoryStream();
        using (BinaryWriter binaryWriter = new BinaryWriter(memoryStream, Encoding.UTF8, leaveOpen: true))
        {
            binaryWriter.Write(1347638868u);
            binaryWriter.Write((byte)1);
            binaryWriter.Write(creatures.Count);
            foreach (NpcEntry creature in creatures)
            {
                binaryWriter.Write(creature.Name ?? "");
                binaryWriter.Write(creature.Positions.Count);
                foreach (NpcPosition position in creature.Positions)
                {
                    binaryWriter.Write((ushort)Math.Clamp(position.X, 0, 65535));
                    binaryWriter.Write((ushort)Math.Clamp(position.Y, 0, 65535));
                    binaryWriter.Write((byte)Math.Clamp(position.Z, 0, 255));
                    binaryWriter.Write((ushort)Math.Clamp(position.SpawnTimeSeconds, 0, 65535));
                }
            }
        }
        byte[] array = Gzip(memoryStream.ToArray());
        using Aes aes = CreateAes();
        aes.GenerateIV();
        using MemoryStream memoryStream2 = new MemoryStream();
        memoryStream2.Write(aes.IV, 0, aes.IV.Length);
        using (CryptoStream cryptoStream = new CryptoStream(memoryStream2, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            cryptoStream.Write(array, 0, array.Length);
        }
        return memoryStream2.ToArray();
    }

    public static List<NpcEntry> Decode(byte[] data)
    {
        if (data == null || data.Length < 17)
        {
            throw new InvalidDataException("Spawn data too short.");
        }
        using Aes aes = CreateAes();
        byte[] array = new byte[16];
        Array.Copy(data, array, 16);
        aes.IV = array;
        byte[] data2;
        using (MemoryStream stream = new MemoryStream(data, 16, data.Length - 16))
        {
            using CryptoStream cryptoStream = new CryptoStream(stream, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using MemoryStream memoryStream = new MemoryStream();
            cryptoStream.CopyTo(memoryStream);
            data2 = memoryStream.ToArray();
        }
        byte[] buffer = Gunzip(data2);
        List<NpcEntry> list = new List<NpcEntry>();
        using BinaryReader binaryReader = new BinaryReader(new MemoryStream(buffer), Encoding.UTF8);
        if (binaryReader.ReadUInt32() != 1347638868)
        {
            throw new InvalidDataException("Bad spawn data magic.");
        }
        if (binaryReader.ReadByte() != 1)
        {
            throw new InvalidDataException("Unknown spawn data version.");
        }
        int num = binaryReader.ReadInt32();
        if (num < 0 || num > 100000)
        {
            throw new InvalidDataException("Implausible creature count.");
        }
        for (int i = 0; i < num; i++)
        {
            string text = binaryReader.ReadString();
            int num2 = binaryReader.ReadInt32();
            if (num2 < 0 || num2 > 1000000)
            {
                throw new InvalidDataException("Implausible spawn count.");
            }
            List<NpcPosition> list2 = new List<NpcPosition>(num2);
            for (int j = 0; j < num2; j++)
            {
                int x = binaryReader.ReadUInt16();
                int y = binaryReader.ReadUInt16();
                int num3 = binaryReader.ReadByte();
                int spawnTimeSeconds = binaryReader.ReadUInt16();
                if (num3 < 16)
                {
                    list2.Add(new NpcPosition(x, y, num3, spawnTimeSeconds));
                }
            }
            if (!string.IsNullOrWhiteSpace(text) && list2.Count != 0)
            {
                list.Add(new NpcEntry
                {
                    Name = text,
                    Location = "",
                    Positions = list2
                });
            }
        }
        return list;
    }

    public static List<string> ParseExcludedCreaturesJson(string json)
    {
        return (from n in (JsonSerializer.Deserialize<ExclusionFileDto>(json) ?? throw new InvalidDataException("Exclusion file is not a JSON object.")).ExcludedCreatures?.Where((string n) => !string.IsNullOrWhiteSpace(n))
            select n.Trim()).ToList() ?? new List<string>();
    }

    public static List<SpawnExclusion> ParseExclusionsJson(string json)
    {
        ExclusionFileDto? obj = JsonSerializer.Deserialize<ExclusionFileDto>(json) ?? throw new InvalidDataException("Exclusion file is not a JSON object.");
        List<SpawnExclusion> list = new List<SpawnExclusion>();
        foreach (ExclusionDto item in obj.Exclusions ?? new List<ExclusionDto>())
        {
            list.Add(new SpawnExclusion(item.Name ?? "", Math.Min(item.X1, item.X2), Math.Max(item.X1, item.X2), Math.Min(item.Y1, item.Y2), Math.Max(item.Y1, item.Y2), Math.Min(item.Z1, item.Z2), Math.Max(item.Z1, item.Z2)));
        }
        return list;
    }

    public static List<NpcEntry> ParseMonsterLocationsJson(string json)
    {
        int excludedCount;
        return ParseMonsterLocationsJson(json, null, out excludedCount);
    }

    public static List<NpcEntry> ParseMonsterLocationsJson(string json, IReadOnlyList<SpawnExclusion>? exclusions, out int excludedCount)
    {
        return ParseMonsterLocationsJson(json, exclusions, null, out excludedCount);
    }

    public static List<NpcEntry> ParseMonsterLocationsJson(string json, IReadOnlyList<SpawnExclusion>? exclusions, IReadOnlyCollection<string>? excludedCreatures, out int excludedCount)
    {
        HashSet<string>? hashSet = ((excludedCreatures == null) ? null : new HashSet<string>(excludedCreatures, StringComparer.OrdinalIgnoreCase));
        List<RawCreature>? obj = JsonSerializer.Deserialize<List<RawCreature>>(json) ?? throw new InvalidDataException("Dataset is not a JSON array.");
        excludedCount = 0;
        Dictionary<string, List<NpcPosition>> dictionary = new Dictionary<string, List<NpcPosition>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (RawCreature item in obj)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || item.Spawns == null)
            {
                continue;
            }
            string text = item.Name.Trim();
            if (hashSet != null && hashSet.Contains(text))
            {
                excludedCount += item.Spawns.Count;
                continue;
            }
            if (!dictionary.TryGetValue(text, out var value))
            {
                value = (dictionary[text] = new List<NpcPosition>());
                dictionary2[text] = text;
            }
            foreach (RawSpawn spawn in item.Spawns)
            {
                if (spawn == null || spawn.X < 0 || spawn.X > 65535 || spawn.Y < 0 || spawn.Y > 65535 || spawn.Z < 0 || spawn.Z >= 16)
                {
                    continue;
                }
                if (exclusions != null)
                {
                    bool flag = false;
                    foreach (SpawnExclusion exclusion in exclusions)
                    {
                        if (exclusion.Contains(spawn.X, spawn.Y, spawn.Z))
                        {
                            flag = true;
                            break;
                        }
                    }
                    if (flag)
                    {
                        excludedCount++;
                        continue;
                    }
                }
                value.Add(new NpcPosition(spawn.X, spawn.Y, spawn.Z, Math.Clamp(spawn.SpawnTime, 0, 65535)));
            }
        }
        List<NpcEntry> list2 = new List<NpcEntry>(dictionary.Count);
        foreach (KeyValuePair<string, List<NpcPosition>> item2 in dictionary)
        {
            if (item2.Value.Count != 0)
            {
                list2.Add(new NpcEntry
                {
                    Name = dictionary2[item2.Key],
                    Location = "",
                    Positions = item2.Value
                });
            }
        }
        return list2;
    }

    private static Aes CreateAes()
    {
        Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = BuildKey();
        return aes;
    }

    private static byte[] BuildKey()
    {
        byte[] array = new byte[16]
        {
            84, 105, 98, 105, 97, 86, 105, 115, 33, 131,
            93, 196, 15, 153, 106, 178
        };
        byte[] array2 = new byte[16]
        {
            62, 167, 16, 245, 136, 44, 209, 71, 91, 233,
            3, 122, 198, 20, 144, 47
        };
        byte[] array3 = new byte[16]
        {
            109, 11, 226, 145, 56, 175, 84, 195, 126, 18,
            216, 101, 10, 241, 76, 135
        };
        byte[] array4 = new byte[32];
        for (int i = 0; i < 16; i++)
        {
            array4[i] = (byte)(array[i] ^ array3[i]);
            array4[16 + i] = (byte)(array2[i] ^ array3[15 - i]);
        }
        return array4;
    }

    private static byte[] Gzip(byte[] data)
    {
        using MemoryStream memoryStream = new MemoryStream();
        using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            gZipStream.Write(data, 0, data.Length);
        }
        return memoryStream.ToArray();
    }

    private static byte[] Gunzip(byte[] data)
    {
        using GZipStream gZipStream = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using MemoryStream memoryStream = new MemoryStream();
        gZipStream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}
