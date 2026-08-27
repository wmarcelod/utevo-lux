using System.Windows.Controls;

namespace OpenTibiaVision.Features.Obs;

/// <summary>The OBS tools dashboard — the OBS module's page. Built once, kept alive.</summary>
public partial class ObsPage : UserControl
{
    public ObsPage(ObsPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
