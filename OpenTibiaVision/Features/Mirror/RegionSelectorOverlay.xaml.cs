using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using OpenTibiaVision.Models;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Features.Mirror;

/// <summary>
/// Full-viewport overlay for drag-selecting a crop region. Positioned in physical pixels
/// directly over the source window's CLIENT area (the game viewport) via SetWindowPos, so the
/// dragged rectangle maps 1:1 onto the source client. The result is returned as fractions of
/// the client area; the caller scales those to client-relative source pixels.
/// </summary>
public partial class RegionSelectorOverlay : Window
{
    private readonly RECT _clientBoundsPhysical;
    private Point _start;
    private bool _dragging;

    /// <summary>Set on confirm; null if cancelled.</summary>
    public RectFraction? Result { get; private set; }

    public RegionSelectorOverlay(RECT clientBoundsPhysical)
    {
        InitializeComponent();
        _clientBoundsPhysical = clientBoundsPhysical;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            _clientBoundsPhysical.Left,
            _clientBoundsPhysical.Top,
            _clientBoundsPhysical.Width,
            _clientBoundsPhysical.Height,
            NativeMethods.SWP_SHOWWINDOW);

        Activate();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        _start = e.GetPosition(RootCanvas);
        _dragging = true;

        Canvas.SetLeft(SelectionRect, _start.X);
        Canvas.SetTop(SelectionRect, _start.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;

        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
            return;

        Point current = e.GetPosition(RootCanvas);
        double x = Math.Min(_start.X, current.X);
        double y = Math.Min(_start.Y, current.Y);
        double w = Math.Abs(current.X - _start.X);
        double h = Math.Abs(current.Y - _start.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
            return;

        _dragging = false;
        ReleaseMouseCapture();
        Finish(e.GetPosition(RootCanvas));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Cancel();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        // Right-click cancels the selection, so the overlay can be dismissed without the
        // keyboard (Esc) and without a completed drag.
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            SelectionRect.Visibility = Visibility.Collapsed;
        }
        e.Handled = true;
        Cancel();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Cancel()
    {
        Result = null;
        DialogResult = false;
        Close();
    }

    private void Finish(Point end)
    {
        double canvasWidth = RootCanvas.ActualWidth;
        double canvasHeight = RootCanvas.ActualHeight;

        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            Result = null;
            DialogResult = false;
            Close();
            return;
        }

        double x = Math.Min(_start.X, end.X);
        double y = Math.Min(_start.Y, end.Y);
        double w = Math.Abs(end.X - _start.X);
        double h = Math.Abs(end.Y - _start.Y);

        var fraction = new RectFraction(
            Clamp01(x / canvasWidth),
            Clamp01(y / canvasHeight),
            Clamp01(w / canvasWidth),
            Clamp01(h / canvasHeight));

        Result = fraction.IsUsable ? fraction : null;
        DialogResult = Result is not null;
        Close();
    }

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));
}
