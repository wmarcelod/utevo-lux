using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Live creature-spawn data source backed by tibiaroute.com's public delivery-task-spots
/// endpoint, converted into the fork's <see cref="NpcEntry"/> spawn model so it feeds the map's
/// creature search + reveal-on-map + spawn-cluster layers exactly like the bundled
/// <see cref="MonsterSpawnDirectory"/>.
///
/// POLITENESS POLICY (this is a courtesy fetch against someone else's free service):
///   * Exactly ONE automatic fetch per app launch. <see cref="StartBackgroundRefreshOnce"/> is
///     guarded so repeated calls are no-ops; there is NO polling/looping. The user can trigger
///     one extra fetch on demand via the map's "Atualizar criaturas" button (RefreshAsync).
///   * Identifying, cache-friendly User-Agent so the operator can see this is a small personal
///     tool, not an abusive scraper.
///   * The ~6.4 MB payload is streamed straight to the cache file — never buffered whole in
///     memory — and the parsed result is cached to disk so we do not re-download on every launch.
///
/// THIRD-PARTY SOURCE: tibiaroute.com is not affiliated with this tool; its endpoint/response
/// shape may change or vanish without notice. Because of that, this provider NEVER throws to its
/// callers: on any failure (offline, timeout, non-200, parse error) it logs and keeps the previous
/// cache, and callers ultimately fall back to the bundled monster_spawns.dat.
///
/// Load() precedence: (a) fresh fetch result if THIS launch succeeded; else (b) the last cached
/// file from a previous launch; else (c) null (caller falls back to MonsterSpawnDirectory .dat).
/// </summary>
public sealed class TibiaRouteSpawnProvider
{
    /// <summary>Process-wide singleton so the background fetch and the map share one snapshot.</summary>
    public static TibiaRouteSpawnProvider Shared { get; } = new TibiaRouteSpawnProvider();

    private const string Endpoint = "https://tibiaroute.com/api/delivery-task-spots";

    // Identifying, cache-friendly UA (politeness policy — see class header). Sent verbatim.
    private const string UserAgent = "OpenTibiaVision/0.1 (+personal map tool; cache-friendly)";

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenTibiaVision");

    // Big payload cache + a small sidecar carrying the fetched-at UTC timestamp.
    private static readonly string CachePath = Path.Combine(CacheDir, "tibiaroute_spawns.json");
    private static readonly string MetaPath = Path.Combine(CacheDir, "tibiaroute_spawns.meta.json");

    private readonly object _gate = new();
    private Snapshot? _snapshot;

    private HttpClient? _client;
    private int _autoStarted; // Interlocked 0/1 guard for the once-per-launch auto fetch.

    private TibiaRouteSpawnProvider()
    {
    }

    /// <summary>UTC time of the currently-loaded dataset (fresh fetch or cache), or null if none.</summary>
    public DateTime? LastUpdatedUtc
    {
        get { lock (_gate) { return _snapshot?.FetchedAtUtc; } }
    }

    /// <summary>
    /// Fire the ONE automatic background fetch for this app launch (fire-and-forget). Safe to call
    /// more than once — the Interlocked guard makes every call after the first a no-op, so the
    /// shell never triggers a second network hit. Called from MapModule.Init.
    /// </summary>
    public void StartBackgroundRefreshOnce()
    {
        if (Interlocked.Exchange(ref _autoStarted, 1) != 0)
            return;

        // Detached: the shell must never wait on network I/O. RefreshAsync never throws.
        _ = Task.Run(() => RefreshAsync());
    }

    /// <summary>
    /// Fetch the endpoint, stream it to the cache, and update the in-memory snapshot. NEVER throws:
    /// any failure logs and leaves the previous cache/snapshot intact. Used by the once-per-launch
    /// background fetch and by the map's manual "Atualizar criaturas" button.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Bound the whole operation (connect + headers + streaming body) to ~30s so a stalled
        // socket can never hang the fetch; linked to the caller's token for cooperative cancel.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        CancellationToken ct = timeoutCts.Token;

