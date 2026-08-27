using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Features.Overlays.GridOverlay;

/// <summary>
/// Backs the Grid dashboard: pick a source window, pin a snapshot grid over its CLIENT area,
/// and tune cell size / line colour / opacity / thickness live. State persists through the
/// shared <see cref="ISettingsStore"/> (atomic + debounced).
/// </summary>
public sealed class GridPageViewModel : ViewModelBase
{
    public const string GridKey = "overlays.grid";

    private readonly IAppServices _services;
    private readonly GridConfig _config;
    private GridWindow? _window;

    private WindowInfo? _selectedSource;
    private string _status = "Pronto.";

    public GridPageViewModel(IAppServices services)
    {
        _services = services;
        _config = services.Settings.Get(GridKey, new GridConfig());

        RefreshSourcesCommand = new RelayCommand(RefreshSources);
        DetectTibiaCommand = new RelayCommand(DetectTibia);
        PinGridCommand = new RelayCommand(PinGrid);
        HideGridCommand = new RelayCommand(HideGrid);

        RefreshSources();
    }

    public GridConfig Config => _config;

    public ObservableCollection<WindowInfo> Sources { get; } = new();

    public WindowInfo? SelectedSource
    {
        get => _selectedSource;
        set => SetProperty(ref _selectedSource, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public ICommand RefreshSourcesCommand { get; }
    public ICommand DetectTibiaCommand { get; }
    public ICommand PinGridCommand { get; }
    public ICommand HideGridCommand { get; }

    // ---- live-tuned settings ----

    public int GridSize
    {
        get => _config.GridSize;
        set { if (_config.GridSize != value) { _config.GridSize = Math.Max(2, value); OnPropertyChanged(); Restyle(); } }
    }

    public string LineColor
    {
        get => _config.LineColor;
        set { if (_config.LineColor != value) { _config.LineColor = value; OnPropertyChanged(); Restyle(); } }
    }

    public double LineOpacity
    {
        get => _config.LineOpacity;
        set { if (Math.Abs(_config.LineOpacity - value) > 0.0001) { _config.LineOpacity = value; OnPropertyChanged(); Restyle(); } }
    }

    public double LineThickness
    {
        get => _config.LineThickness;
        set { if (Math.Abs(_config.LineThickness - value) > 0.0001) { _config.LineThickness = Math.Max(0.5, value); OnPropertyChanged(); Restyle(); } }
    }

    // ---- source discovery (mirrors the Mirror module) ----

    private void RefreshSources()
    {
        IntPtr previous = SelectedSource?.Hwnd ?? IntPtr.Zero;
        Sources.Clear();
        foreach (WindowInfo w in _services.Windows.ListWindows())
        {
            if (w.Title.StartsWith("OpenTibiaVision", StringComparison.Ordinal))
                continue;
            Sources.Add(w);
        }
        if (previous != IntPtr.Zero)
        {
            WindowInfo match = Sources.FirstOrDefault(w => w.Hwnd == previous);
            if (match.Hwnd != IntPtr.Zero)
                SelectedSource = match;
        }
        Status = $"{Sources.Count} janelas encontradas.";
    }

    private void DetectTibia()
    {
        IntPtr hwnd = _services.Windows.FindTibia();
        if (hwnd == IntPtr.Zero) { Status = "Cliente do Tibia nao encontrado."; return; }

        WindowInfo match = Sources.FirstOrDefault(w => w.Hwnd == hwnd);
        if (match.Hwnd == IntPtr.Zero)
        {
            RefreshSources();
            match = Sources.FirstOrDefault(w => w.Hwnd == hwnd);
        }
        if (match.Hwnd != IntPtr.Zero) { SelectedSource = match; Status = $"Tibia detectado: {match.Title}"; }
        else Status = "Tibia detectado, mas a janela nao pode ser listada.";
    }

    // ---- pin / hide ----

    private void PinGrid()
    {
        IntPtr hwnd = SelectedSource?.Hwnd ?? _services.Windows.FindTibia();
        if (hwnd == IntPtr.Zero) { Status = "Selecione uma janela fonte primeiro."; return; }

        RECT client = _services.Windows.GetClientBoundsInScreen(hwnd);
        if (client.Width <= 0 || client.Height <= 0) { Status = "Nao foi possivel obter a area do cliente."; return; }

        _config.SnapLeft = client.Left;
        _config.SnapTop = client.Top;
        _config.SnapWidth = client.Width;
        _config.SnapHeight = client.Height;
        _config.SourceTitle = SelectedSource?.Title ?? "";
        _config.Visible = true;

        // Re-pin: recreate the window at the fresh snapshot rect.
        CloseWindow();
        EnsureWindow();
        Status = "Grade fixada.";
        Save();
    }

    private void HideGrid()
    {
        _config.Visible = false;
        CloseWindow();
        Status = "Grade ocultada.";
        Save();
    }

    public void ToggleVisible()
    {
        if (_config.Visible) HideGrid();
        else PinGrid();
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = new GridWindow(_services, _config);
        _window.Closed += OnWindowClosed;
        _window.Show();          // click-through pinned overlay
        _window.Redraw();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_window is not null) { _window.Closed -= OnWindowClosed; _window = null; }
    }

    private void CloseWindow()
    {
        if (_window is not null)
        {
            _window.Closed -= OnWindowClosed;
            _window.Close();
            _window = null;
        }
    }

    // ---- persistence ----

    private void Restyle()
    {
        _window?.Redraw();
        Save();
    }

    public void Save() => _services.Settings.Set(GridKey, _config);

    public Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (!_config.Visible || !_config.HasSnapshot)
        {
            progress.Report("Grade inativa.");
            return Task.CompletedTask;
        }

        // Best-effort re-pin: if the source is still around, re-snapshot its current client rect.
        if (!string.IsNullOrEmpty(_config.SourceTitle))
        {
            WindowInfo match = _services.Windows.ListWindows()
                .FirstOrDefault(w => string.Equals(w.Title, _config.SourceTitle, StringComparison.Ordinal));
            IntPtr hwnd = match.Hwnd != IntPtr.Zero
                ? match.Hwnd
                : (_config.SourceTitle.StartsWith("Tibia - ", StringComparison.Ordinal) ? _services.Windows.FindTibia() : IntPtr.Zero);

            if (hwnd != IntPtr.Zero)
            {
                RECT client = _services.Windows.GetClientBoundsInScreen(hwnd);
                if (client.Width > 0 && client.Height > 0)
                {
                    _config.SnapLeft = client.Left; _config.SnapTop = client.Top;
                    _config.SnapWidth = client.Width; _config.SnapHeight = client.Height;
                }
            }
        }

        EnsureWindow();
        progress.Report("Grade restaurada.");
        Status = "Grade restaurada.";
        return Task.CompletedTask;
    }

    public void Shutdown()
    {
        CloseWindow(); // keep _config.Visible as-is (so it re-pins next launch)
        _services.Settings.Flush();
    }
}
