using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using UtevoLux.Core;
using UtevoLux.Features.Mirror;
using UtevoLux.Models;
using UtevoLux.Services;
using UtevoLux.ViewModels;

namespace UtevoLux.Features.Obs;

/// <summary>
/// A candidate OBS/streaming window shown in the picker. Carries the raw window title (used to
/// re-bind after a restart) and the owning process name, plus a combined <see cref="Display"/> that
/// matches the original TibiaVision picker's "Title (process)" formatting.
/// </summary>
public sealed class ObsWindowItem
{
    public ObsWindowItem(IntPtr hwnd, string title, string processName)
    {
        Hwnd = hwnd;
        Title = title;
        ProcessName = processName;
    }

    public IntPtr Hwnd { get; }
    public string Title { get; }
    public string ProcessName { get; }

    public string Display => string.IsNullOrEmpty(ProcessName) ? Title : $"{Title} ({ProcessName})";
}

/// <summary>
/// Backs the "Ferramentas OBS" dashboard. It is the OBS-focused sibling of
/// <see cref="MirrorPageViewModel"/>: pick an OBS PROJECTOR window (title like
/// "Projector - ... (obs64)"), define a crop against it (drag-select or the loupe), and manage each
/// region's live DWM mirror — but every mirror it creates is an <see cref="ObsMirrorWindow"/> with the
/// aggressive always-on-top re-assert, so the crops stay above the projector for streaming.
///
/// Reproduces the original flow (WindowSelectorDialog filtered to streaming software -> crop -> an
/// IsObsMirror region named "OBS: {process}") on top of the fork's Mirror/DWM infrastructure. Region
/// geometry persists under "obs.regions"; extended UX under "obs.ux" (see <see cref="ObsUxStore"/>).
/// </summary>
public sealed class ObsPageViewModel : ViewModelBase
{
    private const string RegionsKey = "obs.regions";

    // Streaming-software keyword filter, ported from the original TibiaVision WindowSelectorDialog
    // (localized "projector" spellings included) so the picker shows OBS/Streamlabs/XSplit/etc.
    private static readonly string[] StreamingKeywords =
    {
        "obs", "projector", "projektor", "projecteur", "proyector", "proiettore", "projetor",
        "проектор", "プロジェクター", "投影仪", "投影機", "프로젝터", "projektori", "προβολέας",
        "vetítő", "projektors", "projektorius", "прожектор", "streamlabs", "sl obs", "xsplit",
        "restream", "wirecast", "vmix", "livestream", "broadcast", "preview", "program",
        "stream", "capture"
    };

    private readonly IAppServices _services;
    private readonly ObsUxStore _uxStore;
    private readonly SourceWindowWatcher _watcher;

    private ObsWindowItem? _selectedSource;
    private string _status = "Pronto.";
    private bool _guideExpanded = true;
    private int _regionCounter;

    public ObsPageViewModel(IAppServices services)
    {
        _services = services;
        _uxStore = new ObsUxStore(services.Settings);
        _watcher = new SourceWindowWatcher();

        RefreshSourcesCommand = new RelayCommand(RefreshSources);
        DetectObsCommand = new RelayCommand(DetectObs);
        AddRegionCommand = new RelayCommand(AddRegionDrag);
        AddRegionLoupeCommand = new RelayCommand(AddRegionLoupe);
        ToggleGuideCommand = new RelayCommand(() => GuideExpanded = !GuideExpanded);

        RefreshSources();
    }

    public ObservableCollection<ObsWindowItem> Sources { get; } = new();
    public ObservableCollection<ObsRegionRowViewModel> Regions { get; } = new();

