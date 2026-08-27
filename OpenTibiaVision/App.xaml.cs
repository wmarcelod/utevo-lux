using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using OpenTibiaVision.Core;
using OpenTibiaVision.Services;
using OpenTibiaVision.Shell;

namespace OpenTibiaVision;

public partial class App : Application
{
    private AppServices? _services;

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

        var shell = new ShellWindow(_services, startMinimized);
        MainWindow = shell;
        shell.Show();
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
