using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTibiaVision.Core;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// The TibiaMaps feature module. Like the original TibiaVision, the map lives in its OWN top-level
/// window kept as a singleton; the nav page (<see cref="MapLauncherPage"/>) is a launcher that
/// opens/focuses it (and opens it on first navigation). A global Ctrl+Alt+M hotkey shows/hides it.
///
/// Slotted in the CORE cluster: Order 32, after Timers (30), before Profiles (35).
/// </summary>
public sealed class MapModule : IFeatureModule
{
    private IAppServices _services = null!;
    private MapLauncherPage? _page;
    private MapWindow? _window;

    public string Id => "map";
    public string Title => "TibiaMaps";
    public int Order => 32;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Map") as Geometry
        ?? Geometry.Parse("M3,6 L9,3 L15,6 L21,3 V18 L15,21 L9,18 L3,21 Z M9,3 V18 M15,6 V21");

    public void Init(IAppServices services) => _services = services;

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        // Global show/hide map: Ctrl+Alt+M. Owner = Id so conflicts name this module.
        hotkeys.TryBind(Id, "toggle-map",
            new HotkeyGesture(Key.M, ModifierKeys.Control | ModifierKeys.Alt),
            ToggleMap, out _);
    }

    public UserControl BuildPage() => _page ??= new MapLauncherPage(OpenOrFocus);

    /// <summary>Open the singleton map window (creating it once), or bring it to front.</summary>
    private void OpenOrFocus()
    {
        if (_window == null)
        {
            _window = new MapWindow(_services);
            _window.Closed += (_, _) => _window = null;
        }
        if (!_window.IsVisible)
            _window.Show();
        _window.Activate();
    }

    /// <summary>Hotkey: hide when visible, otherwise open/focus.</summary>
    private void ToggleMap()
    {
        if (_window is { IsVisible: true })
            _window.Hide();
        else
            OpenOrFocus();
    }
}
