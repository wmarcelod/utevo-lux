using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace UtevoLux.Features.Map;

/// <summary>
/// Resolves a creature name -> its loot item names using the community TibiaData API
/// (api.tibiadata.com/v4). The map's loot panel pairs each returned name with an icon from
/// <see cref="ItemSpriteProvider"/> (our extracted Resources/items bank).
///
/// Why TibiaData: the game client ships creature OUTFITS by looktype only (no loot, no names in
/// the client — verified against appearances.dat). Loot tables are game state; TibiaData mirrors
/// them from TibiaWiki. This provider is the runtime bridge from a creature name to its drops.
///
/// POLITENESS / RESILIENCE (mirrors <see cref="TibiaRouteSpawnProvider"/>):
///   * The creature index (name -> race slug) is fetched at most ONCE per launch, then reused.
///   * Per-creature loot is fetched lazily, only for creatures the user actually opens, and cached
///     to disk FOREVER (loot rarely changes) so a creature is fetched across the network only once.
///   * Identifying, cache-friendly User-Agent. NEVER throws to callers: any failure (offline,
///     timeout, non-200, parse) yields null and the panel shows "loot unavailable".
///
/// Return contract of <see cref="GetLootNamesAsync"/>:
///   * non-empty list  -> known loot names.
///   * empty list      -> KNOWN to have no (or no matchable) loot; cached, will not refetch.
///   * null            -> could not determine right now (offline / transient); NOT cached.
/// </summary>
public sealed class CreatureLootProvider
{
    public static CreatureLootProvider Shared { get; } = new CreatureLootProvider();

    private const string CreaturesEndpoint = "https://api.tibiadata.com/v4/creatures";
    private const string CreatureEndpointBase = "https://api.tibiadata.com/v4/creature/";
    private const string UserAgent = "UtevoLux/0.1 (+personal map tool; cache-friendly)";

    // Refetch the creature index if the cached copy is older than this (loot tables shift slowly).
    private static readonly TimeSpan CreaturesMaxAge = TimeSpan.FromDays(30);

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UtevoLux");
    private static readonly string CreaturesCachePath = Path.Combine(CacheDir, "tibiadata_creatures.json");
    private static readonly string LootCachePath = Path.Combine(CacheDir, "tibiadata_loot.json");

    private readonly object _cacheGate = new();
    private readonly SemaphoreSlim _fetchGate = new(1, 1);

    // norm(name)/norm(race) -> race slug for the creature detail endpoint.
    private readonly Dictionary<string, string> _raceByNorm = new(StringComparer.Ordinal);
    // norm(name) -> loot names (empty array = known none). Persisted to LootCachePath.
    private readonly Dictionary<string, string[]> _lootByNorm = new(StringComparer.Ordinal);

    private bool _creaturesAttempted;
    private bool _lootLoaded;
    private HttpClient? _client;

    private CreatureLootProvider()
    {
    }

