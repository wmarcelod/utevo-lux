using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenTibiaVision.Core;
using OpenTibiaVision.Models;
using OpenTibiaVision.Services;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// Backs the regions dashboard: pick a source window, drag-select a crop against the game
/// CLIENT viewport, and manage each region's live DWM mirror. Regions persist through the
/// shared <see cref="ISettingsStore"/> (atomic + 400 ms debounced) under one key.
/// </summary>
public sealed class MirrorPageViewModel : ViewModelBase
{
    private const string RegionsKey = "mirror.regions";

    private readonly IAppServices _services;
    private WindowInfo? _selectedSource;
    private string _status = "Pronto.";
    private int _regionCounter;

    public MirrorPageViewModel(IAppServices services)
    {
        _services = services;

        RefreshSourcesCommand = new RelayCommand(RefreshSources);
        DetectTibiaCommand = new RelayCommand(DetectTibia);
        AddRegionCommand = new RelayCommand(AddRegion);

        RefreshSources();
    }

    public ObservableCollection<WindowInfo> Sources { get; } = new();
    public ObservableCollection<RegionRowViewModel> Regions { get; } = new();

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

    // ---- source discovery ----

    private void RefreshSources()
    {
        IntPtr previous = SelectedSource?.Hwnd ?? IntPtr.Zero;

        Sources.Clear();
        foreach (WindowInfo window in _services.Windows.ListWindows())
        {
            if (window.Title.StartsWith("OpenTibiaVision", StringComparison.Ordinal))
                continue;
            Sources.Add(window);
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
            SelectedSource = match;
            Status = $"Tibia detectado: {match.Title}";
        }
        else
        {
            Status = "Tibia detectado, mas a janela nao pode ser listada.";
        }
    }

    // ---- region creation ----

    private void AddRegion()
    {
        if (SelectedSource is not WindowInfo source || source.Hwnd == IntPtr.Zero)
        {
            Status = "Selecione uma janela fonte primeiro.";
            return;
        }

        // Crop against the CLIENT area (game viewport), matching fSourceClientAreaOnly.
        RECT client = _services.Windows.GetClientBoundsInScreen(source.Hwnd);
        if (client.Width <= 0 || client.Height <= 0)
        {
            Status = "Nao foi possivel obter a area do cliente da janela fonte.";
            return;
        }

        var overlay = new RegionSelectorOverlay(client) { Owner = _services.ShellWindow };
        bool? confirmed = overlay.ShowDialog();

        if (confirmed != true || overlay.Result is not RectFraction fraction)
        {
            Status = "Selecao de regiao cancelada.";
            return;
        }

        RegionConfig config = BuildRegionConfig(source, client, fraction);

        var row = new RegionRowViewModel(_services, config, source.Hwnd);
        WireRow(row);
        Regions.Add(row);
        row.ShowMirror();

        Status = $"Regiao adicionada: {config.Name}.";
        Save();
    }

