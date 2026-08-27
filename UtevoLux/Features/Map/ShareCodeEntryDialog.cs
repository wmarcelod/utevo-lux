using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using UtevoLux.UI;

namespace UtevoLux.Features.Map;

/// <summary>
/// Chromeless themed prompt for importing a pin (<c>TV-</c>) or route (<c>TVR-</c>) share code.
/// Ported from the original tool <c>ShareCodeEntryDialog</c> (Width 380), rebuilt in code
/// against the fork's theme tokens (blue accent). Decoding is delegated to
/// <see cref="ShareCodeService"/> and bounds-checked against the current map.
/// </summary>
public sealed class ShareCodeEntryDialog : Window
{
    private readonly MapBounds _bounds;
    private readonly TextBox _codeBox;
    private readonly TextBlock _errorText;

    public MapMarker? Marker { get; private set; }
    public MapRoute? Route { get; private set; }

    public ShareCodeEntryDialog(MapBounds bounds)
    {
        _bounds = bounds;

        var bg = ThemeAccess.Brush("SurfaceBrush", "#FF1B1F27");
        var border = ThemeAccess.Brush("BorderStrongBrush", "#FF454E5E");
        var textPrimary = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");
        var textSecondary = ThemeAccess.Brush("TextSecondaryBrush", "#FF9AA4B3");
        var danger = ThemeAccess.Brush("DangerBrush", "#FFE5534B");
        var accent = ThemeAccess.Brush("AccentBrush", "#FF3FA9F5");
        var accentSoft = ThemeAccess.Brush("AccentSoftBrush", "#553FA9F5");
        var font = ThemeAccess.Font("Font.App", "Segoe UI");

        Title = "Enter Share Code";
        Width = 380;
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
            Text = "Enter Share Code",
            Foreground = textPrimary,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Paste a pin or route code from another user:",
            Foreground = textSecondary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _codeBox = new TextBox
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            CaretBrush = accent,
            SelectionBrush = accentSoft,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 0, 6)
        };
        _codeBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Import(); e.Handled = true; }
        };
        stack.Children.Add(_codeBox);

        _errorText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = danger,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        stack.Children.Add(_errorText);

        var buttonBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var import = new Button { Content = "Import", MinWidth = 84, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        if (Application.Current?.TryFindResource("Button.Accent") is Style accentStyle)
            import.Style = accentStyle;
        import.Click += (_, _) => Import();

        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; };

        buttonBar.Children.Add(import);
        buttonBar.Children.Add(cancel);
        stack.Children.Add(buttonBar);

        root.Child = stack;
        Content = root;

        Loaded += (_, _) => _codeBox.Focus();
    }

    private void Import()
    {
        string text = _codeBox.Text?.Trim() ?? "";
        if (text.StartsWith("TVR-", StringComparison.OrdinalIgnoreCase))
            TryRoute(text, showErrorOnFail: true);
        else if (text.StartsWith("TV-", StringComparison.OrdinalIgnoreCase))
            TryPin(text, showErrorOnFail: true);
        else if (!TryPin(text, showErrorOnFail: false))
            TryRoute(text, showErrorOnFail: true);
    }

    private bool TryPin(string input, bool showErrorOnFail)
    {
        ShareCodeService.ShareCodeResult result = ShareCodeService.TryDecode(input, _bounds);
        if (result.Success)
        {
            Marker = result.Marker;
            DialogResult = true;
            return true;
        }
        if (showErrorOnFail)
            ShowError(result.Error);
        return false;
    }

    private bool TryRoute(string input, bool showErrorOnFail)
    {
        ShareCodeService.RouteCodeResult result = ShareCodeService.TryDecodeRoute(input, _bounds);
        if (result.Success)
        {
            Route = result.Route;
            DialogResult = true;
            return true;
        }
        if (showErrorOnFail)
            ShowError(result.Error);
        return false;
    }

    private void ShowError(string? message)
    {
        _errorText.Text = message ?? "That code could not be read.";
        _errorText.Visibility = Visibility.Visible;
        _codeBox.Focus();
        _codeBox.SelectAll();
    }
}
