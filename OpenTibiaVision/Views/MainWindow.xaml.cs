using System;
using System.Windows;
using OpenTibiaVision.ViewModels;

namespace OpenTibiaVision.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel { OwnerWindow = this };
        DataContext = _viewModel;

        // Restore saved regions once the window exists, so any visible mirrors layer above it.
        Loaded += (_, _) => _viewModel.LoadSavedRegions();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Shutdown();
        base.OnClosed(e);
    }
}
