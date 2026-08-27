using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTibiaVision.Core;

namespace OpenTibiaVision.Features.Overlays.Glow;

/// <summary>
/// Cursor-Glow feature module: a ring that follows the pointer. Contributes one nav entry plus a
/// global toggle hotkey (Ctrl+Alt+K).
/// </summary>
public sealed class GlowModule : IFeatureModule, IStartupRestore, IShutdownHook
{
    private GlowPageViewModel? _vm;
    private GlowPage? _page;

    public string Id => "overlays.glow";
    public string Title => "Brilho do Cursor";
    public int Order => 70;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Glow") as Geometry
        ?? Geometry.Parse("M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4 Z M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z");

    public void Init(IAppServices services) => _vm = new GlowPageViewModel(services);

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        hotkeys.TryBind(Id, "toggle-glow",
            new HotkeyGesture(Key.K, ModifierKeys.Control | ModifierKeys.Alt),
            () => _vm?.ToggleVisible(),
            out _);
    }

    public UserControl BuildPage() => _page ??= new GlowPage(_vm!);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
        => _vm is null ? Task.CompletedTask : _vm.RestoreAsync(progress, ct);

    public void Shutdown() => _vm?.Shutdown();
}
