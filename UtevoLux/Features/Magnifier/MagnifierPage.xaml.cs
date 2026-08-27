using System.Windows.Controls;

namespace UtevoLux.Features.Magnifier;

/// <summary>The Magnifier dashboard — built once, kept alive, visibility-toggled on nav.</summary>
public partial class MagnifierPage : UserControl
{
    public MagnifierPage(MagnifierPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
