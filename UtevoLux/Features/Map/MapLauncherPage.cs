using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UtevoLux.UI;

namespace UtevoLux.Features.Map;

/// <summary>
/// The nav page for TibiaMaps. Like the original, the map lives in its OWN top-level window kept
/// as a singleton; this page is just a launcher (a short description + "Abrir Mapa" button) that
/// opens or focuses that window. It also opens the window automatically the first time the user
/// navigates here. Built in code against the fork's theme tokens (blue accent).
/// </summary>
public sealed class MapLauncherPage : UserControl
{
    private readonly Action _openOrFocus;
    private bool _openedOnce;

    public MapLauncherPage(Action openOrFocus)
    {
        _openOrFocus = openOrFocus;

        var textPrimary = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");
        var textSecondary = ThemeAccess.Brush("TextSecondaryBrush", "#FF9AA4B3");
        var accent = ThemeAccess.Brush("AccentBrush", "#FF3FA9F5");
        var card = ThemeAccess.Brush("CardBrush", "#FF2C3340");
        var border = ThemeAccess.Brush("BorderBrush", "#FF333A47");
        var font = ThemeAccess.Font("Font.App", "Segoe UI");

        FontFamily = font;

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 460
        };

        // Map glyph.
        if (Application.Current?.TryFindResource("Icon.Map") is Geometry mapIcon)
        {
            stack.Children.Add(new System.Windows.Shapes.Path
            {
                Data = mapIcon,
                Fill = accent,
                Width = 48,
                Height = 48,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = "TibiaMaps",
            Foreground = textPrimary,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Mapa-mundi navegavel com todos os andares, pins, rotas de varios "
                 + "andares, clusters de spawn e busca de criaturas/NPCs. O mapa abre numa "
                 + "janela flutuante propria (sempre no topo), separada desta.",
            Foreground = textSecondary,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 22)
        });

        var openButton = new Button
        {
            Content = "Abrir Mapa",
            MinWidth = 160,
            Padding = new Thickness(16, 9, 16, 9),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        if (Application.Current?.TryFindResource("Button.Accent") is Style accentStyle)
            openButton.Style = accentStyle;
        openButton.Click += (_, _) => _openOrFocus();
        stack.Children.Add(openButton);

        stack.Children.Add(new TextBlock
        {
            Text = "Atalho: Ctrl+Alt+M mostra / esconde o mapa",
            Foreground = textSecondary,
            FontSize = 11,
            Opacity = 0.8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0)
        });

        Content = new Border
        {
            Background = card,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(40, 44, 40, 44),
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = stack
        };

        // Open the map window the first time this page becomes visible (first navigation).
        Loaded += (_, _) =>
        {
            if (_openedOnce)
                return;
            _openedOnce = true;
            _openOrFocus();
        };
    }
}
