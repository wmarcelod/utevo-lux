namespace UtevoLux.Features.Link;

/// <summary>
/// Persisted state for the Link feature. Faithful port of the original
/// <c>WindowReplicaApp.Services.LinkSettings</c>, plus a fork-only <see cref="ClientId"/>: the
/// original derived auth from an activated license + a HardwareIdService, neither of which exist in
/// this clean-room fork, so a stable generated id stands in as the auth identity (see
/// <see cref="LinkIdentity"/>). Persisted through the shared <c>ISettingsStore</c> under
/// "link.settings" (atomic + 400 ms debounced) rather than the original's own link.json.
/// </summary>
public sealed class LinkSettings
{
    /// <summary>Whether the Link overlay/feature is active (set true after a successful connect).</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether the click-through overlay is shown while in a party.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>The name broadcast to the party.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Overlay position (WPF logical/DIP coordinates — matches Window.Left/Top).</summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>When locked the overlay is click-through and non-draggable.</summary>
    public bool Locked { get; set; } = true;

    /// <summary>Overlay layout scale (1.0 == 100%).</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Overlay card background opacity (0..1).</summary>
    public double BackgroundOpacity { get; set; } = 0.7;

    /// <summary>Volume of the "a member disconnected" chime (0..1).</summary>
    public double DisconnectSoundVolume { get; set; } = 1.0;

    /// <summary>
    /// Stable per-install identity used as the auth key against the Link server (fork substitute
    /// for the original license key). Generated on first use and persisted.
    /// </summary>
    public string ClientId { get; set; } = "";
}
