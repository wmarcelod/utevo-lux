using System;
using System.Windows.Input;
using OpenTibiaVision.Core;
using OpenTibiaVision.Models;
using OpenTibiaVision.Services;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// One row in the regions dashboard. Owns the (optional) live <see cref="MirrorWindow"/> and
/// exposes Lock/Unlock, Show/Hide and Remove. The mirror window places and persists itself in
/// physical pixels via the shared config object.
/// </summary>
public sealed class RegionRowViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private readonly RegionConfig _config;
    private IntPtr _sourceHwnd;
    private MirrorWindow? _mirror;

    public event Action<RegionRowViewModel>? RemoveRequested;
    public event Action? Changed;

    public RegionRowViewModel(IAppServices services, RegionConfig config, IntPtr sourceHwnd)
    {
        _services = services;
        _config = config;
        _sourceHwnd = sourceHwnd;

        ToggleLockCommand = new RelayCommand(ToggleLock);
        ToggleVisibleCommand = new RelayCommand(ToggleVisible);
        RemoveCommand = new RelayCommand(Remove);
    }

    public RegionConfig Config => _config;

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

    public string LockButtonText => _config.Locked ? "Destravar" : "Travar";
    public string VisibleButtonText => _config.Visible ? "Ocultar" : "Mostrar";

    public string DisplayInfo =>
        $"{_config.SourceTitle}  |  recorte {_config.CropWidth}x{_config.CropHeight}px" +
        (HasSource ? "" : "  (fonte indisponivel)");

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
            _mirror = new MirrorWindow(_services, _sourceHwnd, CurrentCrop(), _config);
            _mirror.MirrorStateChanged += OnMirrorStateChanged;
            _mirror.Closed += OnMirrorClosed;
            _mirror.Show();
            _mirror.ApplyLock(_config.Locked);
        }

        _config.Visible = true;
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(VisibleButtonText));
        Changed?.Invoke();
    }

    public void HideMirror()
    {
        if (_mirror is not null)
        {
            _mirror.MirrorStateChanged -= OnMirrorStateChanged;
            _mirror.Closed -= OnMirrorClosed;
            _mirror.Close();
            _mirror = null;
        }

        _config.Visible = false;
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(VisibleButtonText));
        Changed?.Invoke();
    }

    /// <summary>Close the window without flipping the persisted Visible flag (app shutdown).</summary>
    public void CloseMirrorKeepState()
    {
        if (_mirror is not null)
        {
            _mirror.MirrorStateChanged -= OnMirrorStateChanged;
            _mirror.Closed -= OnMirrorClosed;
            _mirror.Close();
            _mirror = null;
        }
    }

    private void OnMirrorStateChanged() => Changed?.Invoke();

    private void OnMirrorClosed(object? sender, EventArgs e)
    {
        if (_mirror is not null)
        {
            _mirror = null;
            _config.Visible = false;
            OnPropertyChanged(nameof(Visible));
            OnPropertyChanged(nameof(VisibleButtonText));
        }
    }

    private RECT CurrentCrop() =>
        new RECT(_config.CropLeft, _config.CropTop, _config.CropRight, _config.CropBottom);
}
