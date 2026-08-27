using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Shell;

/// <summary>
/// The single chromeless window: sidebar nav, kept-alive pages, tray, UI scale, and staggered
/// async startup. Discovers feature modules by reflection and builds one nav entry each; the
/// Mirror module supplies the first (Regions) page.
/// </summary>
public partial class ShellWindow : Window, IShellController
{
    private const double MinScale = 0.8;
    private const double MaxScale = 1.6;
    private const double ScaleStep = 0.1;

    private const string ScaleKey = "ui.scale";
    private const string BoundsKey = "shell.bounds";

    private readonly AppServices _services;
    private readonly ShellViewModel _vm = new();
    private readonly bool _startMinimized;
    private readonly List<IFeatureModule> _modules = new();

    private ScaleGuard? _scaleGuard;
    private TrayIcon? _tray;
    private double _uiScale = 1.0;
    private bool _started;

    public ShellWindow(AppServices services, bool startMinimized)
    {
        _services = services;
        _startMinimized = startMinimized;

        InitializeComponent();
        DataContext = _vm;
        _services.ShellWindow = this;

        RestoreScale();
        RestoreSavedBounds();

        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // ---------- startup ----------

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
            return;
        _started = true;

        _scaleGuard = new ScaleGuard(this, _services.Dpi);
        SetupTray();

        if (_startMinimized)
            MinimizeToTray();

        await RunStartupAsync();
    }

    private async Task RunStartupAsync()
    {
        var progress = new Progress<string>(s => StartupStatus.Text = s);
        var ct = CancellationToken.None;

        ((IProgress<string>)progress).Report("Carregando modulos...");
        IReadOnlyList<IFeatureModule> modules = ModuleCatalog.Discover();

        var restorers = new List<IStartupRestore>();

        // Build one nav entry + kept-alive page per module, staggered so the shell paints early.
        foreach (IFeatureModule module in modules)
        {
            try
            {
                module.Init(_services);
                module.RegisterHotkeys(_services.Hotkeys);

                var navItem = new NavItem(module.Id, module.Title, module.Icon, module.BuildPage);
                AddPage(navItem);
                _modules.Add(module);

                if (module is IStartupRestore r)
                    restorers.Add(r);
            }
            catch (Exception ex)
            {
                _services.ShowToast($"Falha ao carregar modulo {module.Title}: {ex.Message}");
            }

            await Task.Delay(50, ct); // inter-item stagger
        }

        // Built-in Settings page (not a module).
        var settingsNav = new NavItem("settings", "Configuracoes",
            (Geometry)FindResource("Icon.Settings"),
            () => new SettingsPage(_services, this));
        AddPage(settingsNav);

        _vm.SelectFirst();

        // Global hotkey hooks come online once the UI exists.
        _services.Start();

        // Restore saved state (regions etc.) behind the overlay.
        foreach (IStartupRestore r in restorers)
        {
            try { await r.RestoreAsync(progress, ct); }
            catch (Exception ex) { _services.ShowToast($"Falha ao restaurar: {ex.Message}"); }
        }

        HideOverlay();
    }

    private void AddPage(NavItem item)
    {
        _vm.Add(item);

        UserControl page = item.Page; // built once here
        var binding = new Binding(nameof(NavItem.IsActive))
        {
            Source = item,
            Converter = new BooleanToVisibilityConverter()
        };
        page.SetBinding(VisibilityProperty, binding);
        PageHost.Children.Add(page);
    }

    private void HideOverlay()
    {
        StartupBar.IsIndeterminate = false;
        ProgressOverlay.Visibility = Visibility.Collapsed;
    }

    // ---------- window chrome ----------

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Manual rounded clip so content honours the 10px corner radius.
        double r = 10;
        RootChrome.Clip = new RectangleGeometry(
            new Rect(0, 0, RootChrome.ActualWidth, RootChrome.ActualHeight), r, r);
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        try { DragMove(); } catch (InvalidOperationException) { /* button already released */ }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // ---------- tray / minimize-to-tray ----------

    private void SetupTray()
    {
        _tray = new TrayIcon();
        _tray.OpenRequested += RestoreFromTray;
        _tray.ExitRequested += Close;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            MinimizeToTray();
    }

    private void MinimizeToTray()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    // ---------- UI scale (IShellController) ----------

    public double UiScale => _uiScale;

    public void SetUiScale(double scale)
    {
        _uiScale = Math.Clamp(Math.Round(scale, 2), MinScale, MaxScale);
        ScaleRoot.LayoutTransform = Math.Abs(_uiScale - 1.0) < 0.001
            ? Transform.Identity
            : new ScaleTransform(_uiScale, _uiScale);
        _services.Settings.Set(ScaleKey, _uiScale);
    }

    public void StepUiScale(int direction) => SetUiScale(_uiScale + direction * ScaleStep);

    public void ResetUiScale() => SetUiScale(1.0);

    private void RestoreScale() => SetUiScale(_services.Settings.Get(ScaleKey, 1.0));

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        switch (e.Key)
        {
            case Key.OemPlus:
            case Key.Add:
                StepUiScale(+1); e.Handled = true; break;
            case Key.OemMinus:
            case Key.Subtract:
                StepUiScale(-1); e.Handled = true; break;
            case Key.D0:
            case Key.NumPad0:
                ResetUiScale(); e.Handled = true; break;
        }
    }

    // ---------- bounds persistence ----------

    private record struct Bounds(double Left, double Top, double Width, double Height);

    private void RestoreSavedBounds()
    {
        if (!_services.Settings.TryGet(BoundsKey, out Bounds b))
            return;
        if (b.Width < MinWidth || b.Height < MinHeight)
            return;

        // Only restore if the top-left is plausibly on a screen.
        if (b.Left > -50 && b.Top > -50 && b.Left < SystemParameters.VirtualScreenWidth &&
            b.Top < SystemParameters.VirtualScreenHeight)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
        }
    }

    private void SaveBounds()
    {
        if (WindowState != WindowState.Normal)
            return;
        _services.Settings.Set(BoundsKey, new Bounds(Left, Top, Width, Height));
    }

    // ---------- shutdown ----------

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveBounds();

        // Let modules close their overlay windows WITHOUT flipping persisted state, before flush.
        foreach (IFeatureModule module in _modules)
            if (module is IShutdownHook hook)
            {
                try { hook.Shutdown(); } catch { /* never block shutdown */ }
            }

        _scaleGuard?.Dispose();
        _tray?.Dispose();
        UI.Toast.Instance.Shutdown();
        _services.Settings.Flush();
        // AppServices disposal (hotkeys/profiles) happens in App.OnExit.
    }
}
