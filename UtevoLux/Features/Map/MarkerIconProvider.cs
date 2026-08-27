using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace UtevoLux.Features.Map;

/// <summary>
/// Loads the 20 map-marker icons (<c>Resources/Icons/MapMarkers/marker_NN.png</c>), memoized and
/// frozen. When an icon file is missing it falls back to a single shared drawn dot.
///
/// FORK DEVIATION FROM ORIGINAL: the fallback dot is painted in the fork's blue AccentBrush
/// (#FF3FA9F5) instead of the original's orange, per the fork's "accent stays blue, never orange"
/// rule. NOTE: the Icons/MapMarkers folder is NOT part of the four asset folders copied by the
/// data stage, so unless it is copied separately every marker renders as the blue fallback dot.
/// Everything else is ported faithfully from the original TibiaVision.
/// </summary>
public static class MarkerIconProvider
{
    private static ImageSource[]? _icons;

    private static readonly object Gate = new object();

    public static ImageSource GetIcon(int iconId)
    {
        ImageSource[] array = EnsureLoaded();
        if (iconId < 0 || iconId >= array.Length)
        {
            iconId = 0;
        }
        return array[iconId];
    }

    private static ImageSource[] EnsureLoaded()
    {
        if (_icons != null)
        {
            return _icons;
        }
        lock (Gate)
        {
            if (_icons != null)
            {
                return _icons;
            }
            string path = ResolveIconDirectory();
            ImageSource[] array = new ImageSource[20];
            ImageSource? imageSource = null;
            for (int i = 0; i < array.Length; i++)
            {
                try
                {
                    string text = Path.Combine(path, $"marker_{i:00}.png");
                    if (File.Exists(text))
                    {
                        BitmapImage bitmapImage = new BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                        bitmapImage.UriSource = new Uri(text, UriKind.Absolute);
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();
                        array[i] = bitmapImage;
                        continue;
                    }
                }
                catch
                {
                }
                array[i] = imageSource ?? (imageSource = CreateFallbackDot());
            }
            _icons = array;
            return _icons;
        }
    }

    private static string ResolveIconDirectory()
    {
        string[] array = new string[2]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons", "MapMarkers"),
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Icons", "MapMarkers")
        };
        string[] array2 = array;
        foreach (string text in array2)
        {
            try
            {
                if (Directory.Exists(text))
                {
                    return text;
                }
            }
            catch
            {
            }
        }
        return array[0];
    }

    private static ImageSource CreateFallbackDot()
    {
        DrawingVisual drawingVisual = new DrawingVisual();
        using (DrawingContext drawingContext = drawingVisual.RenderOpen())
        {
            // Fork blue AccentBrush #FF3FA9F5 (R=0x3F, G=0xA9, B=0xF5) instead of the original orange.
            drawingContext.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x3F, 0xA9, 0xF5)), new Pen(new SolidColorBrush(Color.FromRgb(20, 22, 28)), 1.5), new Point(12.0, 12.0), 10.0, 10.0);
        }
        RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(24, 24, 96.0, 96.0, PixelFormats.Pbgra32);
        renderTargetBitmap.Render(drawingVisual);
        renderTargetBitmap.Freeze();
        return renderTargetBitmap;
    }
}
