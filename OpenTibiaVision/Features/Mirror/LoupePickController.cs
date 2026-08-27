using System;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// Runs one crop-loupe session over a source window: a transparent input overlay, an opaque ~4x
/// DWM loupe that follows the cursor, and a crosshair reticle above the loupe. On commit it hands
/// back the chosen crop (source client physical px) and the final fixed-box size. All three
/// windows are transient to the pick (created here, closed on finish) — a modal tool, not a
/// navigable page, so create/close is correct here.
/// </summary>
public sealed class LoupePickController
{
    private const int LoupePhysWidth = 360;   // loupe window size (physical px)
    private const int LoupePhysHeight = 288;
    private const int Magnification = 4;       // ~4x: loupe view == window / 4
    private const int CursorOffset = 28;       // loupe offset from the cursor (physical px)

    private readonly IAppServices _services;
    private readonly IntPtr _sourceHwnd;
    private readonly RECT _clientBounds;

    private LoupePickOverlay? _overlay;
    private LoupeWindow? _loupe;
    private LoupeReticle? _reticle;

    public LoupePickController(IAppServices services, IntPtr sourceHwnd, RECT clientBoundsPhysical)
    {
        _services = services;
        _sourceHwnd = sourceHwnd;
        _clientBounds = clientBoundsPhysical;
    }

    /// <summary>
    /// Show the loupe and run the pick. <paramref name="onPicked"/> receives the crop (source
    /// client physical px) and the final box size; <paramref name="onCancelled"/> fires on Esc.
    /// </summary>
    public void Pick(int initialBoxW, int initialBoxH,
        Action<RECT, int, int> onPicked, Action? onCancelled = null)
    {
        _overlay = new LoupePickOverlay(_services, _sourceHwnd, _clientBounds, initialBoxW, initialBoxH)
        {
            Owner = _services.ShellWindow
        };
        _loupe = new LoupeWindow(_services, _sourceHwnd);
        _reticle = new LoupeReticle(_services);

        _overlay.PointerMoved += OnPointerMoved;
        _overlay.PickedCrop += crop =>
        {
            int bw = _overlay!.BoxWidth;
            int bh = _overlay.BoxHeight;
            Teardown();
            onPicked(crop, bw, bh);
        };
        _overlay.Cancelled += () =>
        {
            Teardown();
            onCancelled?.Invoke();
        };
        _overlay.Closed += (_, _) => Teardown(); // safety net if the window is force-closed

        // Park the loupe + reticle off-screen so they don't flash at the origin before the first
        // cursor move positions them.
        _loupe.Left = _loupe.Top = -10000;
        _reticle.Left = _reticle.Top = -10000;

        // Loupe + reticle first (passive, click-through), then the overlay owns the input.
        _loupe.Show();
        _reticle.Show();
        _overlay.ShowDialog();
    }

    private void OnPointerMoved(int clientX, int clientY)
    {
        if (_loupe is null || _reticle is null)
            return;

        int viewW = LoupePhysWidth / Magnification;
        int viewH = LoupePhysHeight / Magnification;
        RECT sourceBox = MirrorCoordinateMapper.CenteredBox(
            clientX, clientY, viewW, viewH, _clientBounds.Width, _clientBounds.Height);

        // Cursor position in screen physical px (overlay client origin == source client origin).
        int screenX = _clientBounds.Left + clientX;
        int screenY = _clientBounds.Top + clientY;
        RECT loupeRect = PlaceNear(screenX, screenY);

        _loupe.Update(loupeRect, sourceBox);
        _reticle.MoveTo(loupeRect); // last => stays above the loupe in the topmost band
    }

    /// <summary>Position the loupe near the cursor, flipping away from the client's edges.</summary>
    private RECT PlaceNear(int screenX, int screenY)
    {
        int left = screenX + CursorOffset;
        int top = screenY + CursorOffset;

        int clientRight = _clientBounds.Right;
        int clientBottom = _clientBounds.Bottom;

        if (left + LoupePhysWidth > clientRight)
            left = screenX - CursorOffset - LoupePhysWidth;
        if (top + LoupePhysHeight > clientBottom)
            top = screenY - CursorOffset - LoupePhysHeight;

        // Keep it on-client if flipping overshot the top/left.
        if (left < _clientBounds.Left) left = _clientBounds.Left;
        if (top < _clientBounds.Top) top = _clientBounds.Top;

        return new RECT(left, top, left + LoupePhysWidth, top + LoupePhysHeight);
    }

    private void Teardown()
    {
        if (_overlay is not null)
            _overlay.PointerMoved -= OnPointerMoved;

        _loupe?.Close();
        _reticle?.Close();
        _loupe = null;
        _reticle = null;
        _overlay = null;
    }
}
