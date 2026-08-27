using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UtevoLux.Core;

namespace UtevoLux.Features.Audio;

/// <summary>
/// The Audio / Timers / Alerts feature module. Auto-discovered by the shell's
/// <see cref="ModuleCatalog"/> (a public parameterless ctor + <see cref="IFeatureModule"/>), so
/// it drops in with no shell/csproj edits. Follows the same lifecycle shape as the Mirror module:
///   Init -> RegisterHotkeys -> BuildPage, then RestoreAsync behind the startup overlay, and
///   Shutdown before the final settings flush.
///
/// Per-timer hotkeys are dynamic and bound by the view model (owner tag "audio.timers"); the
/// module-level dismiss / stop-all / mute hotkeys are bound here under the module Id "audio".
/// </summary>
public sealed class AudioModule : IFeatureModule, IStartupRestore, IShutdownHook
{
    private AudioPageViewModel? _viewModel;
    private AudioPage? _page;

    public string Id => "audio";
    public string Title => "Timers e Alertas";
    public int Order => 30;

    public Geometry Icon =>
        (Application.Current?.TryFindResource("Icon.Audio") as Geometry)
        // Bell fallback (24x24 grid) if the shared set has no audio icon.
        ?? Geometry.Parse("M12,3 A5,5 0 0 1 17,8 V12 L19,15 H5 L7,12 V8 A5,5 0 0 1 12,3 Z M10,17 H14 A2,2 0 0 1 10,17 Z");

    public void Init(IAppServices services)
    {
        _viewModel = new AudioPageViewModel(services);
    }

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        if (_viewModel is null)
            return;

        // Ctrl+Alt+D: dispensar alertas (dismiss stay-until-hotkey banners + silence).
        hotkeys.TryBind(Id, "dismiss-alerts",
            new HotkeyGesture(Key.D, ModifierKeys.Control | ModifierKeys.Alt),
            () => _viewModel.DismissAllAlerts(), out _);

        // Ctrl+Alt+S: parar todos os timers (reset + silence).
        hotkeys.TryBind(Id, "stop-all",
            new HotkeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Alt),
            () => _viewModel.StopAllTimers(), out _);

        // Ctrl+Alt+M: alternar mudo.
        hotkeys.TryBind(Id, "mute-toggle",
            new HotkeyGesture(Key.M, ModifierKeys.Control | ModifierKeys.Alt),
            () => _viewModel.ToggleMute(), out _);
    }

    public UserControl BuildPage() => _page ??= new AudioPage(_viewModel!);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
        => _viewModel is null ? Task.CompletedTask : _viewModel.RestoreAsync(progress, ct);

    public void Shutdown() => _viewModel?.Shutdown();
}
