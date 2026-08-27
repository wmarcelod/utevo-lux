using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace UtevoLux.Core;

/// <summary>
/// Default <see cref="IHotkeyManager"/>. One non-consuming LL hook drives the rebindable
/// registry; two more isolated hooks drive the momentary magnifier and the F10 capture path.
/// All callbacks are marshalled to the UI dispatcher with a ~200 ms per-action throttle and a
/// single-flight guard, so a held key or a bounce never double-fires an action.
/// </summary>
public sealed class HotkeyManager : IHotkeyManager, IDisposable
{
    private const int ThrottleMs = 200;

    private readonly object _gate = new();
    private readonly Dispatcher _dispatcher;

    // Rebindable registry.
    private readonly LowLevelKeyboardHook _registryHook = new();
    private readonly Dictionary<HotkeyGesture, Registration> _byGesture = new();
    private readonly Dictionary<(string owner, string action), HotkeyGesture> _byAction = new();

    // Separate hooks.
    private readonly LowLevelKeyboardHook _momentaryHook = new();
    private readonly List<MomentaryReg> _momentary = new();
    private readonly LowLevelKeyboardHook _captureHook = new();
    private readonly List<CaptureReg> _captures = new();

    private bool _started;

    public HotkeyManager(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        _registryHook.KeyDown += OnRegistryKeyDown;
        _momentaryHook.KeyDown += OnMomentaryKeyDown;
        _momentaryHook.KeyUp += OnMomentaryKeyUp;
        _captureHook.KeyDown += OnCaptureKeyDown;
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        _registryHook.Install();
        _momentaryHook.Install();
        _captureHook.Install();
    }

    public void Stop()
    {
        _started = false;
        _registryHook.Uninstall();
        _momentaryHook.Uninstall();
        _captureHook.Uninstall();
    }

    // ---- Rebindable registry ----

    public bool TryBind(string ownerId, string actionId, HotkeyGesture gesture, Action callback,
        out HotkeyBinding? conflict, bool steal = false)
    {
        conflict = null;
        if (gesture.IsEmpty)
            return false;

        lock (_gate)
        {
            // Someone else holds this gesture?
            if (_byGesture.TryGetValue(gesture, out Registration? existing) && existing is not null &&
                (existing.OwnerId != ownerId || existing.ActionId != actionId))
            {
                conflict = new HotkeyBinding(existing.OwnerId, existing.ActionId, gesture);
                if (!steal)
                    return false;

                // Steal: drop the previous holder's gesture mapping.
                _byGesture.Remove(gesture);
                _byAction.Remove((existing.OwnerId, existing.ActionId));
            }

            // Move this action off any gesture it previously held.
            if (_byAction.TryGetValue((ownerId, actionId), out HotkeyGesture prev))
                _byGesture.Remove(prev);

            _byGesture[gesture] = new Registration(ownerId, actionId, callback);
            _byAction[(ownerId, actionId)] = gesture;
            return true;
        }
    }

    public void Unbind(string ownerId, string actionId)
    {
        lock (_gate)
        {
            if (_byAction.Remove((ownerId, actionId), out HotkeyGesture g))
                _byGesture.Remove(g);
        }
    }

    public void UnbindOwner(string ownerId)
    {
        lock (_gate)
        {
            var toRemove = new List<HotkeyGesture>();
            foreach (var kv in _byGesture)
                if (kv.Value.OwnerId == ownerId)
                    toRemove.Add(kv.Key);
            foreach (HotkeyGesture g in toRemove)
                _byGesture.Remove(g);

            var actions = new List<(string, string)>();
            foreach (var kv in _byAction)
                if (kv.Key.owner == ownerId)
                    actions.Add(kv.Key);
            foreach (var a in actions)
                _byAction.Remove(a);
        }
    }

    public HotkeyBinding? FindOwner(HotkeyGesture gesture)
    {
        lock (_gate)
        {
            if (_byGesture.TryGetValue(gesture, out Registration? r) && r is not null)
                return new HotkeyBinding(r.OwnerId, r.ActionId, gesture);
        }
        return null;
    }

    private void OnRegistryKeyDown(Key key)
    {
        if (IsModifierKey(key))
            return;

        var gesture = new HotkeyGesture(key, LowLevelKeyboardHook.CurrentModifiers());

        Registration? reg;
        lock (_gate)
        {
            if (!_byGesture.TryGetValue(gesture, out reg))
                return;
        }

        if (reg is null || !reg.PassThrottle())
            return;

        Dispatch(reg.Callback);
    }

    // ---- Momentary (magnifier) ----

