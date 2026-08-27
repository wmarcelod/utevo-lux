using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using OpenTibiaVision.Core;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// Backs the Audio / Timers / Alerts dashboard. Owns the shared runtime (sound engine, sound
/// library, the two wall-clock tickers) and the collection of timer rows. It also owns the two
/// pieces of shared plumbing rows cannot do alone:
///  - the SINGLE 25 ms tick loop, subscribed only while at least one row is running (principle 4);
///  - the global hotkey bindings, where several rows may share one gesture (multi-row fan-out)
///    on top of each row's own multi-duration fan-out.
/// Timers, sounds and master audio state persist through the shared settings store (principle 7).
/// </summary>
public sealed class AudioPageViewModel : ViewModelBase
{
    private const string TimersKey = "audio.timers";
    private const string MasterKey = "audio.master";

    // Per-timer hotkeys use a distinct owner tag so rebinding them never disturbs the module-level
    // hotkeys (which are owned by the module Id "audio").
    public const string OwnerTimers = "audio.timers";

    private readonly IAppServices _services;
    private readonly IHotkeyManager _hotkeys;
    private readonly AudioRuntime _runtime;
    private readonly SoundEngine _sound;

    private readonly Dictionary<HotkeyGesture, List<TimerRowViewModel>> _gestureMap = new();
    private readonly HashSet<TimerRowViewModel> _runningRows = new();
    private IDisposable? _countdownSub;

    private string _status = "Pronto.";

    public AudioPageViewModel(IAppServices services)
    {
        _services = services;
        _hotkeys = services.Hotkeys;

        var library = new SoundLibrary(services.Settings);
        _sound = new SoundEngine(SoundEngine.CreateDefaultBackend());

        _runtime = new AudioRuntime(
            services,
            _sound,
            library,
            countdownTicker: new WallClockTicker(25, System.Windows.Threading.DispatcherPriority.Render),
            barTicker: new WallClockTicker(50, System.Windows.Threading.DispatcherPriority.Render));

        RestoreMaster();

        AddTimerCommand = new RelayCommand(AddTimer);
        AddSoundCommand = new RelayCommand(AddSound);
        RemoveSoundCommand = new RelayCommand(o => RemoveSound(o as SoundEntry));
        TestSoundCommand = new RelayCommand(o => TestSound(o as SoundEntry));
        StopAllCommand = new RelayCommand(StopAllTimers);
        DismissAlertsCommand = new RelayCommand(DismissAllAlerts);
        ToggleMuteCommand = new RelayCommand(ToggleMute);
    }

    public ObservableCollection<TimerRowViewModel> Timers { get; } = new();
    public ObservableCollection<SoundEntry> Sounds => _runtime.Library.Entries;

    public ICommand AddTimerCommand { get; }
    public ICommand AddSoundCommand { get; }
    public ICommand RemoveSoundCommand { get; }
    public ICommand TestSoundCommand { get; }
    public ICommand StopAllCommand { get; }
    public ICommand DismissAlertsCommand { get; }
    public ICommand ToggleMuteCommand { get; }

    public string BackendName => _sound.BackendName;

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public int MasterVolumePercent
    {
        get => (int)Math.Round(_runtime.MasterVolume * 100);
        set
        {
            double v = Math.Clamp(value / 100.0, 0, 1);
            if (Math.Abs(_runtime.MasterVolume - v) > 0.001)
            {
                _runtime.MasterVolume = v;
                OnPropertyChanged();
                SaveMaster();
            }
        }
    }

    public bool Muted
    {
        get => _runtime.Muted;
        set
        {
            if (_runtime.Muted != value)
            {
                _runtime.Muted = value;
                if (value) _sound.StopAll();
                OnPropertyChanged();
                OnPropertyChanged(nameof(MuteButtonText));
                SaveMaster();
            }
        }
    }

    public string MuteButtonText => _runtime.Muted ? "Reativar som" : "Silenciar";

    // ---- timers ----

    private void AddTimer()
    {
        var config = new TimerDefinition
        {
            Name = $"Timer {Timers.Count + 1}",
            SoundId = _runtime.Library.DefaultSoundId
        };
        var row = new TimerRowViewModel(_runtime, config);
        WireRow(row);
        Timers.Add(row);
        RebindAllHotkeys();
        Status = $"Timer adicionado: {config.Name}.";
        SaveTimers();
    }

    private void WireRow(TimerRowViewModel row)
    {
        row.RemoveRequested += OnRowRemoveRequested;
        row.Changed += SaveTimers;
        row.RebindRequested += RebindAllHotkeys;
        row.RunningChanged += OnRowRunningChanged;
    }

    private void UnwireRow(TimerRowViewModel row)
    {
        row.RemoveRequested -= OnRowRemoveRequested;
        row.Changed -= SaveTimers;
        row.RebindRequested -= RebindAllHotkeys;
        row.RunningChanged -= OnRowRunningChanged;
    }

    private void OnRowRemoveRequested(TimerRowViewModel row)
    {
        row.StopRow();
        row.Shutdown();
        UnwireRow(row);
        _runningRows.Remove(row);
        Timers.Remove(row);
        RebindAllHotkeys();
        Status = "Timer removido.";
        SaveTimers();
    }

    // ---- the single 25 ms tick loop (only while rows are running) ----

    private void OnRowRunningChanged(TimerRowViewModel row, bool running)
    {
        if (running)
        {
            if (_runningRows.Add(row) && _countdownSub is null)
                _countdownSub = _runtime.CountdownTicker.Subscribe(OnCountdownTick);
        }
        else
        {
            if (_runningRows.Remove(row) && _runningRows.Count == 0)
            {
                _countdownSub?.Dispose();
                _countdownSub = null;
            }
        }
    }

