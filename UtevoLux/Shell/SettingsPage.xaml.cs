using System;
using System.Windows;
using System.Windows.Controls;
using UtevoLux.Core;
using UtevoLux.Services;
using UtevoLux.UI;

namespace UtevoLux.Shell;

/// <summary>
/// Built-in shell page (not a feature module): profile selection, run-at-startup toggle,
/// UI-scale control, and the storage path. Reads/writes the shared services directly.
/// </summary>
public partial class SettingsPage : UserControl
{
    private readonly IAppServices _services;
    private readonly IShellController _shell;
    private bool _loading;

    public SettingsPage(IAppServices services, IShellController shell)
    {
        _services = services;
        _shell = shell;
        InitializeComponent();

        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        _loading = true;
        try
        {
            ProfileCombo.ItemsSource = _services.Profiles.Profiles;
            ProfileCombo.SelectedItem = _services.Profiles.ActiveProfile;
            StartupCheck.IsChecked = StartupRegistration.IsEnabled();
            StoragePathLabel.Text = _services.Settings.RootDirectory;
            UpdateScaleLabel();
        }
        finally
        {
            _loading = false;
        }
    }

    private void UpdateScaleLabel()
        => ScaleLabel.Text = $"{Math.Round(_shell.UiScale * 100)}%";

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileCombo.SelectedItem is not string name)
            return;
        _services.Profiles.Switch(name);
        _services.ShowToast($"Perfil ativo: {name}");
    }

    private void OnNewProfile(object sender, RoutedEventArgs e)
    {
        // Minimal M1 flow: create an incrementing name; a rename UI can come later.
        string baseName = "Perfil";
        int n = 2;
        string name = baseName;
        var existing = _services.Profiles.Profiles;
        while (Contains(existing, name))
            name = $"{baseName} {n++}";

        _services.Profiles.Create(name);
        _services.Profiles.Switch(name);
        Refresh();
        _services.ShowToast($"Perfil criado: {name}");
    }

    private static bool Contains(System.Collections.Generic.IReadOnlyList<string> list, string value)
    {
        foreach (string s in list)
            if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        bool on = StartupCheck.IsChecked == true;
        StartupRegistration.SetEnabled(on);
        _services.ShowToast(on ? "Inicia com o Windows." : "Nao inicia com o Windows.");
    }

    private void OnScaleUp(object sender, RoutedEventArgs e) { _shell.StepUiScale(+1); UpdateScaleLabel(); }
    private void OnScaleDown(object sender, RoutedEventArgs e) { _shell.StepUiScale(-1); UpdateScaleLabel(); }
    private void OnScaleReset(object sender, RoutedEventArgs e) { _shell.ResetUiScale(); UpdateScaleLabel(); }
}
