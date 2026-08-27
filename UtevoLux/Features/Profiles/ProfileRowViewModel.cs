using UtevoLux.ViewModels;

namespace UtevoLux.Features.Profiles;

/// <summary>
/// One row in the profiles list — the fork's counterpart to the original's <c>ProfileListItem</c>.
/// In the foundation <see cref="UtevoLux.Core.IProfileService"/> the profile NAME is its id
/// (each profile is a single <c>Profiles\{name}.json</c> file), so a row carries just its name and
/// whether it is the active profile. Rows are lightweight data items; the action buttons bind to
/// the page view model's commands and pass the row as the parameter (the original's
/// command-with-target design).
/// </summary>
public sealed class ProfileRowViewModel : ViewModelBase
{
    private bool _isActive;

    public ProfileRowViewModel(string name, bool isActive)
    {
        Name = name;
        _isActive = isActive;
    }

    /// <summary>Profile name (also its id in the foundation service).</summary>
    public string Name { get; }

    /// <summary>True when this is the currently active profile.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
