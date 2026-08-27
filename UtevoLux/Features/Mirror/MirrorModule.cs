using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UtevoLux.Core;

namespace UtevoLux.Features.Mirror;

/// <summary>
/// The Mirror feature module: the walking-skeleton DWM mirror promoted to the plug-in contract.
/// This is the reference implementation of <see cref="IFeatureModule"/> — the parallel feature
/// tracks (Magnifier, Audio/Timers, Overlays) follow the exact same shape under Features\.
/// </summary>
public sealed class MirrorModule : IFeatureModule, IStartupRestore, IShutdownHook
{
    private IAppServices _services = null!;
    private MirrorPageViewModel? _viewModel;
    private MirrorPage? _page;

    public string Id => "mirror";
    public string Title => "Espelhos";
    public int Order => 10;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Mirror") as Geometry
        ?? Geometry.Parse("M4,4 H14 V14 H4 Z M10,10 H20 V20 H10 Z");

    public void Init(IAppServices services)
    {
        _services = services;
        _viewModel = new MirrorPageViewModel(services);
    }

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        // Global toggle-lock-all: Ctrl+Alt+L. Owner = Id so conflicts name this module.
        hotkeys.TryBind(Id, "toggle-lock-all",
            new HotkeyGesture(Key.L, ModifierKeys.Control | ModifierKeys.Alt),
            () => _viewModel?.ToggleLockAll(),
            out _);
    }

    public UserControl BuildPage() => _page ??= new MirrorPage(_viewModel!);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
        => _viewModel is null ? Task.CompletedTask : _viewModel.RestoreAsync(progress, ct);

    /// <summary>Close mirrors without flipping their Visible state, then flush (app shutdown).</summary>
    public void Shutdown() => _viewModel?.Shutdown();
}
