using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;
using OpenTibiaVision.Shell;
using OpenTibiaVision.Views;

namespace OpenTibiaVision;

public partial class App : Application
{
    private AppServices? _services;

    // The bundled app/window icon (Assets/icon.ico); null if the resource is missing. Loaded in
    // OnStartup (after base.OnStartup) so the pack:// scheme + ResourceAssembly are ready.
    private ImageSource? _appIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless smoke test: exercise interop + services without the GUI, write a report, exit.
        //   OpenTibiaVision.exe --selftest [output-file]
        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            string outputPath = e.Args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                                ?? "selftest-result.txt";
            SelfTest.Run(outputPath);
            Shutdown(0);
            return;
        }

        // Surface unexpected UI-thread errors rather than crash silently.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        bool startMinimized = e.Args.Contains(StartupRegistration.StartupArg, StringComparer.OrdinalIgnoreCase);

        _services = new AppServices();
        _appIcon = LoadAppIcon();

        // Launched by Windows at logon: no splash, go straight to the tray (the shell minimizes
        // itself in OnLoaded). Restore/startup stays owned by the shell.
        if (startMinimized)
        {
            var minimizedShell = new ShellWindow(_services, startMinimized: true);
            minimizedShell.Icon = _appIcon;
            MainWindow = minimizedShell;
            minimizedShell.Show();
            return;
        }

        // Normal launch: show the splash for ~2s, then bring the shell up behind it and fade the
        // splash onto it. The shell runs its own region-restore behind its startup overlay, so the
        // splash only wraps the launch (it does not duplicate any restore work).
        var splash = new SplashWindow { Icon = _appIcon };
        splash.Show();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            var shell = new ShellWindow(_services, startMinimized: false);
            shell.Icon = _appIcon;
            MainWindow = shell;
            shell.Show();

            splash.FadeOutAndClose();
        };
        timer.Start();
    }

    private static ImageSource? LoadAppIcon()
    {
        try
        {
            return BitmapFrame.Create(new Uri("pack://application:,,,/Assets/icon.ico", UriKind.Absolute));
        }
        catch
        {
            return null;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        UI.ThemedMessageBox.Show(
            MainWindow,
            "OpenTibiaVision - erro inesperado",
            e.Exception.Message,
            UI.ThemedMessageBox.Buttons.Ok);
        e.Handled = true;
    }
}
