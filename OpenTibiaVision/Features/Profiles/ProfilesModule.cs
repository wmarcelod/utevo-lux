using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTibiaVision.Core;

namespace OpenTibiaVision.Features.Profiles;

/// <summary>
/// The Profiles ("Perfis") feature module — the fork's version of the original TibiaVision
/// Profiles tool (Manage Profiles). Auto-discovered by <see cref="ModuleCatalog"/> (public
/// parameterless ctor + <see cref="IFeatureModule"/>), so it drops in with no shell/csproj edits.
///
/// Lists every named profile, marks the active one, and offers Create / Switch / Rename / Delete
/// plus import/export of a portable <c>.tvprofile</c> bundle — all wired to the foundation
/// <see cref="IProfileService"/> exposed through <see cref="IAppServices.Profiles"/>. It carries no
/// per-page runtime state of its own (the service owns the active profile), so it needs neither
/// startup restore nor a shutdown hook — the page reflects the service on every navigation.
/// </summary>
public sealed class ProfilesModule : IFeatureModule
{
    private ProfilesPageViewModel? _viewModel;
    private ProfilesPage? _page;

    public string Id => "profiles";
    public string Title => "Perfis";

    // Sits at the end of the CORE cluster (Mirror 10, Magnifier 20, Timers 30), just before the
    // Overlays group (40+). Mirror stays the default landing page.
    public int Order => 35;

    public Geometry Icon =>
        (Application.Current?.TryFindResource("Icon.Profiles") as Geometry)
        // Avatar fallback (head + shoulders, 24x24 grid) if the shared set has no profiles icon.
        ?? Geometry.Parse(
            "M12,3.8 A4.2,4.2 0 1 0 12,12.2 A4.2,4.2 0 1 0 12,3.8 Z " +
            "M4,21 C4,16.5 7.6,14 12,14 C16.4,14 20,16.5 20,21 Z");

    public void Init(IAppServices services)
    {
        _viewModel = new ProfilesPageViewModel(services);
    }

    // No global hotkeys: the original Profiles tool bound none.
    public void RegisterHotkeys(IHotkeyManager hotkeys) { }

    public UserControl BuildPage() => _page ??= new ProfilesPage(_viewModel!);
}