    /// <summary>
    /// Loot item names for <paramref name="creatureName"/>, or null if it cannot be determined now.
    /// See the class header for the empty-vs-null contract. Never throws.
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetLootNamesAsync(string creatureName, CancellationToken ct = default)
    {
        string norm = Normalize(creatureName);
        if (norm.Length == 0)
            return null;

        EnsureLootLoaded();
        lock (_cacheGate)
        {
            if (_lootByNorm.TryGetValue(norm, out string[]? hit))
                return hit;
        }

        try
        {
            await _fetchGate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            // Re-check after acquiring the gate — another call may have filled it in.
            lock (_cacheGate)
            {
                if (_lootByNorm.TryGetValue(norm, out string[]? hit))
                    return hit;
            }

            await EnsureCreaturesLoadedAsync(ct).ConfigureAwait(false);

            string? race;
            lock (_cacheGate)
            {
                _raceByNorm.TryGetValue(norm, out race);
            }

            if (string.IsNullOrEmpty(race))
            {
                // Not a TibiaData creature (boss/new/unknown): known no-data, cache empty.
                StoreLoot(norm, Array.Empty<string>());
                return Array.Empty<string>();
            }

            string[]? loot = await FetchLootAsync(race!, ct).ConfigureAwait(false);
            if (loot == null)
                return null; // transient failure — do NOT cache, allow a later retry.

            StoreLoot(norm, loot);
            return loot;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TibiaData] loot lookup error for '{creatureName}': {ex.GetType().Name}: {ex.Message}.");
            return null;
        }
        finally
        {
            _fetchGate.Release();
        }
    }

    // ---------------------------------------------------------------- creature index (name -> race)

    private async Task EnsureCreaturesLoadedAsync(CancellationToken ct)
    {
        if (_creaturesAttempted)
            return;
        _creaturesAttempted = true; // one attempt per launch, offline or not.

        // (a) Fresh-enough disk cache.
        try
        {
            if (File.Exists(CreaturesCachePath))
            {
                CreaturesCacheFile? cache =
                    JsonSerializer.Deserialize<CreaturesCacheFile>(File.ReadAllText(CreaturesCachePath));
                if (cache?.Items is { Count: > 0 } &&
                    DateTime.UtcNow - cache.FetchedAtUtc < CreaturesMaxAge)
                {
                    IndexCreatures(cache.Items);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TibiaData] creatures cache read error: {ex.Message}.");
        }

        // (b) Fetch the index.
        try
        {
            using HttpResponseMessage resp =
                await GetClient().GetAsync(CreaturesEndpoint, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[TibiaData] creatures fetch failed: HTTP {(int)resp.StatusCode}.");
                TryIndexStaleCache();
                return;
            }

            await using Stream s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            CreaturesResponse? parsed =
                await JsonSerializer.DeserializeAsync<CreaturesResponse>(s, cancellationToken: ct).ConfigureAwait(false);

            List<CreatureIndexItem> items = (parsed?.Creatures?.CreatureList ?? new List<CreatureListItem>())
                .Where(c => !string.IsNullOrWhiteSpace(c.Race))
                .Select(c => new CreatureIndexItem { Name = c.Name ?? "", Race = c.Race! })
                .ToList();

            if (items.Count == 0)
            {
                Debug.WriteLine("[TibiaData] creatures payload empty; trying stale cache.");
                TryIndexStaleCache();
                return;
            }

            IndexCreatures(items);
            WriteJsonAtomic(CreaturesCachePath,
                new CreaturesCacheFile { FetchedAtUtc = DateTime.UtcNow, Items = items });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TibiaData] creatures fetch error: {ex.GetType().Name}: {ex.Message}.");
            TryIndexStaleCache();
        }
    }

    /// <summary>Fall back to a stale (older than max-age) on-disk index when the network is down.</summary>
    private void TryIndexStaleCache()
    {
        try
        {
            if (File.Exists(CreaturesCachePath))
            {
                CreaturesCacheFile? cache =
                    JsonSerializer.Deserialize<CreaturesCacheFile>(File.ReadAllText(CreaturesCachePath));
                if (cache?.Items is { Count: > 0 })
                    IndexCreatures(cache.Items);
            }
        }
        catch
        {
            // best effort only
        }
    }

    private void IndexCreatures(List<CreatureIndexItem> items)
    {
        lock (_cacheGate)
        {
            foreach (CreatureIndexItem it in items)
            {
                string race = it.Race;
                if (string.IsNullOrEmpty(race))
                    continue;
                string nr = Normalize(race);
                if (nr.Length > 0)
                    _raceByNorm[nr] = race;
                string nn = Normalize(it.Name);
                if (nn.Length > 0 && !_raceByNorm.ContainsKey(nn))
                    _raceByNorm[nn] = race;
            }
        }
    }

    // ---------------------------------------------------------------- per-creature loot fetch

    private async Task<string[]?> FetchLootAsync(string race, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage resp = await GetClient()
                .GetAsync(CreatureEndpointBase + Uri.EscapeDataString(race), HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[TibiaData] creature '{race}' fetch failed: HTTP {(int)resp.StatusCode}.");
                return null;
            }

            await using Stream s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            CreatureDetailResponse? parsed =
                await JsonSerializer.DeserializeAsync<CreatureDetailResponse>(s, cancellationToken: ct).ConfigureAwait(false);

            List<string>? loot = parsed?.Creature?.LootList;
            if (loot == null)
                return Array.Empty<string>();

            // De-dup, drop blanks, preserve first-seen order.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ordered = new List<string>(loot.Count);
            foreach (string name in loot)
            {
                string n = (name ?? "").Trim();
                if (n.Length > 0 && seen.Add(n))
                    ordered.Add(n);
            }
            return ordered.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TibiaData] creature '{race}' fetch error: {ex.GetType().Name}: {ex.Message}.");
            return null;
        }
    }

    // ---------------------------------------------------------------- loot disk cache

    private void EnsureLootLoaded()
    {
        if (_lootLoaded)
            return;
        lock (_cacheGate)
        {
            if (_lootLoaded)
                return;
            _lootLoaded = true;
            try
            {
                if (File.Exists(LootCachePath))
                {
                    Dictionary<string, string[]>? disk =
                        JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(LootCachePath));
                    if (disk != null)
                        foreach (KeyValuePair<string, string[]> kv in disk)
                            _lootByNorm[kv.Key] = kv.Value ?? Array.Empty<string>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TibiaData] loot cache read error: {ex.Message}.");
            }
        }
    }

    private void StoreLoot(string norm, string[] loot)
    {
        Dictionary<string, string[]> snapshot;
        lock (_cacheGate)
        {
            _lootByNorm[norm] = loot;
            snapshot = new Dictionary<string, string[]>(_lootByNorm, StringComparer.Ordinal);
        }
        WriteJsonAtomic(LootCachePath, snapshot);
    }

    // ---------------------------------------------------------------- helpers

    private HttpClient GetClient()
    {
        if (_client != null)
            return _client;
        lock (_cacheGate)
        {
            if (_client == null)
            {
                var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
                _client = c;
            }
            return _client;
        }
    }

    /// <summary>lowercase, keep [a-z0-9] only — matches the slug normalization used for the item bank.</summary>
    private static string Normalize(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        Span<char> buf = s.Length <= 64 ? stackalloc char[s.Length] : new char[s.Length];
        int n = 0;
        foreach (char ch in s)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
                buf[n++] = ch;
            else if (ch is >= 'A' and <= 'Z')
                buf[n++] = (char)(ch + 32);
        }
        return new string(buf.Slice(0, n));
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            string json = JsonSerializer.Serialize(value);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TibiaData] cache write error ({Path.GetFileName(path)}): {ex.Message}.");
        }
    }

    // ---------------------------------------------------------------- JSON DTOs

    private sealed class CreaturesResponse
    {
        [JsonPropertyName("creatures")] public CreaturesBlock? Creatures { get; set; }
    }

    private sealed class CreaturesBlock
    {
        [JsonPropertyName("creature_list")] public List<CreatureListItem>? CreatureList { get; set; }
    }

    private sealed class CreatureListItem
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("race")] public string? Race { get; set; }
    }

    private sealed class CreatureDetailResponse
    {
        [JsonPropertyName("creature")] public CreatureDetail? Creature { get; set; }
    }

    private sealed class CreatureDetail
    {
        [JsonPropertyName("loot_list")] public List<string>? LootList { get; set; }
    }

    private sealed class CreaturesCacheFile
    {
        public DateTime FetchedAtUtc { get; set; }
        public List<CreatureIndexItem>? Items { get; set; }
    }

    private sealed class CreatureIndexItem
    {
        public string Name { get; set; } = "";
        public string Race { get; set; } = "";
    }
}
