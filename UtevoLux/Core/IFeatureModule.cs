using System.Windows.Controls;
using System.Windows.Media;

namespace UtevoLux.Core;

/// <summary>
/// The plug-in contract. The shell discovers every implementor by reflection, builds one nav
/// entry per module, and calls the lifecycle in this order:
///   1. Init(services)          -- grab the services you need; do NO UI work here.
///   2. RegisterHotkeys(hotkeys)-- claim global hotkeys (owner = Id).
///   3. BuildPage()             -- built ONCE, kept alive, visibility-toggled on nav (O(1)).
///
/// A module never creates or closes its page on navigation; the shell toggles Visibility of
/// the single instance so state is preserved and nav is instant (optimization principle 3).
/// </summary>
public interface IFeatureModule
{
    /// <summary>Stable unique id (also the hotkey owner tag). e.g. "mirror".</summary>
    string Id { get; }

    /// <summary>Display name shown in the sidebar.</summary>
    string Title { get; }

    /// <summary>Vector icon geometry for the sidebar (crisp at any UI scale).</summary>
    Geometry Icon { get; }

    /// <summary>
    /// Sidebar sort key (ascending; ties broken by <see cref="Title"/>). Lower = higher in the nav.
    /// Defaulted so a drop-in module needs no edit here; declared features override it to claim a
    /// deliberate slot. Convention: leave gaps of 10 so a new feature can slot between two others.
    /// </summary>
    int Order => 1000;

    /// <summary>Receive shared services. Called once, before RegisterHotkeys / BuildPage.</summary>
    void Init(IAppServices services);

    /// <summary>Claim global hotkeys. Called once, after Init.</summary>
    void RegisterHotkeys(IHotkeyManager hotkeys);

    /// <summary>Build the page. Called once; the returned control is cached and reused.</summary>
    UserControl BuildPage();
}