    private void OnCountdownTick()
    {
        // Snapshot: a row may transition to idle (and remove itself) during its own Tick.
        TimerRowViewModel[] snapshot = _runningRows.ToArray();
        foreach (TimerRowViewModel row in snapshot)
            row.Tick();
    }

    // ---- global hotkeys: gesture -> many rows (fan-out) ----

    private void RebindAllHotkeys()
    {
        _hotkeys.UnbindOwner(OwnerTimers);
        _gestureMap.Clear();

        foreach (TimerRowViewModel row in Timers)
        {
            HotkeyGesture g = row.Gesture;
            if (g.IsEmpty)
                continue;
            if (!_gestureMap.TryGetValue(g, out List<TimerRowViewModel>? list))
                _gestureMap[g] = list = new List<TimerRowViewModel>();
            list.Add(row);
        }

        foreach (KeyValuePair<HotkeyGesture, List<TimerRowViewModel>> pair in _gestureMap)
        {
            HotkeyGesture gesture = pair.Key;
            bool ok = _hotkeys.TryBind(
                OwnerTimers, gesture.ToString(), gesture,
                () => TriggerGesture(gesture),
                out HotkeyBinding? conflict);

            if (!ok && conflict is HotkeyBinding c)
                Status = $"Tecla {gesture} ja usada por {c.OwnerId}/{c.ActionId}.";
        }
    }

    private void TriggerGesture(HotkeyGesture gesture)
    {
        if (!_gestureMap.TryGetValue(gesture, out List<TimerRowViewModel>? rows))
            return;
        // Fan-out: one press starts/retriggers every row sharing this gesture.
        foreach (TimerRowViewModel row in rows)
            row.Trigger();
    }

    // ---- module-level actions (bound by AudioModule.RegisterHotkeys) ----

    public void StopAllTimers()
    {
        foreach (TimerRowViewModel row in Timers)
            row.StopRow();
        _sound.StopAll();
        Status = "Todos os timers parados.";
    }

    public void DismissAllAlerts()
    {
        foreach (TimerRowViewModel row in Timers)
            row.DismissAlert();
        _sound.StopAll();
        Status = "Alertas dispensados.";
    }

    public void ToggleMute() => Muted = !Muted;

    // ---- sounds ----

    private void AddSound()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Escolher som",
            Filter = "Audio (*.wav;*.mp3)|*.wav;*.mp3|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true
        };
        bool? picked = _services.ShellWindow is System.Windows.Window owner
            ? dlg.ShowDialog(owner)
            : dlg.ShowDialog();
        if (picked == true)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            _runtime.Library.Add(name, dlg.FileName);
            Status = $"Som adicionado: {name}.";
        }
    }

    private void RemoveSound(SoundEntry? entry)
    {
        if (entry is null)
            return;
        if (entry.BuiltIn)
        {
            Status = "Sons integrados nao podem ser removidos.";
            return;
        }
        _runtime.Library.Remove(entry);
        Status = "Som removido.";
    }

    private void TestSound(SoundEntry? entry)
    {
        if (entry is null)
            return;
        string path = _runtime.Library.ResolvePath(entry.Id);
        if (string.IsNullOrEmpty(path))
        {
            Status = "Arquivo de som indisponivel.";
            return;
        }
        // Preview at master volume even when muted, so the user can always hear it.
        float vol = (float)Math.Clamp(Math.Max(0.25, _runtime.MasterVolume), 0, 1);
        _sound.Enqueue(new SoundRequest(path, vol, Loop: false));
        Status = $"Tocando: {entry.Name}.";
    }

    // ---- persistence ----

    private void SaveTimers() => _services.Settings.Set(TimersKey, Timers.Select(t => t.Config).ToList());

    private void SaveMaster() => _services.Settings.Set(MasterKey,
        new MasterAudioState { Volume = _runtime.MasterVolume, Muted = _runtime.Muted });

    private void RestoreMaster()
    {
        MasterAudioState m = _services.Settings.Get(MasterKey, new MasterAudioState());
        _runtime.MasterVolume = Math.Clamp(m.Volume, 0, 1);
        _runtime.Muted = m.Muted;
    }

    /// <summary>Staggered restore behind the shell progress overlay (principle 6).</summary>
    public async Task RestoreAsync(IProgress<string> progress, CancellationToken ct)
    {
        List<TimerDefinition> configs = _services.Settings.Get(TimersKey, new List<TimerDefinition>());
        if (configs.Count == 0)
        {
            progress.Report("Nenhum timer salvo.");
            return;
        }

        int i = 0;
        foreach (TimerDefinition config in configs)
        {
            ct.ThrowIfCancellationRequested();
            var row = new TimerRowViewModel(_runtime, config);
            WireRow(row);
            Timers.Add(row);
            progress.Report($"Carregando timer {++i}...");
            await Task.Delay(50, ct); // inter-item stagger keeps the shell responsive
        }

        RebindAllHotkeys();
        Status = $"{Timers.Count} timers carregados.";
        progress.Report(Status);
    }

    /// <summary>App shutdown: silence audio, close overlays, unbind, flush.</summary>
    public void Shutdown()
    {
        _countdownSub?.Dispose();
        _countdownSub = null;

        foreach (TimerRowViewModel row in Timers)
            row.Shutdown();

        _hotkeys.UnbindOwner(OwnerTimers);
        _runtime.CountdownTicker.Shutdown();
        _runtime.BarTicker.Shutdown();
        _sound.Dispose();
        _services.Settings.Flush();
    }

    /// <summary>Small serializable holder for master audio state.</summary>
    private sealed class MasterAudioState
    {
        public double Volume { get; set; } = 1.0;
        public bool Muted { get; set; }
    }
}
