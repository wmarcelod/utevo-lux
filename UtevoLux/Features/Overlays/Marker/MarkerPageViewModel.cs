using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using UtevoLux.Core;
using UtevoLux.ViewModels;

namespace UtevoLux.Features.Overlays.Marker;

/// <summary>
/// Backs the Marker dashboard: show/park a passive circle or arrow, lock it (click-through) or
/// unlock to drag it, and tune shape / colour / opacity / size. It does NOT track anything — it
/// is decoration. State persists through the shared <see cref="ISettingsStore"/>.
/// </summary>
public sealed class MarkerPageViewModel : ViewModelBase
{
    public const string MarkerKey = "overlays.marker";

    private readonly IAppServices _services;
    private readonly MarkerConfig _config;
    private MarkerWindow? _window;
    private string _status = "Pronto.";

    public MarkerPageViewModel(IAppServices services)
    {
        _services = services;
        _config = services.Settings.Get(MarkerKey, new MarkerConfig());

        ShowCommand = new RelayCommand(Show);
        HideCommand = new RelayCommand(Hide);
        ToggleLockCommand = new RelayCommand(ToggleLock);
    }

    public MarkerConfig Config => _config;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool Locked => _config.Locked;
    public string LockButtonText => _config.Locked ? "Destravar (arrastar)" : "Travar (fixar)";

    public ICommand ShowCommand { get; }
    public ICommand HideCommand { get; }
    public ICommand ToggleLockCommand { get; }

    public string Shape
    {
        get => _config.Shape;
        set { if (_config.Shape != value) { _config.Shape = value; OnPropertyChanged(); Restyle(); } }
    }

    public string Color
    {
        get => _config.Color;
        set { if (_config.Color != value) { _config.Color = value; OnPropertyChanged(); Restyle(); } }
    }

    public double Opacity
    {
        get => _config.Opacity;
        set { if (Math.Abs(_config.Opacity - value) > 0.0001) { _config.Opacity = value; OnPropertyChanged(); Restyle(); } }
    }

    public double Size
    {
        get => _config.Size;
        set { if (Math.Abs(_config.Size - value) > 0.0001) { _config.Size = value; OnPropertyChanged(); Restyle(); } }
    }

    // ---- show / hide / lock ----

    public void ToggleVisible()
    {
        if (_config.Visible) Hide();
        else Show();
        _services.ShowToast(_config.Visible ? "Marcador exibido." : "Marcador ocultado.");
    }

    public void Show()
    {
        _config.Visible = true;
        EnsureWindow();
        _window!.Show();
        Status = _config.Locked ? "Marcador fixado." : "Marcador exibido (arraste para posicionar).";
        Save();
    }

    public void Hide()
    {
        _config.Visible = false;
        _window?.Hide();
        Status = "Marcador ocultado.";
        Save();
    }

    private void ToggleLock()
    {
        _config.Locked = !_config.Locked;
        _window?.ApplyLock(_config.Locked);
        OnPropertyChanged(nameof(Locked));
        OnPropertyChanged(nameof(LockButtonText));
        Status = _config.Locked ? "Marcador fixado (click-through)." : "Marcador destravado (arraste).";
        Save();
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = new MarkerWindow(_services, _config);
        _window.OverlayStateChanged += OnWindowStateChanged;
        _window.Closed += OnWindowClosed;
        _window.Show();
        _window.ApplyLock(_config.Locked);
        _window.ApplyStyle();
    }

    private void OnWindowStateChanged()
    {
        _window?.PersistBounds();
        Save();
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

    // ---- persistence ----

    private void Restyle()
    {
        _window?.ApplyStyle();
        Save();
    }

    public void Save() => _services.Settings.Set(MarkerKey, _config);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (_config.Visible)
        {
            Show();
            progress.Report("Marcador restaurado.");
        }
        else
        {
            progress.Report("Marcador inativo.");
        }
        return Task.CompletedTask;
    }

    public void Shutdown()
    {
        if (_window is not null)
        {
            _window.OverlayStateChanged -= OnWindowStateChanged;
            _window.Closed -= OnWindowClosed;
            _window.Close();
            _window = null;
        }
        _services.Settings.Flush();
    }
}
