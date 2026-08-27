using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using UtevoLux.Services;

namespace UtevoLux.Core;

/// <summary>
/// One WH_KEYBOARD_LL global keyboard hook. NON-CONSUMING by contract: the proc always calls
/// CallNextHookEx, so keystrokes still reach the game. Raises <see cref="KeyDown"/> /
/// <see cref="KeyUp"/> on the thread that installed it (the UI thread, whose message pump
/// delivers LL-hook callbacks). Keep handlers tiny — work done here delays the input system.
///
/// HotkeyManager instantiates THREE of these on purpose: the rebindable registry, the
/// momentary magnifier, and the F10 capture path each get their own isolated hook.
/// </summary>
internal sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly NativeMethods.LowLevelKeyboardProc _proc; // kept alive; GC must not collect it
    private IntPtr _handle;
    private bool _disposed;

    public event Action<Key>? KeyDown;
    public event Action<Key>? KeyUp;

    public LowLevelKeyboardHook()
    {
        _proc = HookProc;
    }

    public bool IsInstalled => _handle != IntPtr.Zero;

    public void Install()
    {
        if (_handle != IntPtr.Zero)
            return;

        // WH_KEYBOARD_LL is a global hook; hMod may be the module handle of the current
        // process (any valid module works for a LL hook).
        IntPtr hMod = NativeMethods.GetModuleHandleW(null);
        _handle = NativeMethods.SetWindowsHookExW(NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
    }

    public void Uninstall()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            Key key = KeyInterop.KeyFromVirtualKey((int)data.vkCode);

            if (msg == NativeMethods.WM_KEYDOWN || msg == NativeMethods.WM_SYSKEYDOWN)
                KeyDown?.Invoke(key);
            else if (msg == NativeMethods.WM_KEYUP || msg == NativeMethods.WM_SYSKEYUP)
                KeyUp?.Invoke(key);
        }

        // Never consume: pass the event on so the game receives it unchanged.
        return NativeMethods.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    /// <summary>Live modifier state, read from the async key state (works from a global hook).</summary>
    public static ModifierKeys CurrentModifiers()
    {
        ModifierKeys m = ModifierKeys.None;
        if (IsDown(NativeMethods.VK_CONTROL)) m |= ModifierKeys.Control;
        if (IsDown(NativeMethods.VK_SHIFT)) m |= ModifierKeys.Shift;
        if (IsDown(NativeMethods.VK_MENU)) m |= ModifierKeys.Alt;
        if (IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN)) m |= ModifierKeys.Windows;
        return m;
    }

    private static bool IsDown(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Uninstall();
    }
}
