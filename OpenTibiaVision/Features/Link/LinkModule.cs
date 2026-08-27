using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTibiaVision.Core;

namespace OpenTibiaVision.Features.Link;

/// <summary>
/// The TibiaVision Link feature module: create/join a shared party by code and broadcast live
/// presence, with a click-through status overlay. Discovered by reflection like every other
/// <see cref="IFeatureModule"/>; sits in the core cluster (Order 25, between Lupa=20 and
/// Timers=30) where the original placed it.
///
/// The module owns the single <see cref="LinkViewModel"/> and drives the overlay window's
/// show/hide off the view-model's own events, so the page and the HUD stay in sync without either
/// knowing about the other. Everything degrades gracefully when the relay is offline (the
/// view-model surfaces a status message rather than throwing).
/// </summary>
public sealed class LinkModule : IFeatureModule, IShutdownHook
{
    private IAppServices _services = null!;
    private LinkViewModel _viewModel = null!;
    private LinkPage? _page;
    private LinkOverlayWindow? _overlay;

    public string Id => "link";
    public string Title => "TibiaVision Link";
    public int Order => 25;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Link") as Geometry
        ?? Geometry.Parse("M10.5,6.5 A2,2 0 1 0 13.5,6.5 A2,2 0 1 0 10.5,6.5 Z M7,15.2 H17 V16.4 H7 Z");

    public void Init(IAppServices services)
    {
        _services = services;
        _viewModel = new LinkViewModel(services);
        _viewModel.EnabledChanged += OnEnabledChanged;
        _viewModel.LockedChanged += OnLockedChanged;
        _viewModel.SettingsChanged += OnSettingsChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        // The original registered no global hotkey for Link; leave the map clean to avoid conflicts.
    }

    public UserControl BuildPage() => _page ??= new LinkPage(_viewModel);

    // ---- overlay lifecycle (driven by the view-model) ----

    private void OnEnabledChanged()
    {
        if (_viewModel.Enabled)
            ShowOverlay();
        else
            _overlay?.Hide();
    }

    private void OnLockedChanged() => _overlay?.ApplyLockState();

    private void OnSettingsChanged() => _overlay?.ApplyContent();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LinkViewModel.IsInParty) && _viewModel.Enabled)
            _overlay?.ApplyContent();
    }

    private void ShowOverlay()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _overlay ??= new LinkOverlayWindow(_services, _viewModel);
            if (!_overlay.IsVisible)
                _overlay.Show();
            _overlay.ApplyContent();
            _overlay.ApplyLockState();
        });
    }

    public void Shutdown()
    {
        _overlay?.SavePosition();
        _viewModel?.Shutdown();
        if (_overlay is not null)
        {
            _overlay.Close();
            _overlay = null;
        }
    }
}
