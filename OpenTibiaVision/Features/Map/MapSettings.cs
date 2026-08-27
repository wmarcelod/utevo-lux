namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Persisted window/UX state for the map feature (window rect + scale, search toggles, pins
/// panel, and the show/hide hotkey). Loaded/saved by <see cref="MapSettingsService"/>. Ported
/// faithfully from the original TibiaVision.
/// </summary>
public class MapSettings
{
    public double? WindowX { get; set; }

    public double? WindowY { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public double WindowScale { get; set; } = 1.0;

    public bool NpcSearchEnabled { get; set; } = true;

    public bool RareSearchEnabled { get; set; } = true;

    public bool PinsPanelEnabled { get; set; }

    public int HotkeyCode { get; set; }

    public int HotkeyModifiers { get; set; }
}
