using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace UtevoLux.Services;

/// <summary>
/// Checks GitHub Releases for a newer version on startup and, on the user's OK, downloads the
/// installer and launches it (the Inno Setup installer upgrades in place via its stable AppId and
/// relaunches the app). Everything is best-effort and NEVER throws: offline / rate-limited / parse
/// errors simply mean "no update offered" and the app carries on.
///
/// The repo is public, so the check is an unauthenticated GET to the GitHub API (60/hour per IP —
/// one call per launch is well within that). Politeness mirrors the other providers: a single
/// check per launch, an identifying User-Agent, and no retries.
/// </summary>
public static class UpdateService
{
    private const string LatestApi = "https://api.github.com/repos/wmarcelod/utevo-lux/releases/latest";
    private const string ReleasesPage = "https://github.com/wmarcelod/utevo-lux/releases";
    private const string InstallerAsset = "UtevoLux-Setup.exe";
    private const string StableInstallerUrl = "https://github.com/wmarcelod/utevo-lux/releases/latest/download/UtevoLux-Setup.exe";
    private const string UserAgent = "UtevoLux-Updater";

    public sealed record UpdateInfo(Version Version, string Tag, string InstallerUrl, string ReleaseUrl, string Notes);

    /// <summary>The version this build reports (from the assembly's <c>Version</c>), normalized to 3 parts.</summary>
    public static Version CurrentVersion()
    {
        Version? v = Assembly.GetExecutingAssembly().GetName().Version;
        return Norm(v ?? new Version(0, 0, 0));
    }

    /// <summary>
    /// Returns an <see cref="UpdateInfo"/> when the latest GitHub release is newer than this build,
    /// otherwise null (including on any failure). Never throws.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            using HttpResponseMessage resp = await http.GetAsync(LatestApi, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            await using Stream s = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
            JsonElement root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out JsonElement t) ? (t.GetString() ?? "") : "";
            Version? remote = ParseVersion(tag);
            if (remote == null || remote <= CurrentVersion())
                return null;

            string installerUrl = StableInstallerUrl;
            if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out JsonElement n) ? (n.GetString() ?? "") : "";
                    if (string.Equals(name, InstallerAsset, StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out JsonElement u))
                    {
                        installerUrl = u.GetString() ?? StableInstallerUrl;
                        break;
                    }
                }
            }

            string releaseUrl = root.TryGetProperty("html_url", out JsonElement h) ? (h.GetString() ?? ReleasesPage) : ReleasesPage;
            string notes = root.TryGetProperty("body", out JsonElement b) ? (b.GetString() ?? "") : "";
            return new UpdateInfo(remote, tag, installerUrl, releaseUrl, notes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Update] check failed: {ex.GetType().Name}: {ex.Message}.");
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer to a temp file and launches it. Returns true if the installer was
    /// started (the caller should then exit the app so the installer can replace files and relaunch).
    /// Never throws.
    /// </summary>
    public static async Task<bool> DownloadAndLaunchAsync(UpdateInfo info, CancellationToken ct = default)
    {
        try
        {
            string dest = Path.Combine(Path.GetTempPath(), $"UtevoLux-Setup-{Sanitize(info.Tag)}.exe");

            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
                using HttpResponseMessage resp =
                    await http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return false;

                await using Stream net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
                await net.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            Process.Start(new ProcessStartInfo { FileName = dest, UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Update] download/launch failed: {ex.GetType().Name}: {ex.Message}.");
            return false;
        }
    }

    /// <summary>Opens the releases page in the default browser (fallback when an in-app update fails).</summary>
    public static void OpenReleasesPage()
    {
        try { Process.Start(new ProcessStartInfo { FileName = ReleasesPage, UseShellExecute = true }); }
        catch { /* best effort */ }
    }

    private static Version Norm(Version v) =>
        new Version(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static Version? ParseVersion(string tag)
    {
        string cleaned = (tag ?? "").TrimStart('v', 'V').Trim();
        Match m = Regex.Match(cleaned, @"^\d+(?:\.\d+){0,3}");
        if (!m.Success)
            return null;
        string num = m.Value.Contains('.') ? m.Value : m.Value + ".0";
        return Version.TryParse(num, out Version? v) ? Norm(v) : null;
    }

    private static string Sanitize(string s) => Regex.Replace(s ?? "", "[^A-Za-z0-9._-]", "_");
}
