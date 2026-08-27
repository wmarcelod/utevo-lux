using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using OpenTibiaVision.UI;

namespace OpenTibiaVision.Features.Map;

/// <summary>
/// Chromeless themed prompt for naming a route before saving it. Ported from the original
/// TibiaVision <c>RouteSaveDialog</c> (Width 340, single text box, max 40 chars), rebuilt in code
/// against the fork's theme tokens (blue accent) so it matches <see cref="ThemedMessageBox"/> and
/// <see cref="OpenTibiaVision.Features.Profiles.ProfileNameDialog"/>.
/// </summary>
public sealed class RouteSaveDialog : Window
{
    private readonly TextBox _nameBox;

    public string RouteName { get; private set; } = "";

    public RouteSaveDialog(string suggestedName = "")
    {
        var bg = ThemeAccess.Brush("SurfaceBrush", "#FF1B1F27");
        var border = ThemeAccess.Brush("BorderStrongBrush", "#FF454E5E");
        var textPrimary = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");
        var accent = ThemeAccess.Brush("AccentBrush", "#FF3FA9F5");
        var accentSoft = ThemeAccess.Brush("AccentSoftBrush", "#553FA9F5");
        var font = ThemeAccess.Font("Font.App", "Segoe UI");

        Title = "Save Route";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = font;

        var root = new Border
        {
            Background = bg,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 0, Opacity = 0.55 }
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "Save Route",
            Foreground = textPrimary,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _nameBox = new TextBox
        {
            Text = suggestedName ?? "",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            MaxLength = MapRoute.MaxNameLength,
            CaretBrush = accent,
            SelectionBrush = accentSoft,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 16)
        };
        _nameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
        };
        stack.Children.Add(_nameBox);

        var buttonBar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

        var save = new Button { Content = "Save", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        if (Application.Current?.TryFindResource("Button.Accent") is Style accentStyle)
            save.Style = accentStyle;
        save.Click += (_, _) => Commit();

        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; };

        buttonBar.Children.Add(save);
        buttonBar.Children.Add(cancel);
        stack.Children.Add(buttonBar);

        root.Child = stack;
        Content = root;

        Loaded += (_, _) => { _nameBox.Focus(); _nameBox.SelectAll(); };
    }

    private void Commit()
    {
        string text = ShareCodeService.SanitizeText(_nameBox.Text, MapRoute.MaxNameLength);
        if (string.IsNullOrWhiteSpace(text))
        {
            _nameBox.Focus();
            return;
        }
        RouteName = text;
        DialogResult = true;
    }
}
