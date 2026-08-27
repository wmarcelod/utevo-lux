using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Magnifier;

/// <summary>
/// The Magnifier feature module. Discovered by reflection like every other <see cref="IFeatureModule"/>;
/// it contributes ONE nav entry ("Lupa") whose page controls two DWM magnifiers:
///
///   * a follow-cursor lens — HOLD the configured gesture to activate (a momentary binding on the
///     shell's separate, non-consuming magnifier hook); roll the wheel while holding to zoom
///     (1.5–6.0 in 0.25 steps) via a WH_MOUSE_LL hook that swallows the wheel;
///   * a fixed-crop loupe — a placed, live view of a fixed sub-rect of a chosen source window,
///     toggled from the page or via Ctrl+Alt+U.
///
/// All state lives in one <see cref="MagnifierSettings"/> object persisted under a single key via
/// the shared (atomic + debounced) settings store.
/// </summary>
public sealed class MagnifierModule : IFeatureModule, IStartupRestore, IShutdownHook
{
    private const string OwnerId = "magnifier";
    private const string SettingsKey = "magnifier.settings";
    private const string LoupeToggleAction = "toggle-loupe";

    private IAppServices _services = null!;
    private MagnifierSettings _settings = new();
    private FollowLensController? _follow;
    private FixedLoupeController? _loupe;
    private MagnifierPageViewModel? _viewModel;
    private MagnifierPage? _page;

    private IHotkeyManager? _hotkeys;
    private IDisposable? _holdBinding;

    public string Id => OwnerId;
    public string Title => "Lupa";
    public int Order => 20;

    public Geometry Icon =>
        Application.Current?.TryFindResource("Icon.Target") as Geometry
        ?? Geometry.Parse("M11,2 H13 V4 A8,8 0 0 1 20,11 H22 V13 H20 A8,8 0 0 1 13,20 V22 H11 V20 " +
                          "A8,8 0 0 1 4,13 H2 V11 H4 A8,8 0 0 1 11,4 Z");

    public void Init(IAppServices services)
    {
        _services = services;
        _settings = services.Settings.Get(SettingsKey, new MagnifierSettings()) ?? new MagnifierSettings();

        _follow = new FollowLensController(services, () => _settings);
        _loupe = new FixedLoupeController(services, _settings, Persist);
        _viewModel = new MagnifierPageViewModel(services, _settings, _loupe, Persist, RebindHold);
    }

    public void RegisterHotkeys(IHotkeyManager hotkeys)
    {
        _hotkeys = hotkeys;

        // Hold-to-activate the follow lens (separate, non-consuming momentary hook).
        RebindHold();

        // Toggle the fixed loupe from anywhere. Ctrl+Alt+U avoids the Mirror's Ctrl+Alt+L.
        hotkeys.TryBind(OwnerId, LoupeToggleAction,
            new HotkeyGesture(Key.U, ModifierKeys.Control | ModifierKeys.Alt),
            () =>
            {
                _loupe?.Toggle();
                _viewModel?.RefreshLoupeState();
            },
            out _);
    }

    public UserControl BuildPage() => _page ??= new MagnifierPage(_viewModel!);

    /// <summary>Restore the fixed loupe (staggered behind the shell's progress overlay).</summary>
    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        _viewModel?.RefreshSources();

        LoupeConfig c = _settings.Loupe;
        if (c.Visible && !string.IsNullOrEmpty(c.SourceTitle))
        {
            IntPtr hwnd = ResolveLoupeSource(c.SourceTitle, out string title);
            if (hwnd != IntPtr.Zero)
            {
                progress.Report("Restaurando lupa fixa...");
                _loupe!.SetSource(hwnd, title);
                _loupe.Show();
                _viewModel?.RefreshLoupeState();
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>App shutdown: stop the lens, close the loupe WITHOUT flipping its state, then flush.</summary>
    public void Shutdown()
    {
        _follow?.Deactivate();
        _follow?.Dispose();
        _loupe?.CloseKeepState();
        _holdBinding?.Dispose();
        _services.Settings.Flush();
    }

    // ---- helpers ----

    private void Persist() => _services.Settings.Set(SettingsKey, _settings);

    private void RebindHold()
    {
        _holdBinding?.Dispose();
        _holdBinding = null;
        if (_hotkeys is null)
            return;

        var gesture = new HotkeyGesture(_settings.HoldKey, _settings.HoldModifiers);
        if (gesture.IsEmpty)
            return;

        _holdBinding = _hotkeys.BindMomentary(OwnerId, gesture,
            onDown: () => _follow?.Activate(),
            onUp: () => _follow?.Deactivate());
    }

    private IntPtr ResolveLoupeSource(string sourceTitle, out string title)
    {
        WindowInfo exact = _services.Windows.ListWindows()
            .FirstOrDefault(w => string.Equals(w.Title, sourceTitle, StringComparison.Ordinal));
        if (exact.Hwnd != IntPtr.Zero)
        {
            title = exact.Title;
            return exact.Hwnd;
        }

        if (sourceTitle.StartsWith("Tibia - ", StringComparison.Ordinal))
        {
            IntPtr tibia = _services.Windows.FindTibia();
            if (tibia != IntPtr.Zero)
            {
                title = sourceTitle;
                return tibia;
            }
        }

        title = sourceTitle;
        return IntPtr.Zero;
    }
}
