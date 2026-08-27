using System;
using System.Windows.Input;
using UtevoLux.Core;
using UtevoLux.ViewModels;

namespace UtevoLux.Features.Overlays.Notes;

/// <summary>
/// One note in the dashboard. Owns the (kept-alive) <see cref="NoteWindow"/> and exposes every
/// editable property; setters mutate the config, repaint the live window, and raise
/// <see cref="Changed"/> so the module debounce-persists. The window is Shown/Hidden (never
/// Closed) on visibility toggles so re-showing is instant (optimization principle 3).
/// </summary>
public sealed class NoteRowViewModel : ViewModelBase
{
    private readonly IAppServices _services;
    private readonly NoteConfig _config;
    private NoteWindow? _window;

    public event Action<NoteRowViewModel>? RemoveRequested;
    public event Action? Changed;

    public NoteRowViewModel(IAppServices services, NoteConfig config)
    {
        _services = services;
        _config = config;

        ToggleLockCommand = new RelayCommand(ToggleLock);
        ToggleVisibleCommand = new RelayCommand(ToggleVisible);
        RemoveCommand = new RelayCommand(Remove);
    }

    public NoteConfig Config => _config;

    public ICommand ToggleLockCommand { get; }
    public ICommand ToggleVisibleCommand { get; }
    public ICommand RemoveCommand { get; }

    public bool Locked => _config.Locked;
    public bool Visible => _config.Visible;

    public string Preview
    {
        get
        {
            string t = (_config.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (t.Length == 0)
                return "(nota vazia)";
            return t.Length <= 40 ? t : t[..40] + "...";
        }
    }

    public string LockButtonText => _config.Locked ? "Destravar" : "Travar";
    public string VisibleButtonText => _config.Visible ? "Ocultar" : "Mostrar";

    // ---- editable properties (bound by the detail editor) ----

    public string Text
    {
        get => _config.Text;
        set
        {
            if (_config.Text == value) return;
            _config.Text = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(Preview));
            Restyle();
        }
    }

    public string BackColor
    {
        get => _config.BackColor;
        set { if (_config.BackColor != value) { _config.BackColor = value; OnPropertyChanged(); Restyle(); } }
    }

    public string TextColor
    {
        get => _config.TextColor;
        set { if (_config.TextColor != value) { _config.TextColor = value; OnPropertyChanged(); Restyle(); } }
    }

    public double BackOpacity
    {
        get => _config.BackOpacity;
        set { if (Math.Abs(_config.BackOpacity - value) > 0.0001) { _config.BackOpacity = value; OnPropertyChanged(); Restyle(); } }
    }

    public double TextOpacity
    {
        get => _config.TextOpacity;
        set { if (Math.Abs(_config.TextOpacity - value) > 0.0001) { _config.TextOpacity = value; OnPropertyChanged(); Restyle(); } }
    }

    public string FontFamily
    {
        get => _config.FontFamily;
        set { if (_config.FontFamily != value) { _config.FontFamily = value ?? "Segoe UI"; OnPropertyChanged(); Restyle(); } }
    }

    public double FontSize
    {
        get => _config.FontSize;
        set { if (Math.Abs(_config.FontSize - value) > 0.0001) { _config.FontSize = value; OnPropertyChanged(); Restyle(); } }
    }

    // ---- commands ----

    public void ToggleLock()
    {
        _config.Locked = !_config.Locked;
        _window?.ApplyLock(_config.Locked);
        OnPropertyChanged(nameof(Locked));
        OnPropertyChanged(nameof(LockButtonText));
        Changed?.Invoke();
    }

    public void SetLock(bool locked)
    {
        if (_config.Locked != locked)
            ToggleLock();
    }

    private void ToggleVisible()
    {
        if (_config.Visible)
            Hide();
        else
            Show();
    }

    private void Remove()
    {
        CloseWindow();
        RemoveRequested?.Invoke(this);
    }

    // ---- window lifecycle (kept alive across hide) ----

    public void Show()
    {
        _config.Visible = true;
        EnsureWindow();
        _window!.RefreshVisibility();
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(VisibleButtonText));
        Changed?.Invoke();
    }

    public void Hide()
    {
        _config.Visible = false;
        _window?.RefreshVisibility();
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(VisibleButtonText));
        Changed?.Invoke();
    }

    private void EnsureWindow()
    {
        if (_window is not null)
            return;

        _window = new NoteWindow(_services, _config);
        _window.OverlayStateChanged += OnWindowStateChanged;
        _window.Closed += OnWindowClosed;
        _window.Show();               // creates the HWND + applies chrome + initial placement
        _window.ApplyLock(_config.Locked);
        _window.ApplyStyle();
    }

    private void OnWindowStateChanged()
    {
        _window?.PersistBounds();
        Changed?.Invoke();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not null)
        {
            _window.OverlayStateChanged -= OnWindowStateChanged;
            _window.Closed -= OnWindowClosed;
            _window = null;
        }
    }

    /// <summary>App shutdown: close the window WITHOUT flipping the persisted Visible flag.</summary>
    public void CloseWindowKeepState() => CloseWindow();

    private void CloseWindow()
    {
        if (_window is not null)
        {
            _window.OverlayStateChanged -= OnWindowStateChanged;
            _window.Closed -= OnWindowClosed;
            _window.Close();
            _window = null;
        }
    }

    // ---- helpers ----

    private void Restyle()
    {
        _window?.ApplyStyle();
        Changed?.Invoke();
    }
}
