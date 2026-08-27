using System.Collections.ObjectModel;
using UtevoLux.ViewModels;

namespace UtevoLux.Shell;

/// <summary>
/// Backs the shell chrome: the nav rail and which page is active. Selecting a nav item flips
/// the IsActive booleans (the "IsXPageActive" model) that each kept-alive page's Visibility
/// binds to.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private NavItem? _selectedNav;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    public NavItem? SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (SetProperty(ref _selectedNav, value))
                ApplyActive();
        }
    }

    public string ActiveTitle => _selectedNav?.Title ?? "Utevo Lux";

    public void Add(NavItem item) => NavItems.Add(item);

    public void SelectFirst()
    {
        if (_selectedNav is null && NavItems.Count > 0)
            SelectedNav = NavItems[0];
    }

    private void ApplyActive()
    {
        foreach (NavItem item in NavItems)
            item.IsActive = ReferenceEquals(item, _selectedNav);
        OnPropertyChanged(nameof(ActiveTitle));
    }
}
