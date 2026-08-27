using System;
using System.Collections.Generic;

namespace UtevoLux.Features.Map;

/// <summary>
/// Store of user map routes. Raises <see cref="RoutesChanged"/> after any mutation. Clean-room reimplementation.
/// </summary>
public interface IRouteStore
{
    event EventHandler RoutesChanged;

    IReadOnlyList<MapRoute> GetAll();

    void Add(MapRoute route);

    void Remove(Guid id);
}
