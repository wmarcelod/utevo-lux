using System;
using System.Collections.Generic;

namespace UtevoLux.Features.Map;

/// <summary>
/// Store of user map markers. Raises <see cref="MarkersChanged"/> after any mutation. Clean-room reimplementation.
/// </summary>
public interface IMarkerStore
{
    event EventHandler MarkersChanged;

    IReadOnlyList<MapMarker> GetAll();

    IEnumerable<MapMarker> GetForFloor(int z);

    void Add(MapMarker marker);

    void Update(MapMarker marker);

    void Remove(Guid id);
}
