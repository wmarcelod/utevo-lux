using System.Windows.Controls;

namespace OpenTibiaVision.Features.Profiles;

/// <summary>The profiles manager — the Profiles module's page. Built once, kept alive.</summary>
public partial class ProfilesPage : UserControl
{
    public ProfilesPage(ProfilesPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
