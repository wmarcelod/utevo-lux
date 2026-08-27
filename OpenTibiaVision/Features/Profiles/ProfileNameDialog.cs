using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTibiaVision.UI;

namespace OpenTibiaVision.Features.Profiles;

/// <summary>
/// A chromeless, themed single-line text prompt — the fork's reconstruction of the original
/// <c>RenameProfileDialog</c>, built in code (reads theme tokens via <see cref="ThemeAccess"/> with
/// hardcoded fallbacks, blue accent OK button) so it matches <see cref="ThemedMessageBox"/> rather
/// than falling back to light system chrome. Used to rename a profile. Returns the trimmed text, or
/// <c>null</c> if the user cancelled or left it empty.
/// </summary>
internal static class ProfileNameDialog
{
    public static string? Prompt(Window? owner, string title, string label, string initial)
    {
        var bg = ThemeAccess.Brush("SurfaceBrush", "#FF1B1F27");
        var border = ThemeAccess.Brush("BorderStrongBrush", "#FF454E5E");
        var textPrimary = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");
        var textSecondary = ThemeAccess.Brush("TextSecondaryBrush", "#FF9AA4B3");
        var font = ThemeAccess.Font("Font.App", "Segoe UI");

        var win = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.Height,
            Width = 360,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = owner?.Topmost ?? true,
            WindowStartupLocation = WindowStartupLocation.Manual,
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
            Text = label,
            Foreground = textSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var nameBox = new TextBox
        {
            Text = initial ?? string.Empty,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 18)
        };
        stack.Children.Add(nameBox);

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = new Button
        {
            Content = "Cancelar",
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) => { win.DialogResult = false; };

        var ok = new Button
        {
            Content = "OK",
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true
        };
        if (Application.Current?.TryFindResource("Button.Accent") is Style accentStyle)
            ok.Style = accentStyle;
        ok.Click += (_, _) => { win.DialogResult = true; };

        buttonBar.Children.Add(cancel);
        buttonBar.Children.Add(ok);
        stack.Children.Add(buttonBar);

        root.Child = stack;
        win.Content = root;

        // Enter commits from within the text box (IsDefault handles it too, but be explicit).
        nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                win.DialogResult = true;
                e.Handled = true;
            }
        };

        win.SourceInitialized += (_, _) => CenterOnOwner(win, owner);
        win.ContentRendered += (_, _) =>
        {
            nameBox.SelectAll();
            nameBox.Focus();
        };

        bool committed = win.ShowDialog() == true;
        if (!committed)
            return null;

        string result = nameBox.Text.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static void CenterOnOwner(Window win, Window? owner)
    {
        var wa = SystemParameters.WorkArea;

        double left, top;
        if (owner is null || owner.WindowState == WindowState.Minimized)
        {
            left = wa.Left + (wa.Width - win.ActualWidth) / 2;
            top = wa.Top + (wa.Height - win.ActualHeight) / 2;
        }
        else
        {
            left = owner.Left + (owner.ActualWidth - win.ActualWidth) / 2;
            top = owner.Top + (owner.ActualHeight - win.ActualHeight) / 2;
        }

        win.Left = Math.Clamp(left, wa.Left, Math.Max(wa.Left, wa.Right - win.ActualWidth));
        win.Top = Math.Clamp(top, wa.Top, Math.Max(wa.Top, wa.Bottom - win.ActualHeight));
    }
}
