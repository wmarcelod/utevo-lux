using System.Collections.Generic;
using UtevoLux.Core;

namespace UtevoLux.Features.Mirror;

/// <summary>
/// Persists <see cref="MirrorUxState"/> per region under a single settings key, keyed by the
/// region's Id. This keeps the extended UX state out of the shared <c>RegionConfig</c> model:
/// writes go through the shared atomic + 400 ms debounced <see cref="ISettingsStore"/>
/// (principle 7), so we never touch disk directly and never edit foundation files.
/// </summary>
public sealed class MirrorUxStore
{
    private const string Key = "mirror.ux";

    private readonly ISettingsStore _settings;
    private readonly Dictionary<string, MirrorUxState> _map;

    public MirrorUxStore(ISettingsStore settings)
    {
        _settings = settings;
        _map = settings.Get(Key, new Dictionary<string, MirrorUxState>())
               ?? new Dictionary<string, MirrorUxState>();
    }

    /// <summary>The stored state for a region, creating (and persisting) a default if absent.</summary>
    public MirrorUxState GetOrCreate(string regionId)
    {
        if (!_map.TryGetValue(regionId, out MirrorUxState? state) || state is null)
        {
            state = new MirrorUxState();
            _map[regionId] = state;
        }
        return state;
    }

    /// <summary>Coalesced write of the whole map (debounced by the underlying store).</summary>
    public void Save() => _settings.Set(Key, _map);

    public void Remove(string regionId)
    {
        if (_map.Remove(regionId))
            Save();
    }
}
