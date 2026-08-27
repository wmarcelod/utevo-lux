using System.Windows;
using OpenTibiaVision.Services;

namespace OpenTibiaVision.Core;

/// <summary>
/// The single surface feature modules target. Everything a module needs — hotkeys, persistence,
/// profiles, window discovery, DWM mirroring, DPI conversion, and a couple of shell affordances
/// (toast, dialogs, owner window) — hangs off here.
/// </summary>
public interface IAppServices
{
    IHotkeyManager Hotkeys { get; }

    /// <summary>Global settings (shared across profiles).</summary>
    ISettingsStore Settings { get; }

    /// <summary>Named profiles; <c>Profiles.Current</c> is the active per-profile store.</summary>
    IProfileService Profiles { get; }

    IWindowService Windows { get; }
    IDwmService Dwm { get; }
    IDpiService Dpi { get; }

    /// <summary>The shell window, for owning dialogs/overlays. May be null very early in startup.</summary>
    Window? ShellWindow { get; }

    /// <summary>Show the shared click-through toast.</summary>
    void ShowToast(string message);

    /// <summary>Themed yes/no confirmation beside the shell.</summary>
    bool Confirm(string title, string message);

    /// <summary>Themed information dialog beside the shell.</summary>
    void Info(string title, string message);
}
