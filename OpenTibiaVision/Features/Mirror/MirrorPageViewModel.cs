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
/// Backs the regions dashboard: pick a source window, define a crop against the game CLIENT
/// viewport (drag-select or the ~4x crop loupe), and manage each region's live DWM mirror.
/// Region geometry persists via the shared <see cref="ISettingsStore"/> under "mirror.regions";
/// the extended per-region UX (zoom/opacity/passthrough/auto-hide/fixed-box) persists via
/// <see cref="MirrorUxStore"/> under "mirror.ux" — RegionConfig (a foundation model) is untouched.
/// </summary>
public sealed class MirrorPageViewModel : ViewModelBase
{
    private const string RegionsKey = "mirror.regions";

    private readonly IAppServices _services;
    private readonly MirrorUxStore _uxStore;
    private readonly SourceWindowWatcher _watcher;

    private WindowInfo? _selectedSource;
    private string _status = "Pronto.";
    private int _regionCounter;

    public MirrorPageViewModel(IAppServices services)
    {
        _services = services;
        _uxStore = new MirrorUxStore(services.Settings);
        _watcher = new SourceWindowWatcher();

        RefreshSourcesCommand = new RelayCommand(RefreshSources);
        DetectTibiaCommand = new RelayCommand(DetectTibia);
        AddRegionCommand = new RelayCommand(AddRegionDrag);
        AddRegionLoupeCommand = new RelayCommand(AddRegionLoupe);

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
    public ICommand AddRegionLoupeCommand { get; }

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

    private void AddRegionDrag()
    {
        if (!TryGetSelectedClient(out WindowInfo source, out RECT client))
            return;

        var overlay = new RegionSelectorOverlay(client) { Owner = _services.ShellWindow };
        if (overlay.ShowDialog() != true || overlay.Result is not RectFraction fraction)
        {
            Status = "Selecao de regiao cancelada.";
            return;
        }

        RECT crop = FractionToCrop(fraction, client);
        CommitNewRegion(source.Title, TryGetProcessName(source.Hwnd), source.Hwnd, client, crop, null, null);
    }

    private void AddRegionLoupe()
    {
        if (!TryGetSelectedClient(out WindowInfo source, out RECT client))
            return;

        var controller = new LoupePickController(_services, source.Hwnd, client);
        controller.Pick(220, 160, (crop, boxW, boxH) =>
            CommitNewRegion(source.Title, TryGetProcessName(source.Hwnd), source.Hwnd, client, crop, boxW, boxH),
            () => Status = "Selecao de regiao cancelada.");
    }

    /// <summary>"Novo espelho desta fonte" from a row's context menu: re-use that row's source.</summary>
    private void NewCropFromRow(RegionRowViewModel row)
    {
        IntPtr hwnd = row.SourceHwnd;
        RECT client = _services.Windows.GetClientBoundsInScreen(hwnd);
        if (hwnd == IntPtr.Zero || client.Width <= 0 || client.Height <= 0)
        {
            _services.Info("OpenTibiaVision", "Fonte indisponivel para criar um novo espelho.");
            return;
        }

        var controller = new LoupePickController(_services, hwnd, client);
        controller.Pick(row.Ux.FixedCropWidth, row.Ux.FixedCropHeight, (crop, boxW, boxH) =>
            CommitNewRegion(row.Config.SourceTitle, row.Config.SourceProcess, hwnd, client, crop, boxW, boxH));
    }

    private bool TryGetSelectedClient(out WindowInfo source, out RECT client)
    {
        client = default;
        if (SelectedSource is not WindowInfo s || s.Hwnd == IntPtr.Zero)
        {
            source = default;
            Status = "Selecione uma janela fonte primeiro.";
            return false;
        }

        source = s;
        client = _services.Windows.GetClientBoundsInScreen(s.Hwnd);
        if (client.Width <= 0 || client.Height <= 0)
        {
            Status = "Nao foi possivel obter a area do cliente da janela fonte.";
            return false;
        }
        return true;
    }

    private void CommitNewRegion(string sourceTitle, string sourceProcess, IntPtr hwnd, RECT client,
        RECT crop, int? fixedBoxW, int? fixedBoxH)
    {
        RegionConfig config = BuildRegionConfig(sourceTitle, sourceProcess, client, crop);

        if (fixedBoxW is int fw && fixedBoxH is int fh)
        {
            MirrorUxState ux = _uxStore.GetOrCreate(config.Id);
            ux.FixedCropWidth = fw;
            ux.FixedCropHeight = fh;
            _uxStore.Save();
        }

        RegionRowViewModel row = CreateRow(config, hwnd);
        Regions.Add(row);
        row.ShowMirror();

        Status = $"Regiao adicionada: {config.Name}.";
        Save();
    }

    private RegionConfig BuildRegionConfig(string sourceTitle, string sourceProcess, RECT client, RECT crop)
    {
        int cropWidth = Math.Max(1, crop.Width);
        int cropHeight = Math.Max(1, crop.Height);

        // Default mirror size (physical px): preserve crop aspect at a comfortable size.
        double aspect = (double)cropWidth / cropHeight;
        int mirrorHeight = 320;
        int mirrorWidth = (int)Math.Clamp(mirrorHeight * aspect, 140, 1100);

        return new RegionConfig
        {
            Name = $"Regiao {++_regionCounter}",
            SourceTitle = sourceTitle,
            SourceProcess = sourceProcess,
            CropLeft = crop.Left,
            CropTop = crop.Top,
            CropRight = crop.Right,
            CropBottom = crop.Bottom,
            MirrorLeft = client.Left + 40,
            MirrorTop = client.Top + 40,
            MirrorWidth = mirrorWidth,
            MirrorHeight = mirrorHeight,
            Visible = true,
            Locked = false
        };
    }

    private static RECT FractionToCrop(RectFraction f, RECT client)
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
    /// Async staggered restore behind the shell's progress overlay: load saved regions and show
    /// the visible ones with a small inter-item delay (principle 6).
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
            RegionRowViewModel row = CreateRow(config, hwnd);
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

    private RegionRowViewModel CreateRow(RegionConfig config, IntPtr hwnd)
    {
        MirrorUxState ux = _uxStore.GetOrCreate(config.Id);
        var row = new RegionRowViewModel(_services, config, hwnd, ux, _uxStore, _watcher);
        WireRow(row);
        return row;
    }

    private void WireRow(RegionRowViewModel row)
    {
        row.RemoveRequested += OnRowRemoveRequested;
        row.NewCropRequested += NewCropFromRow;
        row.Changed += Save;
    }

    private void OnRowRemoveRequested(RegionRowViewModel row)
    {
        row.RemoveRequested -= OnRowRemoveRequested;
        row.NewCropRequested -= NewCropFromRow;
        row.Changed -= Save;
        row.Dispose();
        _uxStore.Remove(row.Config.Id);
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
        {
            row.CloseMirrorKeepState();
            row.Dispose();
        }
        _watcher.Dispose();
        _services.Settings.Flush();
    }
}
