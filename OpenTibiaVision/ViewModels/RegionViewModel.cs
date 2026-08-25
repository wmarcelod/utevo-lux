using System;
using System.Windows;
using System.Windows.Input;
using OpenTibiaVision.Models;
using OpenTibiaVision.Services;
using OpenTibiaVision.Views;

namespace OpenTibiaVision.ViewModels;

/// <summary>
/// One row in the region list. Owns the (optional) live MirrorWindow and exposes the
/// Lock/Unlock, Show/Hide and Remove commands. MVVM-lite: the view model manages its own
/// window for M1 simplicity.
/// </summary>
public class RegionViewModel : ViewModelBase
{
    private readonly RegionConfig _config;
    private IntPtr _sourceHwnd;
    private MirrorWindow? _mirror;

    /// <summary>Raised when the user removes this region.</summary>
    public event Action<RegionViewModel>? RemoveRequested;

    /// <summary>Raised whenever persisted state changes (bounds, lock, visibility).</summary>
    public event Action? Changed;

    public RegionViewModel(RegionConfig config, IntPtr sourceHwnd)
    {
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
        $"{_config.SourceTitle}  |  crop {_config.CropWidth}x{_config.CropHeight}px" +
        (HasSource ? "" : "  (fonte indisponivel)");

    // ---- Commands ----

    private void ToggleLock()
    {
        _config.Locked = !_config.Locked;
        _mirror?.ApplyLock(_config.Locked);
        OnPropertyChanged(nameof(Locked));
        OnPropertyChanged(nameof(LockButtonText));
        Changed?.Invoke();
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

    // ---- Mirror lifecycle ----

    public void ShowMirror()
    {
        if (!HasSource)
        {
            MessageBox.Show(
                "Esta regiao nao tem uma janela fonte valida. Selecione a fonte e crie a regiao novamente.",
                "OpenTibiaVision",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_mirror is null)
        {
            _mirror = new MirrorWindow(_sourceHwnd, CurrentCrop())
            {
                Left = _config.MirrorLeft,
                Top = _config.MirrorTop,
                Width = _config.MirrorWidth,
                Height = _config.MirrorHeight
            };
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

    private void OnMirrorStateChanged()
    {
        if (_mirror is null)
            return;

        _config.MirrorLeft = _mirror.Left;
        _config.MirrorTop = _mirror.Top;
        _config.MirrorWidth = _mirror.Width;
        _config.MirrorHeight = _mirror.Height;
        Changed?.Invoke();
    }

    private void OnMirrorClosed(object? sender, EventArgs e)
    {
        // The window went away (e.g. closed by other means); reflect that in state.
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
