using System;

namespace UtevoLux.Core;

/// <summary>
/// App-wide global hotkeys over a single non-consuming low-level keyboard hook, plus separate
/// hooks for the momentary magnifier and the F10 capture path.
///
/// Ownership: every binding is tagged with an ownerId (the feature module's Id). A binding
/// carries an actionId unique within its owner. An app-wide conflict registry maps each
/// gesture to its current owner+action, so a rebind can name who already holds a combo before
/// stealing it.
/// </summary>
public interface IHotkeyManager
{
    /// <summary>
    /// Bind <paramref name="gesture"/> to <paramref name="callback"/> for (owner, action).
    /// Returns true if bound. On conflict returns false and sets <paramref name="conflict"/>
    /// to the owner+action that already holds the gesture. Pass <paramref name="steal"/>=true
    /// to forcibly rebind (the previous owner's binding for that gesture is removed).
    /// Re-binding the same (owner, action) to a new gesture always succeeds and moves it.
    /// </summary>
    bool TryBind(string ownerId, string actionId, HotkeyGesture gesture, Action callback,
        out HotkeyBinding? conflict, bool steal = false);

    /// <summary>Remove one binding.</summary>
    void Unbind(string ownerId, string actionId);

    /// <summary>Remove every binding owned by <paramref name="ownerId"/>.</summary>
    void UnbindOwner(string ownerId);

    /// <summary>Who currently holds <paramref name="gesture"/>, if anyone.</summary>
    HotkeyBinding? FindOwner(HotkeyGesture gesture);

    /// <summary>
    /// Momentary (hold-to-activate) binding on the SEPARATE magnifier hook: onDown fires when
    /// the gesture goes down, onUp when the key releases. Dispose the returned handle to remove.
    /// </summary>
    IDisposable BindMomentary(string ownerId, HotkeyGesture gesture, Action onDown, Action onUp);

    /// <summary>
    /// F10 capture binding on the SEPARATE capture hook. F10 is a system key that a normal
    /// WPF InputBinding cannot reliably see; the dedicated LL hook can. Dispose to remove.
    /// </summary>
    IDisposable BindCapture(string ownerId, Action onCapture);

    void Start();
    void Stop();
}

/// <summary>A resolved binding: who owns it and under what action id.</summary>
public readonly record struct HotkeyBinding(string OwnerId, string ActionId, HotkeyGesture Gesture);
