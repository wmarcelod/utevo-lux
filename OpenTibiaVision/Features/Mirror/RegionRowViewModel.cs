using System;
using System.Windows.Input;
using OpenTibiaVision.Core;
using OpenTibiaVision.Models;
using OpenTibiaVision.Services;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// One row in the regions dashboard. Owns the (optional) live <see cref="MirrorWindow"/> and the
/// region's extended <see cref="MirrorUxState"/>. Exposes the full UX surface: zoom, opacity,
/// scale%, right-click passthrough, auto-hide, re-crop (drag or loupe), new-crop and remove.
///
/// Persistence is split cleanly: geometry/lock/crop live in the shared <c>RegionConfig</c> (saved
/// via <see cref="Changed"/> -> the page's region list); the extended UX lives in
/// <see cref="MirrorUxState"/> (saved via <see cref="MirrorUxStore"/>). Neither path touches disk
/// directly — both ride the shared atomic + debounced store (principle 7).
/// </summary>
public sealed class RegionRowViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private readonly RegionConfig _config;
    private readonly MirrorUxState _ux;
    private readonly MirrorUxStore _uxStore;
    private readonly SourceWindowWatcher _watcher;

    private IntPtr _sourceHwnd;
    private MirrorWindow? _mirror;
    private bool _isExpanded;
    private bool _watching;

    public event Action<RegionRowViewModel>? RemoveRequested;
    public event Action<RegionRowViewModel>? NewCropRequested;
    public event Action? Changed;      // region-config persistence (geometry/lock/crop)

    public RegionRowViewModel(IAppServices services, RegionConfig config, IntPtr sourceHwnd,
        MirrorUxState ux, MirrorUxStore uxStore, SourceWindowWatcher watcher)
    {
        _services = services;
        _config = config;
        _ux = ux;
        _uxStore = uxStore;
        _watcher = watcher;
        _sourceHwnd = sourceHwnd;
        _ux.ClampZoom();

        ToggleLockCommand = new RelayCommand(ToggleLock);
        ToggleVisibleCommand = new RelayCommand(ToggleVisible);
        RemoveCommand = new RelayCommand(Remove);
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
        RecropDragCommand = new RelayCommand(RecropDrag);
        RecropLoupeCommand = new RelayCommand(RecropLoupe);
        NewCropCommand = new RelayCommand(() => NewCropRequested?.Invoke(this));

        _watcher.PresenceMayHaveChanged += OnSourcePresenceMayHaveChanged;
    }

    public RegionConfig Config => _config;
    public MirrorUxState Ux => _ux;

    public IntPtr SourceHwnd
    {
        get => _sourceHwnd;
        set
        {
            _sourceHwnd = value;
            OnPropertyChanged(nameof(HasSource));
            OnPropertyChanged(nameof(DisplayInfo));
        }
    }

    public ICommand ToggleLockCommand { get; }
    public ICommand ToggleVisibleCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ToggleExpandCommand { get; }
    public ICommand RecropDragCommand { get; }
    public ICommand RecropLoupeCommand { get; }
    public ICommand NewCropCommand { get; }

    public string Name
    {
        get => _config.Name;
        set
        {
            if (_config.Name != value)
            {
                _config.Name = value;
                OnPropertyChanged();
                Changed?.Invoke();
            }
        }
    }

    public bool HasSource => _sourceHwnd != IntPtr.Zero;
    public bool Locked => _config.Locked;
    public bool Visible => _config.Visible;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string LockButtonText => _config.Locked ? "Destravar" : "Travar";
    public string VisibleButtonText => _config.Visible ? "Ocultar" : "Mostrar";
    public string ExpandButtonText => _isExpanded ? "Menos" : "Ajustes";

    public string DisplayInfo =>
        $"{_config.SourceTitle}  |  recorte {_config.CropWidth}x{_config.CropHeight}px" +
        (HasSource ? "" : "  (fonte indisponivel)");

    // ---- bound UX controls ----

    public double ZoomPercent
    {
        get => _ux.Zoom * 100;
        set
        {
            double z = Math.Clamp(value / 100.0, MirrorUxState.MinZoom, MirrorUxState.MaxZoom);
            if (Math.Abs(z - _ux.Zoom) < 0.0001)
                return;
            if (_mirror is not null)
                _mirror.SetZoom(z);       // raises UxChanged -> RefreshUx + save
            else { _ux.Zoom = z; _uxStore.Save(); OnPropertyChanged(); }
        }
    }

    public double OpacityPercent
    {
        get => _ux.Opacity / 2.55;
        set
        {
            byte o = (byte)Math.Clamp(Math.Round(value * 2.55), MirrorUxState.MinOpacity, 255);
            if (o == _ux.Opacity)
                return;
            if (_mirror is not null)
                _mirror.SetOpacity(o);    // raises UxChanged
            else { _ux.Opacity = o; _uxStore.Save(); OnPropertyChanged(); }
        }
    }

    public double ScalePercent
    {
        // Clamp into the slider's range: an out-of-range value on a TwoWay slider would clamp and
        // write back, silently resizing the window on load. Extreme mismatches read as the bound
        // (cosmetic only); dragging still sets a real size.
        get
        {
            int cropW = Math.Max(1, _config.CropWidth);
            double pct = _config.MirrorWidth * 100.0 / cropW;
            return Math.Clamp(Math.Round(pct), MirrorUxState.MinScalePercent, MirrorUxState.MaxScalePercent);
        }
        set
        {
            double pct = Math.Clamp(value, MirrorUxState.MinScalePercent, MirrorUxState.MaxScalePercent);
            int width = Math.Max(1, (int)Math.Round(_config.CropWidth * pct / 100.0));
            int height = Math.Max(1, (int)Math.Round(_config.CropHeight * pct / 100.0));
            if (_mirror is not null)
                _mirror.SetWindowSizePhysical(width, height); // SetWindowPos -> SizeChanged -> save
            else
            {
                _config.MirrorWidth = width;
                _config.MirrorHeight = height;
                Changed?.Invoke();
                OnPropertyChanged();
            }
        }
    }

    public bool RightClickPassthrough
    {
        get => _ux.RightClickPassthrough;
        set
        {
            if (_ux.RightClickPassthrough == value)
                return;
            _ux.RightClickPassthrough = value;
            _mirror?.SetPassthrough(value);
            _uxStore.Save();
            OnPropertyChanged();
        }
    }

    public bool AutoHide
    {
        get => _ux.AutoHide;
        set
        {
            if (_ux.AutoHide == value)
                return;
            _ux.AutoHide = value;
            _uxStore.Save();
            OnPropertyChanged();
            ApplyAutoHideState();
        }
    }

    // ---- commands ----

    public void ToggleLock()
    {
        _config.Locked = !_config.Locked;
        _mirror?.ApplyLock(_config.Locked);
        OnPropertyChanged(nameof(Locked));
        OnPropertyChanged(nameof(LockButtonText));
        Changed?.Invoke();
    }

    public void SetLock(bool locked)
    {
        if (_config.Locked == locked)
            return;
        ToggleLock();
    }

    private void ToggleVisible()
    {
        if (_config.Visible)
            HideMirror();
        else
            ShowMirror();
    }

    private void Remove()
    {
        HideMirror();
        RemoveRequested?.Invoke(this);
    }

    // ---- re-crop ----

    private void RecropDrag()
    {
        RECT client = _services.Windows.GetClientBoundsInScreen(_sourceHwnd);
        if (!HasSource || client.Width <= 0 || client.Height <= 0)
        {
            _services.Info("OpenTibiaVision", "Fonte indisponivel para refazer o recorte.");
            return;
        }

        var overlay = new RegionSelectorOverlay(client) { Owner = _services.ShellWindow };
        if (overlay.ShowDialog() != true || overlay.Result is not RectFraction f)
            return;

        ApplyCrop(FractionToCrop(f, client));
    }

    private void RecropLoupe()
    {
        RECT client = _services.Windows.GetClientBoundsInScreen(_sourceHwnd);
        if (!HasSource || client.Width <= 0 || client.Height <= 0)
        {
            _services.Info("OpenTibiaVision", "Fonte indisponivel para refazer o recorte.");
            return;
        }

        var controller = new LoupePickController(_services, _sourceHwnd, client);
        controller.Pick(_ux.FixedCropWidth, _ux.FixedCropHeight, (crop, boxW, boxH) =>
        {
            _ux.FixedCropWidth = boxW;
            _ux.FixedCropHeight = boxH;
            _uxStore.Save();
            ApplyCrop(crop);
        });
    }

    private RECT FractionToCrop(RectFraction f, RECT client)
    {
        int left = (int)Math.Round(f.X * client.Width);
        int top = (int)Math.Round(f.Y * client.Height);
        int right = (int)Math.Round((f.X + f.W) * client.Width);
        int bottom = (int)Math.Round((f.Y + f.H) * client.Height);

        left = Math.Clamp(left, 0, client.Width);
        right = Math.Clamp(right, 0, client.Width);
        top = Math.Clamp(top, 0, client.Height);
        bottom = Math.Clamp(bottom, 0, client.Height);

        return new RECT(left, top, Math.Max(left + 1, right), Math.Max(top + 1, bottom));
    }

    private void ApplyCrop(RECT crop)
    {
        _config.CropLeft = crop.Left;
        _config.CropTop = crop.Top;
        _config.CropRight = crop.Right;
        _config.CropBottom = crop.Bottom;

        _mirror?.UpdateCrop(crop);

        OnPropertyChanged(nameof(DisplayInfo));
        OnPropertyChanged(nameof(ScalePercent));
        Changed?.Invoke();
    }

    // ---- mirror lifecycle ----

    public void ShowMirror()
    {
        if (!HasSource)
        {
            _services.Info("OpenTibiaVision",
                "Esta regiao nao tem uma janela fonte valida. Selecione a fonte e crie a regiao novamente.");
            return;
        }

        if (_mirror is null)
        {
            _mirror = new MirrorWindow(_services, _sourceHwnd, _config, _ux);
            _mirror.MirrorStateChanged += OnMirrorStateChanged;
            _mirror.UxChanged += OnUxChanged;
            _mirror.RecropDragRequested += RecropDrag;
            _mirror.RecropLoupeRequested += RecropLoupe;
            _mirror.NewCropRequested += () => NewCropRequested?.Invoke(this);
            _mirror.RemoveRequested += Remove;
            _mirror.HideRequested += HideMirror;
            _mirror.LockToggleRequested += ToggleLock;
            _mirror.PassthroughToggled += on => RightClickPassthrough = on;
            _mirror.AutoHideToggled += on => AutoHide = on;
            _mirror.Closed += OnMirrorClosed;
            _mirror.Show();
            _mirror.ApplyLock(_config.Locked);
        }

        _config.Visible = true;
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(VisibleButtonText));
        Changed?.Invoke();

        ApplyAutoHideState();
    }

    public void HideMirror()
    {
        StopWatching();
        DetachMirror(close: true);

        _config.Visible = false;
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(VisibleButtonText));
        Changed?.Invoke();
    }

    /// <summary>Close the window without flipping the persisted Visible flag (app shutdown).</summary>
    public void CloseMirrorKeepState()
    {
        StopWatching();
        DetachMirror(close: true);
    }

    private void DetachMirror(bool close)
    {
        if (_mirror is null)
            return;

        _mirror.MirrorStateChanged -= OnMirrorStateChanged;
        _mirror.UxChanged -= OnUxChanged;
        _mirror.Closed -= OnMirrorClosed;
        if (close)
            _mirror.Close();
        _mirror = null;
    }

    private void OnMirrorStateChanged()
    {
        OnPropertyChanged(nameof(ScalePercent));
        Changed?.Invoke();
    }

    private void OnUxChanged()
    {
        OnPropertyChanged(nameof(ZoomPercent));
        OnPropertyChanged(nameof(OpacityPercent));
        _uxStore.Save();
    }

    private void OnMirrorClosed(object? sender, EventArgs e)
    {
        if (_mirror is not null)
        {
            StopWatching();
            _mirror = null;
            _config.Visible = false;
            OnPropertyChanged(nameof(Visible));
            OnPropertyChanged(nameof(VisibleButtonText));
        }
    }

    // ---- auto show/hide bound to the source ----

    private void ApplyAutoHideState()
    {
        if (_mirror is null)
            return;

        if (_ux.AutoHide)
        {
            StartWatching();
            _mirror.SetSourcePresence(MirrorInterop.IsSourcePresent(_sourceHwnd));
        }
        else
        {
            StopWatching();
            _mirror.SetSourcePresence(true); // pin visible when auto-hide is off
        }
    }

    private void StartWatching()
    {
        if (_watching || _sourceHwnd == IntPtr.Zero)
            return;
        _watcher.Watch(_sourceHwnd);
        _watching = true;
    }

    private void StopWatching()
    {
        if (!_watching)
            return;
        _watcher.Unwatch(_sourceHwnd);
        _watching = false;
    }

    private void OnSourcePresenceMayHaveChanged()
    {
        if (_mirror is null || !_ux.AutoHide || !_config.Visible)
            return;
        _mirror.SetSourcePresence(MirrorInterop.IsSourcePresent(_sourceHwnd));
    }

    /// <summary>Detach from the shared watcher (row removed from the dashboard).</summary>
    public void Dispose()
    {
        StopWatching();
        _watcher.PresenceMayHaveChanged -= OnSourcePresenceMayHaveChanged;
    }
}
