using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTibiaVision.UI;

namespace OpenTibiaVision.Features.Overlays;

/// <summary>
/// Tiny factory helpers so the overlay dashboard pages can be built in code (no XAML) while
/// staying consistent with the app theme. Code-built controls read theme tokens through
/// <see cref="ThemeAccess"/> with hardcoded fallbacks (matching Toast / ThemedMessageBox).
/// </summary>
internal static class OverlayUi
{
    /// <summary>A neutral swatch palette shared by note / grid / marker colour pickers.</summary>
    public static readonly string[] Palette =
    {
        "#FFF3F5F9", "#FF12151C", "#FF3FA9F5", "#FF3FB950", "#FFD8A24A",
        "#FFE5534B", "#FFB57BEE", "#FF57D0C9", "#FFED6A5E", "#FF9AA4B3",
    };

    public static SolidColorBrush Brush(string key, string fallback) => ThemeAccess.Brush(key, fallback);

    public static FontFamily AppFont() => ThemeAccess.Font("Font.App", "Segoe UI");

    public static TextBlock Label(string text, double size = 13, bool secondary = false)
        => new()
        {
            Text = text,
            FontSize = size,
            FontFamily = AppFont(),
            Foreground = secondary
                ? Brush("TextSecondaryBrush", "#FF9AA4B3")
                : Brush("TextPrimaryBrush", "#FFF3F5F9"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

    public static TextBlock Header(string text)
        => new()
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            FontFamily = AppFont(),
            Foreground = Brush("TextPrimaryBrush", "#FFF3F5F9"),
            Margin = new Thickness(0, 0, 0, 6),
        };

    public static Button Button(string text, Action onClick, bool accent = false, double minWidth = 0)
    {
        var b = new Button
        {
            Content = text,
            MinWidth = minWidth,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(12, 6, 12, 6),
        };
        if (Application.Current?.TryFindResource(accent ? "Button.Accent" : typeof(Button)) is Style s)
            b.Style = s;
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Horizontal row of colour swatches; clicking one calls back with its hex.</summary>
    public static WrapPanel SwatchRow(Action<string> onPick, Func<string> currentHex)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (string hex in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var fill = new SolidColorBrush(color);
            fill.Freeze();

            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(4),
                Background = fill,
                BorderBrush = Brush("BorderStrongBrush", "#FF454E5E"),
                BorderThickness = new Thickness(hex.Equals(currentHex(), StringComparison.OrdinalIgnoreCase) ? 2 : 1),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            string captured = hex;
            swatch.MouseLeftButtonDown += (_, _) => onPick(captured);
            panel.Children.Add(swatch);
        }
        return panel;
    }

    /// <summary>A labelled 0..1 opacity (or arbitrary range) slider that reports live changes.</summary>
    public static Grid SliderRow(string label, double value, double min, double max,
        Action<double> onChanged, string? format = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });

        TextBlock caption = Label(label, secondary: true);
        Grid.SetColumn(caption, 0);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = false,
        };
        Grid.SetColumn(slider, 1);

        TextBlock readout = Label(Format(value, format), secondary: true);
        readout.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(readout, 2);

        slider.ValueChanged += (_, e) =>
        {
            readout.Text = Format(e.NewValue, format);
            onChanged(e.NewValue);
        };

        grid.Children.Add(caption);
        grid.Children.Add(slider);
        grid.Children.Add(readout);
        return grid;
    }

    private static string Format(double v, string? format)
        => format is null
            ? v.ToString("0.##", CultureInfo.InvariantCulture)
            : v.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>System font families, de-duplicated and alphabetised, for a font ComboBox.</summary>
    public static IReadOnlyList<string> SystemFonts()
        => Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
