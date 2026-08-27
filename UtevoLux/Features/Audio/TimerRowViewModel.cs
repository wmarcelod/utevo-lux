using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Input;
using UtevoLux.Core;
using UtevoLux.ViewModels;

namespace UtevoLux.Features.Audio;

/// <summary>
/// One timer row. A single hotkey press (dispatched by <see cref="AudioPageViewModel"/>) starts
/// or RETRIGGERS every duration at once — the multi-timer fan-out. Each duration becomes an
/// <see cref="ActiveCountdown"/> storing an absolute EndTime on the monotonic
/// <see cref="Environment.TickCount64"/> clock, so the ONE shared 25 ms ticker only reads elapsed
/// time and the display can never drift (principle 4).
///
/// On each duration's expiry the row: enqueues the alert sound (respecting master mute/volume),
/// shows the click-through banner if enabled, and lets the bar overlay flash. The bar overlay
/// tracks the longest duration via a provider delegate and rides the shared 50 ms ticker.
/// </summary>
public sealed class TimerRowViewModel : ViewModelBase
{
    private readonly AudioRuntime _rt;
    private readonly TimerDefinition _config;
    private readonly List<ActiveCountdown> _active = new();

    private AlertBannerWindow? _banner;
    private CountdownBarWindow? _bar;
    private ActiveCountdown? _barTracked;
    private bool _running;
    private string _remainingText = "";

    public event Action<TimerRowViewModel>? RemoveRequested;
    /// <summary>Row's persisted state changed; owner should Save().</summary>
    public event Action? Changed;
    /// <summary>Row's hotkey changed; owner should rebuild global bindings.</summary>
    public event Action? RebindRequested;
    /// <summary>Running-state edge (true=just started, false=just went idle).</summary>
    public event Action<TimerRowViewModel, bool>? RunningChanged;

    public TimerRowViewModel(AudioRuntime runtime, TimerDefinition config)
    {
        _rt = runtime;
        _config = config;

        TriggerCommand = new RelayCommand(Trigger);
        StopCommand = new RelayCommand(StopRow);
        RemoveCommand = new RelayCommand(() => RemoveRequested?.Invoke(this));
        SetHotkeyCommand = new RelayCommand(SetHotkey);
        ClearHotkeyCommand = new RelayCommand(ClearHotkey);
        PlaceAlertCommand = new RelayCommand(PlaceAlert);
        PlaceBarCommand = new RelayCommand(PlaceBar);
    }

    public TimerDefinition Config => _config;
    public HotkeyGesture Gesture => _config.Gesture;

    public ICommand TriggerCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand SetHotkeyCommand { get; }
    public ICommand ClearHotkeyCommand { get; }
    public ICommand PlaceAlertCommand { get; }
    public ICommand PlaceBarCommand { get; }

    // ---- bindable surface ----

    public string Name
    {
        get => _config.Name;
        set { if (_config.Name != value) { _config.Name = value; OnPropertyChanged(); Changed?.Invoke(); } }
    }

