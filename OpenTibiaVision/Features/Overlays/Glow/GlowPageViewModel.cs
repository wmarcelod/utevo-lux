using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenTibiaVision.Core;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Features.Overlays.Glow;

/// <summary>
/// Backs the Cursor-Glow dashboard: enable/disable the ring and tune colour / opacity / size /
/// thickness live. State persists through the shared <see cref="ISettingsStore"/>.
/// </summary>
public sealed class GlowPageViewModel : ViewModelBase
{
    public const string GlowKey = "overlays.glow";

    private readonly IAppServices _services;
    private readonly GlowConfig _config;
    private GlowWindow? _window;
    private string _status = "Pronto.";

    public GlowPageViewModel(IAppServices services)
    {
        _services = services;
        _config = services.Settings.Get(GlowKey, new GlowConfig());

        EnableCommand = new RelayCommand(() => SetEnabled(true));
        DisableCommand = new RelayCommand(() => SetEnabled(false));
    }

    public GlowConfig Config => _config;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand EnableCommand { get; }
    public ICommand DisableCommand { get; }

    public string Color
    {
        get => _config.Color;
        set { if (_config.Color != value) { _config.Color = value; OnPropertyChanged(); Rebuild(); } }
    }

    public double Opacity
    {
        get => _config.Opacity;
        set { if (Math.Abs(_config.Opacity - value) > 0.0001) { _config.Opacity = value; OnPropertyChanged(); Rebuild(); } }
    }

    public double OuterSize
    {
        get => _config.OuterSize;
        set { if (Math.Abs(_config.OuterSize - value) > 0.0001) { _config.OuterSize = value; OnPropertyChanged(); Rebuild(); } }
    }

    public double Thickness
    {
        get => _config.Thickness;
        set { if (Math.Abs(_config.Thickness - value) > 0.0001) { _config.Thickness = value; OnPropertyChanged(); Rebuild(); } }
    }

    // ---- enable / disable ----

    public void ToggleVisible()
    {
        SetEnabled(!_config.Visible);
        _services.ShowToast(_config.Visible ? "Brilho do cursor ativado." : "Brilho do cursor desativado.");
    }

    private void SetEnabled(bool enabled)
    {
        _config.Visible = enabled;
        if (enabled)
        {
            EnsureWindow();
            _window!.Show();
            Status = "Brilho ativado.";
        }
        else
        {
            _window?.Hide();
            Status = "Brilho desativado.";
        }
        Save();
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = new GlowWindow(_services, _config);
        _window.Closed += OnWindowClosed;
        _window.Show();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not null) { _window.Closed -= OnWindowClosed; _window = null; }
    }

    // ---- persistence ----

    private void Rebuild()
    {
        _window?.BuildRings();
        Save();
    }

    public void Save() => _services.Settings.Set(GlowKey, _config);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (_config.Visible)
        {
            SetEnabled(true);
            progress.Report("Brilho do cursor restaurado.");
        }
        else
        {
            progress.Report("Brilho do cursor inativo.");
        }
        return Task.CompletedTask;
    }

    public void Shutdown()
    {
        if (_window is not null)
        {
            _window.Closed -= OnWindowClosed;
            _window.Close();
            _window = null;
        }
        _services.Settings.Flush();
    }
}