    private RegionConfig BuildRegionConfig(WindowInfo source, RECT client, RectFraction fraction)
    {
        // Crop in physical px, relative to the CLIENT area top-left.
        int left = (int)Math.Round(fraction.X * client.Width);
        int top = (int)Math.Round(fraction.Y * client.Height);
        int right = (int)Math.Round((fraction.X + fraction.W) * client.Width);
        int bottom = (int)Math.Round((fraction.Y + fraction.H) * client.Height);

        left = Math.Clamp(left, 0, client.Width);
        right = Math.Clamp(right, 0, client.Width);
        top = Math.Clamp(top, 0, client.Height);
        bottom = Math.Clamp(bottom, 0, client.Height);

        int cropWidth = Math.Max(1, right - left);
        int cropHeight = Math.Max(1, bottom - top);

        // Default mirror size (physical px): preserve crop aspect at a comfortable size.
        double aspect = (double)cropWidth / cropHeight;
        int mirrorHeight = 320;
        int mirrorWidth = (int)Math.Clamp(mirrorHeight * aspect, 140, 1100);

        return new RegionConfig
        {
            Name = $"Regiao {++_regionCounter}",
            SourceTitle = source.Title,
            SourceProcess = TryGetProcessName(source.Hwnd),
            CropLeft = left,
            CropTop = top,
            CropRight = right,
            CropBottom = bottom,
            MirrorLeft = client.Left + 40,
            MirrorTop = client.Top + 40,
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

    // ---- persistence via shared ISettingsStore ----

    public void Save()
    {
        _services.Settings.Set(RegionsKey, Regions.Select(r => r.Config).ToList());
    }

    /// <summary>
    /// Async staggered restore behind the shell's progress overlay: load saved regions and
    /// show the visible ones with a small inter-item delay (optimization principle 6).
    /// </summary>
    public async Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        List<RegionConfig> configs = _services.Settings.Get(RegionsKey, new List<RegionConfig>());
        if (configs.Count == 0)
        {
            progress.Report("Nenhuma regiao salva.");
            return;
        }

        IReadOnlyList<WindowInfo> current = _services.Windows.ListWindows();
        int shown = 0;

        foreach (RegionConfig config in configs)
        {
            ct.ThrowIfCancellationRequested();

            IntPtr hwnd = ResolveSource(config, current);
            var row = new RegionRowViewModel(_services, config, hwnd);
            WireRow(row);
            Regions.Add(row);

            if (config.Name.StartsWith("Regiao ", StringComparison.Ordinal) &&
                int.TryParse(config.Name.AsSpan("Regiao ".Length), out int n) && n > _regionCounter)
            {
                _regionCounter = n;
            }

            if (config.Visible && hwnd != IntPtr.Zero)
            {
                progress.Report($"Restaurando espelho {++shown}...");
                row.ShowMirror();
                await Task.Delay(50, ct); // inter-item stagger keeps the shell responsive
            }
        }

        Status = $"{Regions.Count} regioes carregadas.";
        progress.Report($"{Regions.Count} regioes carregadas.");
    }

    private IntPtr ResolveSource(RegionConfig config, IReadOnlyList<WindowInfo> current)
    {
        WindowInfo exact = current.FirstOrDefault(w =>
            string.Equals(w.Title, config.SourceTitle, StringComparison.Ordinal));
        if (exact.Hwnd != IntPtr.Zero)
            return exact.Hwnd;

        if (config.SourceTitle.StartsWith("Tibia - ", StringComparison.Ordinal))
        {
            IntPtr tibia = _services.Windows.FindTibia();
            if (tibia != IntPtr.Zero)
                return tibia;
        }

        return IntPtr.Zero;
    }

    private void WireRow(RegionRowViewModel row)
    {
        row.RemoveRequested += OnRowRemoveRequested;
        row.Changed += Save;
    }

    private void OnRowRemoveRequested(RegionRowViewModel row)
    {
        row.RemoveRequested -= OnRowRemoveRequested;
        row.Changed -= Save;
        Regions.Remove(row);
        Status = "Regiao removida.";
        Save();
    }

    // ---- hotkey action ----

    /// <summary>Lock or unlock every visible mirror at once (bound to a global hotkey).</summary>
    public void ToggleLockAll()
    {
        bool anyUnlocked = Regions.Any(r => r.Visible && !r.Locked);
        foreach (RegionRowViewModel row in Regions.Where(r => r.Visible))
            row.SetLock(anyUnlocked);
        _services.ShowToast(anyUnlocked ? "Espelhos travados." : "Espelhos destravados.");
    }

    /// <summary>App shutdown: close mirror windows without flipping Visible, then persist.</summary>
    public void Shutdown()
    {
        foreach (RegionRowViewModel row in Regions)
            row.CloseMirrorKeepState();
        _services.Settings.Flush();
    }
}
