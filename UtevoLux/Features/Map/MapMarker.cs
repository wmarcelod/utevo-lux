using System;

namespace UtevoLux.Features.Map;

/// <summary>
/// A user-placed map pin: world position, icon index, and a short description. Persisted by
/// <see cref="JsonMarkerStore"/> and shareable via <see cref="ShareCodeService"/>. Ported
/// faithfully from the original TibiaVision.
/// </summary>
public class MapMarker
{
    public const int MaxDescriptionLength = 100;

    public const int IconCount = 20;

    public Guid Id { get; set; } = Guid.NewGuid();

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public int Icon { get; set; }

    public string Description { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsSaved { get; set; }
}
