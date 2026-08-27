using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace OpenTibiaVision.Views;

/// <summary>
/// The ~2s startup splash shown before the shell. App shows it, waits, brings up the shell behind
/// it, then calls <see cref="FadeOutAndClose"/> to dissolve onto the shell (mirrors the original
/// TibiaVision startup transition). The dot pulse animation lives in XAML.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>Fade the splash out over 400ms and close it once the fade completes.</summary>
    public void FadeOutAndClose()
    {
        var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(400));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
