using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UtevoLux.Core;
using UtevoLux.Services;
using UtevoLux.Shell;
using UtevoLux.Views;

namespace UtevoLux;

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
        //   UtevoLux.exe --selftest [output-file]
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

            // Best-effort: check GitHub for a newer release once per launch.
            CheckForUpdatesAsync();
        };
        timer.Start();
    }

    /// <summary>
    /// Checks GitHub Releases; if a newer version exists, offers to download + run the installer.
    /// Runs on the UI thread (awaits resume on the dispatcher), fire-and-forget, never blocks startup.
    /// </summary>
    private async void CheckForUpdatesAsync()
    {
        if (_services == null)
            return;

        UpdateService.UpdateInfo? info = await UpdateService.CheckAsync();
        if (info == null)
            return;

        string current = UpdateService.CurrentVersion().ToString(3);
        bool accept = _services.Confirm(
            "Atualizacao disponivel",
            $"Utevo Lux {info.Tag} esta disponivel (voce tem a v{current}).\n\nBaixar e instalar agora?");
        if (!accept)
            return;

        _services.ShowToast("Baixando atualizacao...");
        bool started = await UpdateService.DownloadAndLaunchAsync(info);
        if (started)
        {
            Shutdown(0); // exit so the installer can replace files and relaunch
        }
        else
        {
            _services.Info("Atualizacao", "Nao foi possivel baixar a atualizacao agora. Vou abrir a pagina de releases.");
            UpdateService.OpenReleasesPage();
        }
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
            "Utevo Lux - erro inesperado",
            e.Exception.Message,
            UI.ThemedMessageBox.Buttons.Ok);
        e.Handled = true;
    }
}
