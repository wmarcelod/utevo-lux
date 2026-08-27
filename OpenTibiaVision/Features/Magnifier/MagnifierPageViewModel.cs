using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Features.Magnifier;

/// <summary>
/// Backs the Magnifier dashboard: the follow-cursor lens options (hold gesture, shape, size,
/// default zoom, opacity) and the fixed-crop loupe (source, zoom, crop centre, shape, opacity,
/// show/hide, lock). Follow-lens fields are read live by the controller each frame, so most edits
/// take effect on the next hold with no extra plumbing; the loupe is poked directly.
/// </summary>
public sealed class MagnifierPageViewModel : ViewModelBase
{
    /// <summary>Curated hold gestures (the shell's momentary hook is non-consuming, so these still
    /// reach the game; pick one that does not clash with your gameplay binds).</summary>
    private static readonly (string Label, Key Key, ModifierKeys Mods)[] HoldPresets =
    {
        ("Ctrl+Alt+M", Key.M, ModifierKeys.Control | ModifierKeys.Alt),
        ("Ctrl+Alt+Z", Key.Z, ModifierKeys.Control | ModifierKeys.Alt),
        ("Ctrl+Shift+Espaco", Key.Space, ModifierKeys.Control | ModifierKeys.Shift),
        ("Alt+X", Key.X, ModifierKeys.Alt),
    };

    private static readonly string[] ShapeLabels = { "Arredondada", "Circular" };

    private readonly IAppServices _services;
    private readonly MagnifierSettings _settings;
    private readonly FixedLoupeController _loupe;
    private readonly Action _persist;
    private readonly Action _rebindHold;

    private WindowInfo? _selectedSource;
    private bool _loadingSource;
    private string _status = "Pronto.";

    internal MagnifierPageViewModel(IAppServices services, MagnifierSettings settings,
        FixedLoupeController loupe, Action persist, Action rebindHold)
    {
        _services = services;
        _settings = settings;
        _loupe = loupe;
        _persist = persist;
        _rebindHold = rebindHold;

        RefreshSourcesCommand = new RelayCommand(RefreshSources);
        DetectTibiaCommand = new RelayCommand(DetectTibia);
        ShowHideLoupeCommand = new RelayCommand(ToggleLoupe);
        ToggleLockLoupeCommand = new RelayCommand(ToggleLoupeLock);
    }

    // ---- static option lists ----

    public IReadOnlyList<string> HoldGestureOptions { get; } =
        HoldPresets.Select(p => p.Label).ToArray();

    public IReadOnlyList<string> ShapeOptions => ShapeLabels;

    // ---- follow lens ----

