using System;
using System.Security.Cryptography;
using System.Text;

namespace UtevoLux.Features.Link;

/// <summary>
/// Auth identity for the Link server in the clean-room fork. The original TibiaVision authenticated
/// with an activated license key + a HardwareIdService HWID; this fork has neither, so:
///   - the "licenseKey" slot carries a stable per-install <see cref="LinkSettings.ClientId"/> GUID, and
///   - the HWID is a deterministic, non-reversible hash of machine + user identifiers.
/// Both are best-effort: if the server is offline or rejects them the feature degrades gracefully
/// (a status message), it never throws.
/// </summary>
public static class LinkIdentity
{
    /// <summary>Return the stored client id, generating and persisting one on first use.</summary>
    public static string EnsureClientId(LinkSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
            settings.ClientId = Guid.NewGuid().ToString("N");
        return settings.ClientId;
    }

    /// <summary>
    /// A stable, non-reversible hardware id derived from the machine + user name. Never throws —
    /// falls back to a random value if the environment lookups are unavailable.
    /// </summary>
    public static string GetHardwareId()
    {
        try
        {
            string seed = Environment.MachineName + "|" + Environment.UserName + "|" +
                          Environment.OSVersion.Platform;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
            return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
