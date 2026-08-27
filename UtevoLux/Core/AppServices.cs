using System;
using System.IO;
using System.Windows;
using UtevoLux.Services;
using UtevoLux.UI;

namespace UtevoLux.Core;

/// <summary>
/// Concrete <see cref="IAppServices"/>. Owns the singleton service instances and wires the
/// shell affordances (toast/dialogs) once <see cref="ShellWindow"/> is set. Built once at
/// startup and handed to every feature module.
/// </summary>
public sealed class AppServices : IAppServices, IDisposable
{
    private readonly HotkeyManager _hotkeys;
    private readonly SettingsStore _settings;
    private readonly ProfileService _profiles;

    public AppServices()
    {
        string root = SettingsStore.DefaultRoot;
        Directory.CreateDirectory(root);

        _settings = new SettingsStore(Path.Combine(root, "settings.json"));
        _profiles = new ProfileService(_settings, Path.Combine(root, "Profiles"));
        _hotkeys = new HotkeyManager();

        Windows = new WindowService();
        Dwm = new DwmService();
        Dpi = new DpiService();
    }

    public IHotkeyManager Hotkeys => _hotkeys;
    public ISettingsStore Settings => _settings;
    public IProfileService Profiles => _profiles;
    public IWindowService Windows { get; }
    public IDwmService Dwm { get; }
    public IDpiService Dpi { get; }

    public Window? ShellWindow { get; set; }

    public void ShowToast(string message) => Toast.Instance.Show(message);

    public bool Confirm(string title, string message)
        => ThemedMessageBox.Show(ShellWindow, title, message, ThemedMessageBox.Buttons.YesNo)
            == ThemedMessageBox.Result.Yes;

    public void Info(string title, string message)
        => ThemedMessageBox.Show(ShellWindow, title, message, ThemedMessageBox.Buttons.Ok);

    /// <summary>Start global services (hotkey hooks). Call on the UI thread after the shell exists.</summary>
    public void Start() => _hotkeys.Start();

    public void Dispose()
    {
        _hotkeys.Dispose();
        _profiles.Dispose();
        _settings.Flush();
        _settings.Dispose();
    }
}
