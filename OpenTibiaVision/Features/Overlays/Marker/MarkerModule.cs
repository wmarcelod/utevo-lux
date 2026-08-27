using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTibiaVision.Core;

namespace OpenTibiaVision.Features.Overlays.Marker;

/// <summary>
/// Marker feature module: a passive, user-parked location marker (decoration, does not track).
/// Contributes one nav entry plus a global toggle hotkey (Ctrl+Alt+M).
/// </summary>
public sealed class MarkerModule : IFeatureModule, IStartupRestore, IShutdownHook
{
    private MarkerPageViewModel? _vm;
    private MarkerPage? _page;

    public string Id => "overlays.marker";
    public string Title => "Marcador";
    public int Order => 60;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Marker") as Geometry
        ?? Geometry.Parse("M12,2 C8,2 5,5 5,9 C5,14 12,22 12,22 C12,22 19,14 19,9 C19,5 16,2 12,2 Z M12,6 A3,3 0 1 1 12,12 A3,3 0 1 1 12,6 Z");

    public void Init(IAppServices services) => _vm = new MarkerPageViewModel(services);

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        hotkeys.TryBind(Id, "toggle-marker",
            new HotkeyGesture(Key.M, ModifierKeys.Control | ModifierKeys.Alt),
            () => _vm?.ToggleVisible(),
            out _);
    }

    public UserControl BuildPage() => _page ??= new MarkerPage(_vm!);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
        => _vm is null ? Task.CompletedTask : _vm.RestoreAsync(progress, ct);

    public void Shutdown() => _vm?.Shutdown();
}
