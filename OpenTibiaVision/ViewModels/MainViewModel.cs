using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using OpenTibiaVision.Models;
using OpenTibiaVision.Services;
using OpenTibiaVision.Views;

namespace OpenTibiaVision.ViewModels;

/// <summary>
/// Backs MainWindow: choose a source window, add crop regions, and manage each region's
/// mirror (lock/unlock, show/hide, remove). Persists via RegionStore.
/// </summary>
public class MainViewModel : ViewModelBase
{
    private WindowInfo? _selectedSource;
    private string _status = "Pronto.";
    private int _regionCounter;

    public MainViewModel()
    {
        RefreshSourcesCommand = new RelayCommand(RefreshSources);
        DetectTibiaCommand = new RelayCommand(DetectTibia);
        AddRegionCommand = new RelayCommand(AddRegion);

        RefreshSources();
    }

    public ObservableCollection<WindowInfo> Sources { get; } = new();
    public ObservableCollection<RegionViewModel> Regions { get; } = new();

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
    public ICommand AddRegionCommand { get; }

    /// <summary>MainWindow sets this so dialogs are owned correctly.</summary>
    public Window? OwnerWindow { get; set; }

    // ---- Source discovery ----

    private void RefreshSources()
    {
        IntPtr previous = SelectedSource?.Hwnd ?? IntPtr.Zero;

        Sources.Clear();
        foreach (WindowInfo window in WindowFinder.ListWindows())
        {
            // Skip our own windows so the list only shows mirror-able targets.
            if (window.Title.StartsWith("OpenTibiaVision", StringComparison.Ordinal))
                continue;

            Sources.Add(window);
        }

        // Preserve the previous selection if it still exists.
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
        IntPtr hwnd = WindowFinder.FindTibia();
        if (hwnd == IntPtr.Zero)
        {
            Status = "Cliente do Tibia nao encontrado.";
            return;
        }

        WindowInfo match = Sources.FirstOrDefault(w => w.Hwnd == hwnd);
        if (match.Hwnd == IntPtr.Zero)
        {
            // Not in the list (e.g. filtered/refreshed): add it explicitly.
            RefreshSources();
            match = Sources.FirstOrDefault(w => w.Hwnd == hwnd);
        }

        if (match.Hwnd != IntPtr.Zero)
        {
            SelectedSource = match;
            Status = $"Tibia detectado: {match.Title}";
        }
        else
        {
            Status = "Tibia detectado, mas a janela nao pode ser listada.";
        }
    }

    // ---- Region creation ----

    private void AddRegion()
    {
        if (SelectedSource is not WindowInfo source || source.Hwnd == IntPtr.Zero)
        {
            Status = "Selecione uma janela fonte primeiro.";
            return;
        }

        RECT bounds = DwmThumbnail.GetSourceBounds(source.Hwnd);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            Status = "Nao foi possivel obter os limites da janela fonte.";
            return;
        }

        var overlay = new RegionSelectorOverlay(bounds) { Owner = OwnerWindow };
        bool? confirmed = overlay.ShowDialog();

        if (confirmed != true || overlay.Result is not RectFraction fraction)
        {
            Status = "Selecao de regiao cancelada.";
            return;
        }

        RegionConfig config = BuildRegionConfig(source, bounds, fraction);

        var region = new RegionViewModel(config, source.Hwnd);
        WireRegion(region);
        Regions.Add(region);
        region.ShowMirror();

        Status = $"Regiao adicionada: {config.Name}.";
        Save();
    }

    private RegionConfig BuildRegionConfig(WindowInfo source, RECT bounds, RectFraction fraction)
    {
        // Crop in physical px, relative to the source's visible frame top-left.
        int left = (int)Math.Round(fraction.X * bounds.Width);
        int top = (int)Math.Round(fraction.Y * bounds.Height);
        int right = (int)Math.Round((fraction.X + fraction.W) * bounds.Width);
        int bottom = (int)Math.Round((fraction.Y + fraction.H) * bounds.Height);

        left = Math.Clamp(left, 0, bounds.Width);
        right = Math.Clamp(right, 0, bounds.Width);
        top = Math.Clamp(top, 0, bounds.Height);
        bottom = Math.Clamp(bottom, 0, bounds.Height);

        int cropWidth = Math.Max(1, right - left);
        int cropHeight = Math.Max(1, bottom - top);

        // Default mirror size: preserve the crop aspect ratio at a comfortable height.
        double aspect = (double)cropWidth / cropHeight;
        double mirrorHeight = 280;
        double mirrorWidth = Math.Clamp(mirrorHeight * aspect, 120, 960);

        return new RegionConfig
        {
            Name = $"Regiao {++_regionCounter}",
            SourceTitle = source.Title,
            SourceProcess = TryGetProcessName(source.Hwnd),
            CropLeft = left,
            CropTop = top,
            CropRight = right,
            CropBottom = bottom,
            MirrorLeft = 140,
            MirrorTop = 140,
            MirrorWidth = mirrorWidth,
            MirrorHeight = mirrorHeight,
            Visible = true,
            Locked = false
        };
    }

    private static string TryGetProcessName(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    // ---- Persistence ----

    /// <summary>Loads saved regions and shows any that were visible. Call after the main
    /// window is shown so restored mirror windows layer correctly.</summary>
    public void LoadSavedRegions()
    {
        List<RegionConfig> configs = RegionStore.Load();
        if (configs.Count == 0)
            return;

        List<WindowInfo> current = WindowFinder.ListWindows();

        foreach (RegionConfig config in configs)
        {
            IntPtr hwnd = ResolveSource(config, current);
            var region = new RegionViewModel(config, hwnd);
            WireRegion(region);
            Regions.Add(region);

            // Keep numbering ahead of restored regions.
            if (config.Name.StartsWith("Regiao ", StringComparison.Ordinal) &&
                int.TryParse(config.Name.AsSpan("Regiao ".Length), out int n) &&
                n > _regionCounter)
            {
                _regionCounter = n;
            }

            if (config.Visible && hwnd != IntPtr.Zero)
                region.ShowMirror();
        }

        Status = $"{Regions.Count} regioes carregadas.";
    }

    /// <summary>Best-effort re-binding of a saved region to a currently open window.</summary>
    private static IntPtr ResolveSource(RegionConfig config, List<WindowInfo> current)
    {
        // Exact title match first.
        WindowInfo exact = current.FirstOrDefault(w =>
            string.Equals(w.Title, config.SourceTitle, StringComparison.Ordinal));
        if (exact.Hwnd != IntPtr.Zero)
            return exact.Hwnd;

        // Tibia titles change with the character name; fall back to the client detector.
        if (config.SourceTitle.StartsWith("Tibia - ", StringComparison.Ordinal))
        {
            IntPtr tibia = WindowFinder.FindTibia();
            if (tibia != IntPtr.Zero)
                return tibia;
        }

        return IntPtr.Zero;
    }

    private void WireRegion(RegionViewModel region)
    {
        region.RemoveRequested += OnRegionRemoveRequested;
        region.Changed += Save;
    }

    private void OnRegionRemoveRequested(RegionViewModel region)
    {
        region.RemoveRequested -= OnRegionRemoveRequested;
        region.Changed -= Save;
        Regions.Remove(region);
        Status = "Regiao removida.";
        Save();
    }

    public void Save()
    {
        RegionStore.Save(Regions.Select(r => r.Config));
    }

    /// <summary>Called on app shutdown: close mirror windows and persist final state.</summary>
    public void Shutdown()
    {
        foreach (RegionViewModel region in Regions)
            region.CloseMirrorKeepState();

        Save();
    }
}
