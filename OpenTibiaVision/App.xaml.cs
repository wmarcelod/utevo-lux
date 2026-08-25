using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using OpenTibiaVision.Services;
using OpenTibiaVision.Views;

namespace OpenTibiaVision;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless smoke test: exercise the interop + services without the GUI, write a
        // report, and exit. Used to verify the app's runtime plumbing (P/Invoke marshalling,
        // DWM/DPI calls, JSON) on machines/CI without driving the window. Usage:
        //   OpenTibiaVision.exe --selftest [output-file]
        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            string outputPath = e.Args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
                                ?? "selftest-result.txt";
            SelfTest.Run(outputPath);
            Shutdown(0);
            return;
        }

        // In M1 we surface unexpected errors rather than crash silently. A real release
        // would log these; here a message box is enough to prove the app is running.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "OpenTibiaVision - erro inesperado",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
