using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UtevoLux.Core;

namespace UtevoLux.Features.Overlays.Notes;

/// <summary>
/// Notes feature module: floating sticky notes over the game. Discovered by reflection, it
/// contributes one nav entry (its dashboard page) and a global show/hide hotkey.
/// </summary>
public sealed class NotesModule : IFeatureModule, IStartupRestore, IShutdownHook
{
    private NotesPageViewModel? _vm;
    private NotesPage? _page;

    public string Id => "overlays.notes";
    public string Title => "Notas";
    public int Order => 40;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Notes") as Geometry
        ?? Geometry.Parse("M5,3 H14 L19,8 V21 H5 Z M14,3 V8 H19 M8,12 H16 M8,15 H16 M8,18 H13");

    public void Init(IAppServices services) => _vm = new NotesPageViewModel(services);

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        // Global show/hide all notes: Ctrl+Alt+N (spec). Owner = Id so conflicts name this module.
        hotkeys.TryBind(Id, "toggle-notes",
            new HotkeyGesture(Key.N, ModifierKeys.Control | ModifierKeys.Alt),
            () => _vm?.ToggleAllVisible(),
            out _);
    }

    public UserControl BuildPage() => _page ??= new NotesPage(_vm!);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
        => _vm is null ? Task.CompletedTask : _vm.RestoreAsync(progress, ct);

    public void Shutdown() => _vm?.Shutdown();
}