    public string SelectedHoldGesture
    {
        get
        {
            var match = HoldPresets.FirstOrDefault(p =>
                p.Key == _settings.HoldKey && p.Mods == _settings.HoldModifiers);
            return match.Label ?? HoldPresets[0].Label;
        }
        set
        {
            var match = HoldPresets.FirstOrDefault(p => p.Label == value);
            if (match.Label is null)
                return;
            if (_settings.HoldKey == match.Key && _settings.HoldModifiers == match.Mods)
                return;
            _settings.HoldKey = match.Key;
            _settings.HoldModifiers = match.Mods;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FollowStatus));
            _rebindHold();
            _persist();
        }
    }

    public string SelectedFollowShape
    {
        get => ShapeToLabel(_settings.Shape);
        set
        {
            LensShape shape = LabelToShape(value);
            if (_settings.Shape == shape)
                return;
            _settings.Shape = shape;
            OnPropertyChanged();
            _persist();
        }
    }

    public double LensSize
    {
        get => _settings.LensSize;
        set
        {
            int v = (int)Math.Round(value);
            if (_settings.LensSize == v)
                return;
            _settings.LensSize = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LensSizeText));
            _persist();
        }
    }

    public string LensSizeText => $"{_settings.LensSize} px";

    public double DefaultZoom
    {
        get => _settings.DefaultZoom;
        set
        {
            double v = Math.Round(value / _settings.ZoomStep) * _settings.ZoomStep;
            v = Clamp(v, _settings.ZoomMin, _settings.ZoomMax);
            if (Math.Abs(_settings.DefaultZoom - v) < 0.001)
                return;
            _settings.DefaultZoom = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefaultZoomText));
            _persist();
        }
    }

    public string DefaultZoomText => $"{_settings.DefaultZoom:0.00}x";

    public double FollowOpacityPercent
    {
        get => Math.Round(_settings.Opacity / 255.0 * 100);
        set
        {
            byte v = PercentToByte(value);
            if (_settings.Opacity == v)
                return;
            _settings.Opacity = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FollowOpacityText));
            _persist();
        }
    }

    public string FollowOpacityText => $"{FollowOpacityPercent:0}%";

    public double ZoomMin => _settings.ZoomMin;
    public double ZoomMax => _settings.ZoomMax;
    public double ZoomStep => _settings.ZoomStep;

    public string FollowStatus =>
        $"Segure {SelectedHoldGesture} para ativar; role o mouse para ajustar o zoom " +
        $"({_settings.ZoomMin:0.0}x–{_settings.ZoomMax:0.0}x).";

    // ---- fixed loupe ----

    public ObservableCollection<WindowInfo> Sources { get; } = new();

    public WindowInfo? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (!SetProperty(ref _selectedSource, value))
                return;
            if (_loadingSource)
                return;
            if (value is WindowInfo w && w.Hwnd != IntPtr.Zero)
            {
                _loupe.SetSource(w.Hwnd, w.Title);
                Status = $"Fonte da lupa fixa: {w.Title}";
            }
        }
    }

    public string SelectedLoupeShape
    {
        get => ShapeToLabel(_settings.Loupe.Shape);
        set
        {
            LensShape shape = LabelToShape(value);
            if (_settings.Loupe.Shape == shape)
                return;
            _loupe.SetShape(shape);
            OnPropertyChanged();
        }
    }

    public double LoupeZoom
    {
        get => _settings.Loupe.Zoom;
        set
        {
            double v = Math.Round(value / _settings.ZoomStep) * _settings.ZoomStep;
            v = Clamp(v, _settings.ZoomMin, _settings.ZoomMax);
            if (Math.Abs(_settings.Loupe.Zoom - v) < 0.001)
                return;
            _loupe.SetZoom(v);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LoupeZoomText));
        }
    }

    public string LoupeZoomText => $"{_settings.Loupe.Zoom:0.00}x";

    public double LoupeCenterX
    {
        get => _settings.Loupe.CenterX;
        set
        {
            if (Math.Abs(_settings.Loupe.CenterX - value) < 0.001)
                return;
            _loupe.SetCenter(value, _settings.Loupe.CenterY);
            OnPropertyChanged();
        }
    }

    public double LoupeCenterY
    {
        get => _settings.Loupe.CenterY;
        set
        {
            if (Math.Abs(_settings.Loupe.CenterY - value) < 0.001)
                return;
            _loupe.SetCenter(_settings.Loupe.CenterX, value);
            OnPropertyChanged();
        }
    }

    public double LoupeOpacityPercent
    {
        get => Math.Round(_settings.Loupe.Opacity / 255.0 * 100);
        set
        {
            byte v = PercentToByte(value);
            if (_settings.Loupe.Opacity == v)
                return;
            _settings.Loupe.Opacity = v;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LoupeOpacityText));
            _loupe.Refresh();
            _persist();
        }
    }

    public string LoupeOpacityText => $"{LoupeOpacityPercent:0}%";

    public string LoupeVisibleButtonText => _loupe.IsVisible ? "Ocultar" : "Mostrar";
    public string LoupeLockButtonText => _loupe.IsLocked ? "Destravar" : "Travar";

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    // ---- commands ----

    public ICommand RefreshSourcesCommand { get; }
    public ICommand DetectTibiaCommand { get; }
    public ICommand ShowHideLoupeCommand { get; }
    public ICommand ToggleLockLoupeCommand { get; }

    public void RefreshSources()
    {
        IntPtr previous = _selectedSource?.Hwnd ?? IntPtr.Zero;

        _loadingSource = true;
        try
        {
            Sources.Clear();
            foreach (WindowInfo window in _services.Windows.ListWindows())
            {
                if (window.Title.StartsWith("OpenTibiaVision", StringComparison.Ordinal))
                    continue;
                Sources.Add(window);
            }

            // Re-select the current source, or the persisted loupe source, without re-binding it.
            WindowInfo match = default;
            if (previous != IntPtr.Zero)
                match = Sources.FirstOrDefault(w => w.Hwnd == previous);
            if (match.Hwnd == IntPtr.Zero && !string.IsNullOrEmpty(_settings.Loupe.SourceTitle))
                match = Sources.FirstOrDefault(w =>
                    string.Equals(w.Title, _settings.Loupe.SourceTitle, StringComparison.Ordinal));

            if (match.Hwnd != IntPtr.Zero)
                SetProperty(ref _selectedSource, match, nameof(SelectedSource));
        }
        finally
        {
            _loadingSource = false;
        }

        Status = $"{Sources.Count} janelas encontradas.";
    }

    private void DetectTibia()
    {
        IntPtr hwnd = _services.Windows.FindTibia();
        if (hwnd == IntPtr.Zero)
        {
            Status = "Cliente do Tibia nao encontrado.";
            return;
        }

        WindowInfo match = Sources.FirstOrDefault(w => w.Hwnd == hwnd);
        if (match.Hwnd == IntPtr.Zero)
        {
            RefreshSources();
            match = Sources.FirstOrDefault(w => w.Hwnd == hwnd);
        }

        if (match.Hwnd != IntPtr.Zero)
        {
            SelectedSource = match; // setter binds the loupe source
            Status = $"Tibia detectado: {match.Title}";
        }
        else
        {
            Status = "Tibia detectado, mas a janela nao pode ser listada.";
        }
    }

    private void ToggleLoupe()
    {
        _loupe.Toggle();
        OnPropertyChanged(nameof(LoupeVisibleButtonText));
        Status = _loupe.IsVisible ? "Lupa fixa visivel." : "Lupa fixa oculta.";
    }

    private void ToggleLoupeLock()
    {
        _loupe.SetLock(!_loupe.IsLocked);
        OnPropertyChanged(nameof(LoupeLockButtonText));
        Status = _loupe.IsLocked ? "Lupa fixa travada (click-through)." : "Lupa fixa destravada.";
    }

    /// <summary>Reflect loupe state changed elsewhere (restore / hotkey toggle) in the buttons.</summary>
    public void RefreshLoupeState()
    {
        OnPropertyChanged(nameof(LoupeVisibleButtonText));
        OnPropertyChanged(nameof(LoupeLockButtonText));
    }

    // ---- helpers ----

    private static string ShapeToLabel(LensShape shape) =>
        shape == LensShape.Circle ? ShapeLabels[1] : ShapeLabels[0];

    private static LensShape LabelToShape(string label) =>
        label == ShapeLabels[1] ? LensShape.Circle : LensShape.RoundedRect;

    private static byte PercentToByte(double percent)
    {
        double v = Clamp(percent, 20, 100) / 100.0 * 255.0;
        return (byte)Math.Round(v);
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
}
