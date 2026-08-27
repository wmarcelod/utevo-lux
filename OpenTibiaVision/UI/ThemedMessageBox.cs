using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenTibiaVision.UI;

/// <summary>
/// A chromeless, themed replacement for <see cref="MessageBox"/>. Code-built (reads theme
/// tokens with hardcoded fallbacks) and positioned BESIDE its owner rather than dead-center,
/// so it doesn't cover the thing the user is deciding about.
/// </summary>
public static class ThemedMessageBox
{
    public enum Buttons { Ok, OkCancel, YesNo }
    public enum Result { Ok, Cancel, Yes, No }

    public static Result Show(Window? owner, string title, string message, Buttons buttons = Buttons.Ok)
    {
        var bg = ThemeAccess.Brush("SurfaceBrush", "#FF1B1F27");
        var border = ThemeAccess.Brush("BorderStrongBrush", "#FF454E5E");
        var textPrimary = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");
        var textSecondary = ThemeAccess.Brush("TextSecondaryBrush", "#FF9AA4B3");
        var font = ThemeAccess.Font("Font.App", "Segoe UI");

        Result result = buttons == Buttons.OkCancel || buttons == Buttons.Ok ? Result.Cancel : Result.No;

        var win = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = owner?.Topmost ?? true,
            WindowStartupLocation = WindowStartupLocation.Manual,
            MinWidth = 300,
            MaxWidth = 460,
            FontFamily = font
        };
        if (owner is not null)
            win.Owner = owner;

        var root = new Border
        {
            Background = bg,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = textPrimary,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = textSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        void AddButton(string content, Result r, bool accent, bool isDefault, bool isCancel)
        {
            var b = new Button
            {
                Content = content,
                MinWidth = 84,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            if (accent && Application.Current?.TryFindResource("Button.Accent") is Style accentStyle)
                b.Style = accentStyle;
            b.Click += (_, _) => { result = r; win.DialogResult = true; };
            buttonBar.Children.Add(b);
        }

        switch (buttons)
        {
            case Buttons.Ok:
                AddButton("OK", Result.Ok, accent: true, isDefault: true, isCancel: true);
                break;
            case Buttons.OkCancel:
                AddButton("Cancelar", Result.Cancel, accent: false, isDefault: false, isCancel: true);
                AddButton("OK", Result.Ok, accent: true, isDefault: true, isCancel: false);
                break;
            case Buttons.YesNo:
                AddButton("Nao", Result.No, accent: false, isDefault: false, isCancel: true);
                AddButton("Sim", Result.Yes, accent: true, isDefault: true, isCancel: false);
                break;
        }

        stack.Children.Add(buttonBar);
        root.Child = stack;
        win.Content = root;

        // Position beside the owner (to its right, vertically centered), clamped on-screen.
        win.SourceInitialized += (_, _) => PositionBeside(win, owner);

        win.ShowDialog();
        return result;
    }

    private static void PositionBeside(Window win, Window? owner)
    {
        if (owner is null || owner.WindowState == WindowState.Minimized)
        {
            win.Left = (SystemParameters.WorkArea.Width - win.ActualWidth) / 2 + SystemParameters.WorkArea.Left;
            win.Top = (SystemParameters.WorkArea.Height - win.ActualHeight) / 2 + SystemParameters.WorkArea.Top;
            return;
        }

        double gap = 12;
        double left = owner.Left + owner.ActualWidth + gap;
        double top = owner.Top + (owner.ActualHeight - win.ActualHeight) / 2;

        var wa = SystemParameters.WorkArea;
        // If it would fall off the right edge, place it to the left of the owner instead.
        if (left + win.ActualWidth > wa.Right)
            left = owner.Left - win.ActualWidth - gap;
        left = Math.Clamp(left, wa.Left, Math.Max(wa.Left, wa.Right - win.ActualWidth));
        top = Math.Clamp(top, wa.Top, Math.Max(wa.Top, wa.Bottom - win.ActualHeight));

        win.Left = left;
        win.Top = top;
    }
}
