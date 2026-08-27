using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTibiaVision.Core;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// A tiny modal that captures the next key + modifier combination and returns it as a
/// <see cref="HotkeyGesture"/>. Modifier-only presses are ignored (you cannot bind "Ctrl"
/// alone); Esc cancels. Kept local to the Audio feature so it never touches shared files.
/// </summary>
public sealed class HotkeyCaptureWindow : Window
{
    public HotkeyGesture? Result { get; private set; }

    public HotkeyCaptureWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "OpenTibiaVision - Definir tecla";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new TextBlock
        {
            Text = "Pressione a combinacao de teclas...\n(Esc cancela)",
            Foreground = AlertVisual.Brush("#FFF3F5F9", "#FFF3F5F9"),
            FontSize = 15,
            TextAlignment = TextAlignment.Center
        };

        Content = new Border
        {
            Background = AlertVisual.Brush("#F2101820", "#F2101820"),
            BorderBrush = AlertVisual.Brush("#FF4CC2FF", "#FF4CC2FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(28, 22, 28, 22),
            Child = text
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.Handled = true;

        // Resolve the "real" key even when it arrives as Key.System (Alt combos).
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Result = null;
            DialogResult = false;
            Close();
            return;
        }

        if (IsModifier(key))
            return; // wait for a non-modifier

        Result = new HotkeyGesture(key, Keyboard.Modifiers);
        DialogResult = true;
        Close();
    }

    private static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or
        Key.LWin or Key.RWin or
        Key.System;
}