        string tmp = CachePath + ".tmp";
        try
        {
            Directory.CreateDirectory(CacheDir);

            HttpClient client = GetClient();
            using HttpResponseMessage response =
                await client.GetAsync(Endpoint, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[TibiaRoute] fetch failed: HTTP {(int)response.StatusCode}; keeping previous cache.");
                return;
            }

            // Stream the large (~6.4 MB) body straight to a temp file — never buffer it whole.
            await using (Stream netStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await netStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            }

            // Parse the just-written temp file to validate it and build the in-memory snapshot.
            List<TibiaRouteMonsterEntry>? raw;
            await using (var readStream = new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                raw = await JsonSerializer.DeserializeAsync<List<TibiaRouteMonsterEntry>>(readStream, cancellationToken: ct)
                    .ConfigureAwait(false);
            }

            if (raw == null || raw.Count == 0)
            {
                Debug.WriteLine("[TibiaRoute] parsed payload was empty/invalid; keeping previous cache.");
                TryDelete(tmp);
                return;
            }

            List<NpcEntry> entries = ConvertToEntries(raw);
            DateTime fetchedAt = DateTime.UtcNow;

            // Atomic swap: back up the current cache, move temp into place, then write the sidecar.
            // (Mirrors the fork's tmp -> .bak -> File.Move idiom in JsonRouteStore/JsonMarkerStore.)
            if (File.Exists(CachePath))
                File.Copy(CachePath, CachePath + ".bak", overwrite: true);
            File.Move(tmp, CachePath, overwrite: true);
            WriteMeta(new TibiaRouteCacheMeta { FetchedAtUtc = fetchedAt, Source = Endpoint, EntryCount = entries.Count });

            lock (_gate)
            {
                _snapshot = new Snapshot(entries, fetchedAt);
            }
            Debug.WriteLine($"[TibiaRoute] refreshed {entries.Count} creature-spawn entries at {fetchedAt:O}.");
        }
        catch (Exception ex)
        {
            // NEVER throw to callers. Offline / DNS / timeout / non-200 / parse / IO all land here;
            // the previous cache and in-memory snapshot stay intact so the map keeps working.
            Debug.WriteLine($"[TibiaRoute] refresh error: {ex.GetType().Name}: {ex.Message}; keeping previous cache.");
            TryDelete(tmp);
        }
    }

    /// <summary>
    /// Return the current creature-spawn dataset as fork <see cref="NpcEntry"/> spawn entries, or
    /// null when neither a fresh fetch nor a cached file is available (caller then falls back to the
    /// bundled monster_spawns.dat). Precedence: (a) this-launch fetch, (b) last cache, (c) null.
    /// Never throws.
    /// </summary>
    public IReadOnlyList<NpcEntry>? Load()
    {
        // (a) Fresh fetch (or an already-loaded cache) held in memory.
        lock (_gate)
        {
            if (_snapshot != null)
                return _snapshot.Entries;
        }

        // (b) Last cached file from a previous launch.
        try
        {
            if (File.Exists(CachePath))
            {
                List<TibiaRouteMonsterEntry>? raw;
                using (var fs = new FileStream(CachePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    raw = JsonSerializer.Deserialize<List<TibiaRouteMonsterEntry>>(fs);
                }

                if (raw is { Count: > 0 })
                {
                    List<NpcEntry> entries = ConvertToEntries(raw);
                    DateTime fetchedAt = ReadMetaTimestamp() ?? File.GetLastWriteTimeUtc(CachePath);
                    var snap = new Snapshot(entries, fetchedAt);
                    lock (_gate)
                    {
                        // A concurrent RefreshAsync may have won the race; prefer its fresher result.
                        _snapshot ??= snap;
                        return _snapshot.Entries;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TibiaRoute] cache load error: {ex.GetType().Name}: {ex.Message}.");
        }

        // (c) Nothing usable — caller falls back to the bundled monster_spawns.dat.
        return null;
    }

    // ---------------------------------------------------------------- conversion to fork model

    /// <summary>
    /// Convert tibiaroute monsters into the fork's spawn entries: Name = monster name; Positions =
    /// ALL spawn tiles flattened across every spot; Location = the distinct spot location names
    /// (biggest first); IsSpawnData = true. Monsters with no usable spawn tile are skipped (an
    /// empty NpcEntry.Positions would break Search/reveal, whose Primary indexes Positions[0]).
    /// </summary>
    private static List<NpcEntry> ConvertToEntries(List<TibiaRouteMonsterEntry> raw)
    {
        var result = new List<NpcEntry>(raw.Count);

        foreach (TibiaRouteMonsterEntry entry in raw)
        {
            TibiaRouteMonster? monster = entry.Monster;
            if (monster == null || string.IsNullOrWhiteSpace(monster.Name) || entry.Spots == null)
                continue;

            var positions = new List<NpcPosition>();
            var locationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (TibiaRouteSpot? spot in entry.Spots)
            {
                if (spot?.Spawns == null)
                    continue;

                int added = 0;
                foreach (TibiaRouteSpawn s in spot.Spawns)
                {
                    // Guard the floor to the valid Tibia range (0..15) like the fork's other loaders.
                    if (s.Z < 0 || s.Z > 15)
                        continue;
                    positions.Add(new NpcPosition(s.X, s.Y, s.Z));
                    added++;
                }

                if (added > 0 && !string.IsNullOrWhiteSpace(spot.LocationName))
                {
                    string loc = spot.LocationName.Trim();
                    locationCounts[loc] = (locationCounts.TryGetValue(loc, out int c) ? c : 0) + added;
                }
            }

            if (positions.Count == 0)
                continue;

            result.Add(new NpcEntry
            {
                Name = monster.Name.Trim(),
                Location = DescribeLocation(locationCounts, positions.Count),
                Positions = positions,
                IsSpawnData = true
            });
        }

        return result;
    }

    /// <summary>Distinct spot names, biggest first; falls back to a spawn-point count if unnamed.</summary>
    private static string DescribeLocation(Dictionary<string, int> counts, int totalSpawns)
    {
        if (counts.Count == 0)
            return totalSpawns == 1 ? "1 spawn point" : $"{totalSpawns:N0} spawn points";

        List<string> ordered = counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .ToList();

        if (ordered.Count <= 3)
            return string.Join(", ", ordered);

        return $"{ordered[0]}, {ordered[1]} +{ordered.Count - 2} more";
    }

    // ---------------------------------------------------------------- http / disk helpers

    private HttpClient GetClient()
    {
        if (_client != null)
            return _client;

        lock (_gate)
        {
            if (_client == null)
            {
                var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                // TryAddWithoutValidation sends the UA string verbatim (the "(+...)" comment can
                // trip the strict header validator). Politeness policy — see class header.
                c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
                _client = c;
            }
            return _client;
        }
    }

    private static void WriteMeta(TibiaRouteCacheMeta meta)
    {
        try
        {
            string json = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
            string tmp = MetaPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, MetaPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TibiaRoute] meta write error: {ex.Message}.");
        }
    }

    private static DateTime? ReadMetaTimestamp()
    {
        try
        {
            if (File.Exists(MetaPath))
            {
                TibiaRouteCacheMeta? meta = JsonSerializer.Deserialize<TibiaRouteCacheMeta>(File.ReadAllText(MetaPath));
                if (meta != null && meta.FetchedAtUtc != default)
                    return meta.FetchedAtUtc;
            }
        }
        catch
        {
            // best effort: a missing/corrupt sidecar just means we fall back to the file mtime.
        }
        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore: a leftover .tmp is harmless and gets overwritten on the next fetch.
        }
    }

    /// <summary>Immutable in-memory dataset: the converted entries plus when they were obtained.</summary>
    private sealed class Snapshot
    {
        public Snapshot(IReadOnlyList<NpcEntry> entries, DateTime fetchedAtUtc)
        {
            Entries = entries;
            FetchedAtUtc = fetchedAtUtc;
        }

        public IReadOnlyList<NpcEntry> Entries { get; }

        public DateTime FetchedAtUtc { get; }
    }
}
