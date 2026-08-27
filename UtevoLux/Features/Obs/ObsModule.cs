using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UtevoLux.Core;

namespace UtevoLux.Features.Obs;

/// <summary>
/// The "Ferramentas OBS" feature module: crop areas from OBS projector windows for streaming
/// (the original tool "Crop areas from OBS projector windows for streaming"). It reuses the
/// Mirror feature's DWM host window and crop tooling wholesale (<see cref="ObsMirrorWindow"/> is the
/// fork's <c>MirrorWindow</c> plus an aggressive always-on-top re-assert), so an OBS crop behaves
/// exactly like a normal mirror but stays pinned above the capture tool's projector.
///
/// Discovered by reflection like every other module — no registration edits. Sits right after the
/// Mirror dashboard: Order 15 slots it between Mirror (10) and Magnifier (20), its natural place as
/// a crop-mirror sibling.
/// </summary>
public sealed class ObsModule : IFeatureModule, IStartupRestore, IShutdownHook
{
    private ObsPageViewModel? _viewModel;
    private ObsPage? _page;

    public string Id => "obs";
    public string Title => "Ferramentas OBS";
    public int Order => 15;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Target") as Geometry
        ?? Geometry.Parse("M11,2 H13 V22 H11 Z M2,11 H22 V13 H2 Z");

    public void Init(IAppServices services)
    {
        _viewModel = new ObsPageViewModel(services);
    }

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        // No global hotkeys: the original OBS crop flow is entirely dashboard-driven.
    }

    public UserControl BuildPage() => _page ??= new ObsPage(_viewModel!);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
        => _viewModel is null ? Task.CompletedTask : _viewModel.RestoreAsync(progress, ct);

    /// <summary>Close OBS mirrors without flipping their Visible state, then flush (app shutdown).</summary>
    public void Shutdown() => _viewModel?.Shutdown();
}
