using System.Windows.Controls;

namespace UtevoLux.Features.Audio;

/// <summary>The Audio / Timers / Alerts dashboard — built once, kept alive, visibility-toggled.</summary>
public partial class AudioPage : UserControl
{
    public AudioPage(AudioPageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
