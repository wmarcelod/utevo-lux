using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using UtevoLux.Services;

namespace UtevoLux.UI;

/// <summary>
/// A single reused, click-through, no-activate toast window (optimization principle 3: never
/// create/destroy — Show/Hide the one instance). It floats top-center over everything, never
/// takes focus, and auto-hides after a few seconds via one shared dispatcher timer.
/// </summary>
public sealed class Toast
{
    private static Toast? _instance;
    public static Toast Instance => _instance ??= new Toast();

    private readonly Window _window;
    private readonly TextBlock _text;
    private readonly DispatcherTimer _hideTimer;
    private bool _chromeApplied;

    private Toast()
    {
        _text = new TextBlock
        {
            Foreground = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9"),
            FontFamily = ThemeAccess.Font("Font.App", "Segoe UI"),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };

        var root = new Border
        {
            Background = ThemeAccess.Brush("OverlayBrush", "#CC000000"),
            BorderBrush = ThemeAccess.Brush("BorderStrongBrush", "#FF454E5E"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18, 12, 18, 12),
            Child = _text
        };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            MaxWidth = 520,
            Content = root
        };

        _window.SourceInitialized += (_, _) => ApplyChrome();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); _window.Hide(); };
    }

    private void ApplyChrome()
    {
        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        // Click-through + no-activate + tool window: never intercept input, never steal focus,
        // never appear in Alt+Tab.
        WindowFinder.SetClickThrough(hwnd, true);
        WindowFinder.SetOverlayChrome(hwnd, true);
        _chromeApplied = true;
    }

    public void Show(string message)
    {
        _text.Text = message;

        // Ensure HWND exists so chrome applies, without stealing activation.
        if (!_window.IsVisible)
            _window.Show();
        if (!_chromeApplied)
            ApplyChrome();

        _window.UpdateLayout();
        PositionTopCenter();

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void PositionTopCenter()
    {
        var wa = SystemParameters.WorkArea;
        _window.Left = wa.Left + (wa.Width - _window.ActualWidth) / 2;
        _window.Top = wa.Top + 48;
    }

    /// <summary>Close for real on app shutdown.</summary>
    public void Shutdown()
    {
        _hideTimer.Stop();
        _window.Close();
    }
}
