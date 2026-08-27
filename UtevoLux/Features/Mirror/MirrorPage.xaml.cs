using System.Windows.Controls;

namespace UtevoLux.Features.Mirror;

/// <summary>The regions dashboard — the Mirror module's page. Built once, kept alive.</summary>
public partial class MirrorPage : UserControl
{
    public MirrorPage(MirrorPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
