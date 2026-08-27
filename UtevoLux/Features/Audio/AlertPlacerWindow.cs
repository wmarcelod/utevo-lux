using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using UtevoLux.Services;

namespace UtevoLux.Features.Audio;

/// <summary>
/// The NON-click-through, drag-to-place twin used to position any overlay (the alert banner or a
/// countdown bar). It shows a live preview the user drags with the mouse; Enter / double-click
/// confirms (writing the window's top-left back in PHYSICAL px), Esc cancels. Shown modally via
/// <see cref="ShowDialog"/>. This is the counterpart to the click-through runtime overlays
/// (<see cref="AlertBannerWindow"/>, <see cref="CountdownBarWindow"/>), which cannot be dragged
/// because they are transparent to the mouse.
/// </summary>
public sealed class AlertPlacerWindow : Window
{
    /// <summary>Confirmed top-left in PHYSICAL screen pixels (valid only when ShowDialog == true).</summary>
    public int ResultX { get; private set; }
    public int ResultY { get; private set; }

    public AlertPlacerWindow(FrameworkElement preview, string hintText)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "Utevo Lux - Posicionar";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var hint = new TextBlock
        {
            Text = hintText,
            Foreground = AlertVisual.Brush("#FFB9C4D4", "#FFB9C4D4"),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        Content = new StackPanel { Children = { preview, hint } };
    }

    /// <summary>Convenience factory for the alert banner preview.</summary>
    public static AlertPlacerWindow ForAlert(AlertConfig cfg, string text)
    {
        Border banner = AlertVisual.Build(cfg, text, out _);
        return new AlertPlacerWindow(banner,
            "Arraste para posicionar  -  Enter confirma  -  Esc cancela");
    }

    /// <summary>Convenience factory for the countdown-bar preview (approx. size in DIP).</summary>
    public static AlertPlacerWindow ForBar(BarConfig cfg)
    {
        var preview = new Border
        {
            Width = Math.Max(8, cfg.Width),
            Height = Math.Max(6, cfg.Height),
            CornerRadius = new CornerRadius(4),
            Background = AlertVisual.Brush(cfg.TrackHex, "#66000000"),
            Child = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(8, cfg.Width) * 0.6,
                CornerRadius = new CornerRadius(4),
                Background = AlertVisual.Brush(cfg.FillHex, "#FF4CC2FF")
            }
        };
        return new AlertPlacerWindow(preview,
            "Arraste a barra para posicionar  -  Enter confirma  -  Esc cancela");
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2)
        {
            Confirm();
            return;
        }
        try { DragMove(); }
        catch (InvalidOperationException) { /* button already released */ }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter)
            Confirm();
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private void Confirm()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out RECT r))
        {
            ResultX = r.Left;
            ResultY = r.Top;
        }
        DialogResult = true;
        Close();
    }
}
