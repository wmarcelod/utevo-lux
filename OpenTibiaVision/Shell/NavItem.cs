using System;
using System.Windows.Controls;
using System.Windows.Media;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Shell;

/// <summary>
/// One sidebar entry. Its page is built ONCE (lazily, on first access) and cached, then kept
/// alive; navigation only flips <see cref="IsActive"/>, which the page's Visibility binds to
/// (optimization principle 3 — O(1) nav, full state preserved).
/// </summary>
public sealed class NavItem : ViewModelBase
{
    private readonly Func<UserControl> _factory;
    private UserControl? _page;
    private bool _isActive;

    public NavItem(string id, string title, Geometry icon, Func<UserControl> factory)
    {
        Id = id;
        Title = title;
        Icon = icon;
        _factory = factory;
    }

    public string Id { get; }
    public string Title { get; }
    public Geometry Icon { get; }

    /// <summary>Built once, then reused for the lifetime of the app.</summary>
    public UserControl Page => _page ??= _factory();

    public bool IsBuilt => _page is not null;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}