    public IDisposable BindMomentary(string ownerId, HotkeyGesture gesture, Action onDown, Action onUp)
    {
        var reg = new MomentaryReg(ownerId, gesture, onDown, onUp);
        lock (_gate)
            _momentary.Add(reg);
        return new Remover(() =>
        {
            lock (_gate)
                _momentary.Remove(reg);
        });
    }

    private void OnMomentaryKeyDown(Key key)
    {
        List<MomentaryReg>? fire = null;
        ModifierKeys mods = LowLevelKeyboardHook.CurrentModifiers();
        lock (_gate)
        {
            foreach (MomentaryReg r in _momentary)
            {
                if (r.Gesture.Key == key && r.Gesture.Modifiers == mods && !r.IsDown)
                {
                    r.IsDown = true;
                    (fire ??= new List<MomentaryReg>()).Add(r);
                }
            }
        }
        if (fire is not null)
            foreach (MomentaryReg r in fire)
                Dispatch(r.OnDown);
    }

    private void OnMomentaryKeyUp(Key key)
    {
        List<MomentaryReg>? fire = null;
        lock (_gate)
        {
            foreach (MomentaryReg r in _momentary)
            {
                if (r.Gesture.Key == key && r.IsDown)
                {
                    r.IsDown = false;
                    (fire ??= new List<MomentaryReg>()).Add(r);
                }
            }
        }
        if (fire is not null)
            foreach (MomentaryReg r in fire)
                Dispatch(r.OnUp);
    }

    // ---- Capture (F10) ----

    public IDisposable BindCapture(string ownerId, Action onCapture)
    {
        var reg = new CaptureReg(ownerId, onCapture);
        lock (_gate)
            _captures.Add(reg);
        return new Remover(() =>
        {
            lock (_gate)
                _captures.Remove(reg);
        });
    }

    private void OnCaptureKeyDown(Key key)
    {
        if (key != Key.F10)
            return;

        CaptureReg[] snapshot;
        lock (_gate)
            snapshot = _captures.ToArray();

        foreach (CaptureReg r in snapshot)
        {
            if (r.PassThrottle())
                Dispatch(r.OnCapture);
        }
    }

    // ---- helpers ----

    private void Dispatch(Action action)
    {
        // Marshal off the hook path so heavy handlers never stall the input system.
        _dispatcher.BeginInvoke(action, DispatcherPriority.Input);
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.System or
        Key.LWin or Key.RWin;

    public void Dispose()
    {
        Stop();
        _registryHook.Dispose();
        _momentaryHook.Dispose();
        _captureHook.Dispose();
    }

    // ---- registration records ----

    private sealed class Registration
    {
        public readonly string OwnerId;
        public readonly string ActionId;
        public readonly Action Callback;
        private long _lastTicks;
        private int _inFlight;

        public Registration(string ownerId, string actionId, Action callback)
        {
            OwnerId = ownerId;
            ActionId = actionId;
            Callback = Wrap(callback);
        }

        private Action Wrap(Action inner) => () =>
        {
            try { inner(); }
            finally { Interlocked.Exchange(ref _inFlight, 0); }
        };

        /// <summary>Single-flight + 200 ms throttle. Returns true if the action may fire now.</summary>
        public bool PassThrottle()
        {
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastTicks);
            if (now - last < ThrottleMs)
                return false;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
                return false;
            Interlocked.Exchange(ref _lastTicks, now);
            return true;
        }
    }

    private sealed class MomentaryReg
    {
        public readonly string OwnerId;
        public readonly HotkeyGesture Gesture;
        public readonly Action OnDown;
        public readonly Action OnUp;
        public bool IsDown;

        public MomentaryReg(string ownerId, HotkeyGesture gesture, Action onDown, Action onUp)
        {
            OwnerId = ownerId;
            Gesture = gesture;
            OnDown = onDown;
            OnUp = onUp;
        }
    }

    private sealed class CaptureReg
    {
        public readonly string OwnerId;
        public readonly Action OnCapture;
        private long _lastTicks;

        public CaptureReg(string ownerId, Action onCapture)
        {
            OwnerId = ownerId;
            OnCapture = onCapture;
        }

        public bool PassThrottle()
        {
            long now = Environment.TickCount64;
            if (now - _lastTicks < ThrottleMs)
                return false;
            _lastTicks = now;
            return true;
        }
    }

    private sealed class Remover : IDisposable
    {
        private Action? _dispose;
        public Remover(Action dispose) => _dispose = dispose;
        public void Dispose()
        {
            _dispose?.Invoke();
            _dispose = null;
        }
    }
}