    /// <summary>Durations as seconds, comma/space separated (e.g. "5, 30, 60").</summary>
    public string DurationsText
    {
        get => string.Join(", ", _config.DurationsMs.Select(ms => (ms / 1000.0)
                    .ToString("0.###", CultureInfo.InvariantCulture)));
        set
        {
            List<int> parsed = ParseDurations(value);
            if (parsed.Count == 0)
                parsed.Add(30000);
            _config.DurationsMs = parsed;
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    public string HotkeyText => _config.Gesture.IsEmpty ? "(sem tecla)" : _config.Gesture.ToString();

    public IEnumerable<SoundEntry> Sounds => _rt.Library.Entries;

    public SoundEntry? SelectedSound
    {
        get => _rt.Library.Find(_config.SoundId) ?? _rt.Library.Entries.FirstOrDefault();
        set
        {
            string id = value?.Id ?? "";
            if (_config.SoundId != id) { _config.SoundId = id; OnPropertyChanged(); Changed?.Invoke(); }
        }
    }

    public bool LoopSound
    {
        get => _config.LoopSound;
        set { if (_config.LoopSound != value) { _config.LoopSound = value; OnPropertyChanged(); Changed?.Invoke(); } }
    }

    public int VolumePercent
    {
        get => (int)Math.Round(_config.Volume * 100);
        set
        {
            double v = Math.Clamp(value / 100.0, 0, 1);
            if (Math.Abs(_config.Volume - v) > 0.001) { _config.Volume = v; OnPropertyChanged(); Changed?.Invoke(); }
        }
    }

    public bool AlertEnabled
    {
        get => _config.Alert.Enabled;
        set { if (_config.Alert.Enabled != value) { _config.Alert.Enabled = value; OnPropertyChanged(); Changed?.Invoke(); } }
    }

    public bool AlertStayUntilHotkey
    {
        get => _config.Alert.Mode == AlertMode.StayUntilHotkey;
        set
        {
            AlertMode mode = value ? AlertMode.StayUntilHotkey : AlertMode.Fade;
            if (_config.Alert.Mode != mode) { _config.Alert.Mode = mode; OnPropertyChanged(); Changed?.Invoke(); }
        }
    }

    public bool BarEnabled
    {
        get => _config.Bar.Enabled;
        set
        {
            if (_config.Bar.Enabled != value)
            {
                _config.Bar.Enabled = value;
                if (!value) _bar?.Hide();
                OnPropertyChanged();
                Changed?.Invoke();
            }
        }
    }

    public bool IsRunning
    {
        get => _running;
        private set => SetProperty(ref _running, value);
    }

    public string RemainingText
    {
        get => _remainingText;
        private set => SetProperty(ref _remainingText, value);
    }

    // ---- trigger / retrigger ----

    /// <summary>Start or retrigger all durations at once (fan-out) with fresh absolute EndTimes.</summary>
    public void Trigger()
    {
        if (!_config.Enabled)
            return;

        long now = Environment.TickCount64;
        _active.Clear();
        foreach (int durationMs in _config.DurationsMs)
        {
            _active.Add(new ActiveCountdown
            {
                DurationMs = Math.Max(1, durationMs),
                EndTimeTicks = now + Math.Max(1, durationMs),
                Fired = false
            });
        }

        // The longest duration drives the bar.
        _barTracked = _active.OrderByDescending(a => a.DurationMs).FirstOrDefault();

        // Fresh cycle: stop any looping sound left from a previous run and dismiss a stuck banner.
        _rt.Sound.StopAll();
        _banner?.Dismiss();

        if (_config.Bar.Enabled)
            EnsureBar().Show(_config.Bar, GetBarState);

        UpdateRemaining();

        if (!IsRunning)
        {
            IsRunning = true;
            RunningChanged?.Invoke(this, true);
        }
    }

    /// <summary>Called every 25 ms by the owner while this row is running.</summary>
    public void Tick()
    {
        long now = Environment.TickCount64;
        bool anyRunning = false;

        foreach (ActiveCountdown ac in _active)
        {
            if (ac.Fired)
                continue;
            if (now >= ac.EndTimeTicks)
            {
                ac.Fired = true;
                FireExpiry();
            }
            else
            {
                anyRunning = true;
            }
        }

        UpdateRemaining();

        if (!anyRunning && IsRunning)
        {
            IsRunning = false;
            RunningChanged?.Invoke(this, false);
            // Bar (if enabled) keeps flashing via its own 50 ms ticker until Stop/retrigger.
        }
    }

    /// <summary>Reset this row: clear countdowns, hide bar, dismiss banner, stop looping sound.</summary>
    public void StopRow()
    {
        _active.Clear();
        _barTracked = null;
        _bar?.Hide();
        _banner?.Dismiss();
        _rt.Sound.StopAll();
        RemainingText = "";

        if (IsRunning)
        {
            IsRunning = false;
            RunningChanged?.Invoke(this, false);
        }
    }

    /// <summary>Dismiss a StayUntilHotkey banner and stop looping sound (module dismiss hotkey).</summary>
    public void DismissAlert()
    {
        _banner?.Dismiss();
    }

    private void FireExpiry()
    {
        // Sound.
        string path = _rt.Library.ResolvePath(_config.SoundId);
        if (string.IsNullOrEmpty(path))
            path = _rt.Library.ResolvePath(_rt.Library.DefaultSoundId);

        float volume = _rt.EffectiveVolume(_config.Volume);
        if (!string.IsNullOrEmpty(path) && volume > 0f)
            _rt.Sound.Enqueue(new SoundRequest(path, volume, _config.LoopSound));

        // Visual alert banner.
        if (_config.Alert.Enabled)
        {
            string text = string.IsNullOrWhiteSpace(_config.Alert.Text) ? _config.Name : _config.Alert.Text;
            EnsureBanner().Show(_config.Alert, text);
        }
        // Bar flash is driven by GetBarState().expired via the 50 ms ticker.
    }

    /// <summary>Provider for the bar overlay: (remaining fraction 0..1, expired?).</summary>
    private (double fraction, bool expired) GetBarState()
    {
        ActiveCountdown? ac = _barTracked;
        if (ac is null)
            return (0.0, true);

        long remaining = ac.EndTimeTicks - Environment.TickCount64;
        if (remaining <= 0)
            return (0.0, true);

        double frac = remaining / (double)ac.DurationMs;
        return (Math.Clamp(frac, 0.0, 1.0), false);
    }

    private void UpdateRemaining()
    {
        if (_active.Count == 0)
        {
            RemainingText = "";
            return;
        }

        long now = Environment.TickCount64;
        var sb = new StringBuilder();
        foreach (ActiveCountdown ac in _active.OrderBy(a => a.EndTimeTicks))
        {
            double secs = Math.Max(0, (ac.EndTimeTicks - now) / 1000.0);
            if (sb.Length > 0) sb.Append("  ·  ");
            sb.Append(secs.ToString("0.0", CultureInfo.InvariantCulture)).Append('s');
        }
        RemainingText = sb.ToString();
    }

    // ---- hotkey editing ----

    private void SetHotkey()
    {
        var dlg = new HotkeyCaptureWindow { Owner = _rt.Services.ShellWindow };
        if (dlg.ShowDialog() == true && dlg.Result is HotkeyGesture g)
        {
            _config.Gesture = g;
            OnPropertyChanged(nameof(HotkeyText));
            RebindRequested?.Invoke();
            Changed?.Invoke();
        }
    }

    private void ClearHotkey()
    {
        _config.Gesture = HotkeyGesture.None;
        OnPropertyChanged(nameof(HotkeyText));
        RebindRequested?.Invoke();
        Changed?.Invoke();
    }

    // ---- overlay placement (drag-to-place twins) ----

    private void PlaceAlert()
    {
        string text = string.IsNullOrWhiteSpace(_config.Alert.Text) ? _config.Name : _config.Alert.Text;
        AlertPlacerWindow placer = AlertPlacerWindow.ForAlert(_config.Alert, text);
        placer.Owner = _rt.Services.ShellWindow;
        if (placer.ShowDialog() == true)
        {
            _config.Alert.PosX = placer.ResultX;
            _config.Alert.PosY = placer.ResultY;
            Changed?.Invoke();
            _rt.Services.ShowToast($"Alerta '{_config.Name}' posicionado.");
        }
    }

    private void PlaceBar()
    {
        AlertPlacerWindow placer = AlertPlacerWindow.ForBar(_config.Bar);
        placer.Owner = _rt.Services.ShellWindow;
        if (placer.ShowDialog() == true)
        {
            _config.Bar.PosX = placer.ResultX;
            _config.Bar.PosY = placer.ResultY;
            Changed?.Invoke();
            if (_config.Bar.Enabled)
                EnsureBar().Show(_config.Bar, GetBarState);
            _rt.Services.ShowToast($"Barra '{_config.Name}' posicionada.");
        }
    }

    // ---- overlay lifecycle (created once, Show/Hidden) ----

    private AlertBannerWindow EnsureBanner() => _banner ??= new AlertBannerWindow(_rt.Services);
    private CountdownBarWindow EnsureBar() => _bar ??= new CountdownBarWindow(_rt.Services, _rt.BarTicker);

    /// <summary>App shutdown: close overlay windows for real.</summary>
    public void Shutdown()
    {
        _banner?.Shutdown();
        _bar?.Shutdown();
    }

    // ---- parsing ----

    private static List<int> ParseDurations(string text)
    {
        var list = new List<int>();
        if (string.IsNullOrWhiteSpace(text))
            return list;

        foreach (string token in text.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double secs)
                && secs > 0)
            {
                list.Add((int)Math.Round(secs * 1000));
            }
        }
        return list;
    }

    /// <summary>One armed countdown instance (one per duration in the fan-out).</summary>
    private sealed class ActiveCountdown
    {
        public int DurationMs;
        public long EndTimeTicks;
        public bool Fired;
    }
}
