using System.Collections.Generic;
using UtevoLux.Core;
using UtevoLux.Features.Mirror;

namespace UtevoLux.Features.Obs;

/// <summary>
/// Per-region extended UX state for OBS mirrors, persisted under its OWN settings key ("obs.ux"),
/// keyed by region Id. It reuses the Mirror feature's <see cref="MirrorUxState"/> model verbatim
/// (zoom / opacity / passthrough / auto-hide / fixed-box) — only the storage key differs, so OBS
/// regions never co-mingle with the Mirror dashboard's "mirror.ux" map. Writes ride the shared
/// atomic + 400 ms debounced <see cref="ISettingsStore"/> (never touches disk directly).
///
/// This is the OBS twin of <c>MirrorUxStore</c>; it is a deliberate copy rather than a shared type
/// because the store's key is intentionally per-feature.
/// </summary>
public sealed class ObsUxStore
{
    private const string Key = "obs.ux";

    private readonly ISettingsStore _settings;
    private readonly Dictionary<string, MirrorUxState> _map;

    public ObsUxStore(ISettingsStore settings)
    {
        _settings = settings;
        _map = settings.Get(Key, new Dictionary<string, MirrorUxState>())
               ?? new Dictionary<string, MirrorUxState>();
    }

    /// <summary>The stored state for a region, creating a default if absent.</summary>
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
