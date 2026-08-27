using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Features.Mirror;

/// <summary>
/// Transparent, full-client input surface for the loupe crop. It captures the cursor over the
/// source's CLIENT area and reports the cursor in SOURCE client physical pixels (via GetCursorPos
/// + ScreenToClient — DPI-clean, independent of this overlay's own scaling). It previews the
/// fixed-box crop footprint (dashed rectangle) at 1:1, resizes it with the wheel, commits on
/// left click and cancels on Esc.
/// </summary>
public partial class LoupePickOverlay : Window
{
    private const int BoxStep = 16;   // source px per wheel notch
    private const int MinBox = 48;

    private readonly IntPtr _sourceHwnd;
    private readonly RECT _clientBoundsPhysical;
    private readonly IDpiService _dpi;

    private int _boxW;
    private int _boxH;
    private int _lastX;
    private int _lastY;
    private bool _haveCursor;
    private IntPtr _selfHwnd;

    /// <summary>Cursor moved: source-client physical px (used to follow the loupe).</summary>
    public event Action<int, int>? PointerMoved;

    /// <summary>Left click: commit the crop rectangle (source-client physical px).</summary>
    public event Action<RECT>? PickedCrop;

    public event Action? Cancelled;

    public int BoxWidth => _boxW;
    public int BoxHeight => _boxH;

    public LoupePickOverlay(IAppServices services, IntPtr sourceHwnd, RECT clientBoundsPhysical,
        int initialBoxW, int initialBoxH)
    {
        InitializeComponent();
        _sourceHwnd = sourceHwnd;
        _clientBoundsPhysical = clientBoundsPhysical;
        _dpi = services.Dpi;
        _boxW = Math.Max(MinBox, initialBoxW);
        _boxH = Math.Max(MinBox, initialBoxH);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _selfHwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            _selfHwnd, NativeMethods.HWND_TOPMOST,
            _clientBoundsPhysical.Left, _clientBoundsPhysical.Top,
            _clientBoundsPhysical.Width, _clientBoundsPhysical.Height,
            NativeMethods.SWP_SHOWWINDOW);

        Activate();
    }

    private bool TryCursorInSourceClient(out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!MirrorInterop.GetCursorPos(out NativeMethods.POINT p))
            return false;
        if (!MirrorInterop.ScreenToClient(_sourceHwnd, ref p))
            return false;
        x = p.X;
        y = p.Y;
        return true;
    }

    private RECT CurrentCrop(int cx, int cy)
        => MirrorCoordinateMapper.CenteredBox(cx, cy, _boxW, _boxH,
            _clientBoundsPhysical.Width, _clientBoundsPhysical.Height);

    private void RenderFootprint(int cx, int cy)
    {
        RECT crop = CurrentCrop(cx, cy);

        // Overlay client (0,0) physical == source client (0,0) physical (we positioned it there),
        // so source-client physical maps to overlay DIP by dividing out this window's scale.
        double scale = _selfHwnd == IntPtr.Zero ? 1.0 : _dpi.GetScaleForWindow(_selfHwnd);

        Canvas.SetLeft(CropBox, _dpi.ToDip(crop.Left, scale));
        Canvas.SetTop(CropBox, _dpi.ToDip(crop.Top, scale));
        CropBox.Width = _dpi.ToDip(crop.Width, scale);
        CropBox.Height = _dpi.ToDip(crop.Height, scale);
        CropBox.Visibility = Visibility.Visible;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!TryCursorInSourceClient(out int x, out int y))
            return;

        _lastX = x;
        _lastY = y;
        _haveCursor = true;

        RenderFootprint(x, y);
        PointerMoved?.Invoke(x, y);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!TryCursorInSourceClient(out int x, out int y))
            return;

        PickedCrop?.Invoke(CurrentCrop(x, y));
        DialogResult = true;
        Close();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        int delta = e.Delta > 0 ? BoxStep : -BoxStep;
        // Preserve aspect while resizing the box.
        double aspect = _boxH == 0 ? 1.0 : _boxW / (double)_boxH;
        _boxW = Math.Max(MinBox, _boxW + delta);
        _boxH = Math.Max(MinBox, (int)Math.Round(_boxW / aspect));

        if (_haveCursor)
            RenderFootprint(_lastX, _lastY);

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Cancelled?.Invoke();
            DialogResult = false;
            Close();
        }
    }
}
