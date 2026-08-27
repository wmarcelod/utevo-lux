using System;
using System.Collections.Generic;

namespace OpenTibiaVision.Core;

/// <summary>
/// Manages named configuration profiles. Each profile is its own atomic JSON file under
/// Profiles\{name}.json (per-file-path template). A last_profile pointer records the active
/// one so the app reopens where the user left off. <see cref="Current"/> exposes the active
/// profile as an <see cref="ISettingsStore"/>, so feature modules that want per-profile state
/// (e.g. different region sets per character) read/write it exactly like the global store.
/// </summary>
public interface IProfileService
{
    IReadOnlyList<string> Profiles { get; }

    /// <summary>Name of the active profile.</summary>
    string ActiveProfile { get; }

    /// <summary>Settings store scoped to the active profile.</summary>
    ISettingsStore Current { get; }

    /// <summary>Raised (UI thread) after <see cref="Current"/> points at a different profile.</summary>
    event Action? ActiveProfileChanged;

    void Create(string name);
    void Switch(string name);
    void Delete(string name);
    void Rename(string oldName, string newName);
}