    public ObsWindowItem? SelectedSource
    {
        get => _selectedSource;
        set => SetProperty(ref _selectedSource, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public bool GuideExpanded
    {
        get => _guideExpanded;
        set
        {
            if (SetProperty(ref _guideExpanded, value))
                OnPropertyChanged(nameof(GuideToggleText));
        }
    }

    public string GuideToggleText => _guideExpanded ? "Ocultar guia" : "Mostrar guia";

    public ICommand RefreshSourcesCommand { get; }
    public ICommand DetectObsCommand { get; }
    public ICommand AddRegionCommand { get; }
    public ICommand AddRegionLoupeCommand { get; }
    public ICommand ToggleGuideCommand { get; }

    // ---- source discovery (streaming/OBS windows only) ----

    private void RefreshSources()
    {
        IntPtr previous = SelectedSource?.Hwnd ?? IntPtr.Zero;

        Sources.Clear();
        foreach (WindowInfo window in _services.Windows.ListWindows())
        {
            if (window.Title.StartsWith("Utevo Lux", StringComparison.Ordinal))
                continue;
            if (!IsStreamingSoftwareWindow(window.Title))
                continue;

            Sources.Add(new ObsWindowItem(window.Hwnd, window.Title, TryGetProcessName(window.Hwnd)));
        }

        if (previous != IntPtr.Zero)
        {
            ObsWindowItem? match = Sources.FirstOrDefault(w => w.Hwnd == previous);
            if (match is not null)
                SelectedSource = match;
        }

        Status = Sources.Count == 0
            ? "Nenhuma janela de projetor OBS encontrada. Abra um Projector no OBS e clique em Atualizar."
            : $"{Sources.Count} janela(s) de streaming encontrada(s).";
    }

    /// <summary>Pick the most likely OBS projector window (title has "projector"/"projetor", else obs*).</summary>
    private void DetectObs()
    {
        RefreshSources();

        ObsWindowItem? projector = Sources.FirstOrDefault(w =>
            w.Title.Contains("Projector", StringComparison.OrdinalIgnoreCase) ||
            w.Title.Contains("Projetor", StringComparison.OrdinalIgnoreCase));

        ObsWindowItem? obs = projector ?? Sources.FirstOrDefault(w =>
            w.ProcessName.StartsWith("obs", StringComparison.OrdinalIgnoreCase));

        if (obs is not null)
        {
            SelectedSource = obs;
            Status = $"OBS detectado: {obs.Display}";
        }
        else
        {
            Status = "Projetor do OBS nao encontrado. No OBS: botao direito na Game Capture -> Projetor (Fonte).";
        }
    }

    private static bool IsStreamingSoftwareWindow(string title)
    {
        string lower = title.ToLowerInvariant();
        return StreamingKeywords.Any(lower.Contains);
    }

    // ---- region creation ----

    private void AddRegionDrag()
    {
        if (!TryGetSelectedClient(out ObsWindowItem source, out RECT client))
            return;

        var overlay = new RegionSelectorOverlay(client) { Owner = _services.ShellWindow };
        if (overlay.ShowDialog() != true || overlay.Result is not RectFraction fraction)
        {
            Status = "Selecao de recorte cancelada.";
            return;
        }

        RECT crop = FractionToCrop(fraction, client);
        CommitNewRegion(source, client, crop, null, null);
    }

    private void AddRegionLoupe()
    {
        if (!TryGetSelectedClient(out ObsWindowItem source, out RECT client))
            return;

        var controller = new LoupePickController(_services, source.Hwnd, client);
        controller.Pick(220, 160, (crop, boxW, boxH) =>
            CommitNewRegion(source, client, crop, boxW, boxH),
            () => Status = "Selecao de recorte cancelada.");
    }

    /// <summary>"Novo espelho desta fonte" from a row's context menu: re-use that row's OBS window.</summary>
    private void NewCropFromRow(ObsRegionRowViewModel row)
    {
        IntPtr hwnd = row.SourceHwnd;
        RECT client = _services.Windows.GetClientBoundsInScreen(hwnd);
        if (hwnd == IntPtr.Zero || client.Width <= 0 || client.Height <= 0)
        {
            _services.Info("UtevoLux", "Janela do OBS indisponivel para criar um novo recorte.");
            return;
        }

        var source = new ObsWindowItem(hwnd, row.Config.SourceTitle, row.Config.SourceProcess);
        var controller = new LoupePickController(_services, hwnd, client);
        controller.Pick(row.Ux.FixedCropWidth, row.Ux.FixedCropHeight, (crop, boxW, boxH) =>
            CommitNewRegion(source, client, crop, boxW, boxH));
    }

    private bool TryGetSelectedClient(out ObsWindowItem source, out RECT client)
    {
        client = default;
        if (SelectedSource is not ObsWindowItem s || s.Hwnd == IntPtr.Zero)
        {
            source = null!;
            Status = "Selecione a janela do projetor do OBS primeiro.";
            return false;
        }

        source = s;
        client = _services.Windows.GetClientBoundsInScreen(s.Hwnd);
        if (client.Width <= 0 || client.Height <= 0)
        {
            Status = "Nao foi possivel obter a area do projetor do OBS.";
            return false;
        }
        return true;
    }

    private void CommitNewRegion(ObsWindowItem source, RECT client, RECT crop, int? fixedBoxW, int? fixedBoxH)
    {
        RegionConfig config = BuildRegionConfig(source, client, crop);

        MirrorUxState ux = _uxStore.GetOrCreate(config.Id);
        // OBS mirrors are pinned over the projector: default auto-hide OFF (the user can still
        // enable it from the mirror context menu). The aggressive topmost keeps them on top.
        ux.AutoHide = false;
        if (fixedBoxW is int fw && fixedBoxH is int fh)
        {
            ux.FixedCropWidth = fw;
            ux.FixedCropHeight = fh;
        }
        _uxStore.Save();

        ObsRegionRowViewModel row = CreateRow(config, source.Hwnd);
        Regions.Add(row);
        row.ShowMirror();

        Status = $"Recorte OBS adicionado: {config.Name}.";
        Save();
    }

    private RegionConfig BuildRegionConfig(ObsWindowItem source, RECT client, RECT crop)
    {
        int cropWidth = Math.Max(1, crop.Width);
        int cropHeight = Math.Max(1, crop.Height);

        double aspect = (double)cropWidth / cropHeight;
        int mirrorHeight = 320;
        int mirrorWidth = (int)Math.Clamp(mirrorHeight * aspect, 140, 1100);

        return new RegionConfig
        {
            // Matches the original "OBS: {process}" naming; falls back to a counter if the process
            // name is unknown so every region still gets a distinct label.
            Name = string.IsNullOrEmpty(source.ProcessName)
                ? $"OBS {++_regionCounter}"
                : $"OBS: {source.ProcessName}",
            SourceTitle = source.Title,
            SourceProcess = source.ProcessName,
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
    /// Async staggered restore behind the shell's progress overlay: load saved OBS regions and show
    /// the visible ones, re-binding each to its projector window by title (best effort).
    /// </summary>
    public async Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        List<RegionConfig> configs = _services.Settings.Get(RegionsKey, new List<RegionConfig>());
        if (configs.Count == 0)
        {
            progress.Report("Nenhum recorte OBS salvo.");
            return;
        }

        IReadOnlyList<WindowInfo> current = _services.Windows.ListWindows();
        int shown = 0;

        foreach (RegionConfig config in configs)
        {
            ct.ThrowIfCancellationRequested();

            IntPtr hwnd = ResolveSource(config, current);
            ObsRegionRowViewModel row = CreateRow(config, hwnd);
            Regions.Add(row);

            if (config.Name.StartsWith("OBS ", StringComparison.Ordinal) &&
                int.TryParse(config.Name.AsSpan("OBS ".Length), out int n) && n > _regionCounter)
            {
                _regionCounter = n;
            }

            if (config.Visible && hwnd != IntPtr.Zero)
            {
                progress.Report($"Restaurando recorte OBS {++shown}...");
                row.ShowMirror();
                await Task.Delay(50, ct);
            }
        }

        Status = $"{Regions.Count} recorte(s) OBS carregado(s).";
        progress.Report($"{Regions.Count} recorte(s) OBS carregado(s).");
    }

    /// <summary>
    /// Re-bind a saved OBS region to a live projector window: exact title match first, then a
    /// projector-aware contains match (an OBS projector's title changes with the scene/source, so a
    /// loose match on the stored title recovers most cases).
    /// </summary>
    private IntPtr ResolveSource(RegionConfig config, IReadOnlyList<WindowInfo> current)
    {
        if (string.IsNullOrEmpty(config.SourceTitle))
            return IntPtr.Zero;

        WindowInfo exact = current.FirstOrDefault(w =>
            string.Equals(w.Title, config.SourceTitle, StringComparison.OrdinalIgnoreCase));
        if (exact.Hwnd != IntPtr.Zero)
            return exact.Hwnd;

        // Loose projector match: compare against the stored title with any trailing " (process)"
        // suffix stripped, so a projector whose title drifted still re-binds.
        string wanted = StripProcessSuffix(config.SourceTitle);
        WindowInfo loose = current.FirstOrDefault(w =>
            IsStreamingSoftwareWindow(w.Title) &&
            (w.Title.Contains(wanted, StringComparison.OrdinalIgnoreCase) ||
             wanted.Contains(w.Title, StringComparison.OrdinalIgnoreCase)));
        return loose.Hwnd;
    }

    private static string StripProcessSuffix(string title)
    {
        int idx = title.LastIndexOf(" (", StringComparison.Ordinal);
        return idx > 0 ? title[..idx] : title;
    }

    private ObsRegionRowViewModel CreateRow(RegionConfig config, IntPtr hwnd)
    {
        MirrorUxState ux = _uxStore.GetOrCreate(config.Id);
        var row = new ObsRegionRowViewModel(_services, config, hwnd, ux, _uxStore, _watcher);
        WireRow(row);
        return row;
    }

    private void WireRow(ObsRegionRowViewModel row)
    {
        row.RemoveRequested += OnRowRemoveRequested;
        row.NewCropRequested += NewCropFromRow;
        row.Changed += Save;
    }

    private void OnRowRemoveRequested(ObsRegionRowViewModel row)
    {
        row.RemoveRequested -= OnRowRemoveRequested;
        row.NewCropRequested -= NewCropFromRow;
        row.Changed -= Save;
        row.Dispose();
        _uxStore.Remove(row.Config.Id);
        Regions.Remove(row);
        Status = "Recorte OBS removido.";
        Save();
    }

    /// <summary>App shutdown: close mirror windows without flipping Visible, then persist.</summary>
    public void Shutdown()
    {
        foreach (ObsRegionRowViewModel row in Regions)
        {
            row.CloseMirrorKeepState();
            row.Dispose();
        }
        _watcher.Dispose();
        _services.Settings.Flush();
    }
}
