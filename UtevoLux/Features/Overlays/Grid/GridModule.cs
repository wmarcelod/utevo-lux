using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UtevoLux.Core;

namespace UtevoLux.Features.Overlays.GridOverlay;

/// <summary>
/// Grid feature module: a DPI-correct grid pinned over the game viewport. Contributes one nav
/// entry plus a global toggle hotkey (Ctrl+Alt+G).
/// </summary>
public sealed class GridModule : IFeatureModule, IStartupRestore, IShutdownHook
{
    private GridPageViewModel? _vm;
    private GridPage? _page;

    public string Id => "overlays.grid";
    public string Title => "Grade";
    public int Order => 50;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Grid") as Geometry
        ?? Geometry.Parse("M3,3 H21 V21 H3 Z M9,3 V21 M15,3 V21 M3,9 H21 M3,15 H21");

    public void Init(IAppServices services) => _vm = new GridPageViewModel(services);

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        hotkeys.TryBind(Id, "toggle-grid",
            new HotkeyGesture(Key.G, ModifierKeys.Control | ModifierKeys.Alt),
            () => _vm?.ToggleVisible(),
            out _);
    }

    public UserControl BuildPage() => _page ??= new GridPage(_vm!);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
        => _vm is null ? Task.CompletedTask : _vm.RestoreAsync(progress, ct);

    public void Shutdown() => _vm?.Shutdown();
}
