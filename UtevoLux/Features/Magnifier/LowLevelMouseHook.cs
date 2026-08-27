using System;
using System.Runtime.InteropServices;

namespace UtevoLux.Features.Magnifier;

/// <summary>
/// A dedicated WH_MOUSE_LL hook that is installed ONLY while the follow-lens hold is active and
/// SWALLOWS the vertical wheel (returns 1) so the game never scrolls while the user zooms the
/// lens. Every non-wheel message passes straight through via CallNextHookEx.
///
/// Like the keyboard LL hook, the callback runs on the thread that installed it — always the WPF
/// UI thread here — so the <see cref="Wheel"/> handler can touch UI state directly. Keep the proc
/// tiny: work done in a LL hook delays the whole input system.
/// </summary>
internal sealed class LowLevelMouseHook : IDisposable
{
    private readonly MagnifierNative.LowLevelMouseProc _proc; // kept alive; GC must not collect it
    private IntPtr _handle;
    private bool _disposed;

    /// <summary>Wheel notches: +N for wheel-up, -N for wheel-down (120 raw units per notch).</summary>
    public event Action<int>? Wheel;

    public LowLevelMouseHook()
    {
        _proc = HookProc;
    }

    public bool IsInstalled => _handle != IntPtr.Zero;

    public void Install()
    {
        if (_handle != IntPtr.Zero)
            return;
        IntPtr hMod = MagnifierNative.GetModuleHandleW(null);
        _handle = MagnifierNative.SetWindowsHookExW(MagnifierNative.WH_MOUSE_LL, _proc, hMod, 0);
    }

    public void Uninstall()
    {
        if (_handle != IntPtr.Zero)
        {
            MagnifierNative.UnhookWindowsHookEx(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == MagnifierNative.WM_MOUSEWHEEL)
        {
            var data = Marshal.PtrToStructure<MagnifierNative.MSLLHOOKSTRUCT>(lParam);
            int delta = (short)((data.mouseData >> 16) & 0xFFFF); // HIWORD, signed
            int notches = delta / 120;
            if (notches == 0 && delta != 0)
                notches = delta > 0 ? 1 : -1;
            if (notches != 0)
            {
                try { Wheel?.Invoke(notches); }
                catch { /* a handler fault must never wedge the input system */ }
            }
            return (IntPtr)1; // SWALLOW: the wheel is consumed for zoom, not forwarded to the game
        }

        return MagnifierNative.CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Uninstall();
    }
}
