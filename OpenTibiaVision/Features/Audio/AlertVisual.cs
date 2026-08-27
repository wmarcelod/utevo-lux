using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenTibiaVision.Features.Audio;

/// <summary>
/// Builds the shared banner visual (a rounded, coloured <see cref="Border"/> holding centred
/// text) reused by both the runtime <see cref="AlertBannerWindow"/> (click-through) and its
/// drag-to-place twin <see cref="AlertPlacerWindow"/>. All brushes are frozen (principle 2).
/// </summary>
internal static class AlertVisual
{
    public static Border Build(AlertConfig cfg, string text, out TextBlock textBlock)
    {
        textBlock = new TextBlock
        {
            Text = text,
            FontSize = cfg.FontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(cfg.TextHex, "#FFFFFFFF"),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        return new Border
        {
            Background = Brush(cfg.BackgroundHex, "#E6101820"),
            BorderBrush = Brush(cfg.BorderHex, "#FF4CC2FF"),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(26, 15, 26, 15),
            Child = textBlock
        };
    }

    public static SolidColorBrush Brush(string hex, string fallbackHex)
    {
        SolidColorBrush brush;
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            brush = new SolidColorBrush(color);
        }
        catch
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex)!);
        }
        brush.Freeze();
        return brush;
    }
}
