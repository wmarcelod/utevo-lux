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

    /// <summary>
    /// Prefer the installed Tibia client's own minimap (the player's explored map) over the bundled
    /// tiles when a game install is detected. Default on so the map "just works" with the player's
    /// current map; falls back to the bundled snapshot when no install is found.
    /// </summary>
    public bool UsePlayerMinimap { get; set; } = true;

    public int HotkeyCode { get; set; }

    public int HotkeyModifiers { get; set; }
}
