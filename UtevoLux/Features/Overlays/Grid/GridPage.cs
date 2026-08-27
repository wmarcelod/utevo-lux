using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace UtevoLux.Features.Overlays.GridOverlay;

/// <summary>The Grid dashboard, built in code. Built once, kept alive, visibility-toggled.</summary>
public sealed class GridPage : UserControl
{
    private readonly GridPageViewModel _vm;

    public GridPage(GridPageViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        Margin = new Thickness(16);

        var stack = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };

        // Source picker
        stack.Children.Add(OverlayUi.Header("Janela fonte"));
        var picker = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var combo = new ComboBox { Width = 260, DisplayMemberPath = "Title", Margin = new Thickness(0, 0, 8, 0) };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(GridPageViewModel.Sources)));
        combo.SetBinding(Selector_SelectedItem(), new Binding(nameof(GridPageViewModel.SelectedSource)) { Mode = BindingMode.TwoWay });
        picker.Children.Add(combo);
        picker.Children.Add(OverlayUi.Button("Atualizar", () => _vm.RefreshSourcesCommand.Execute(null)));
        picker.Children.Add(OverlayUi.Button("Detectar Tibia", () => _vm.DetectTibiaCommand.Execute(null)));
        stack.Children.Add(picker);

        // Pin / hide
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        actions.Children.Add(OverlayUi.Button("Fixar grade", () => _vm.PinGridCommand.Execute(null), accent: true));
        actions.Children.Add(OverlayUi.Button("Ocultar grade", () => _vm.HideGridCommand.Execute(null)));
        stack.Children.Add(actions);

        // Settings
        stack.Children.Add(OverlayUi.Header("Aparencia"));
        stack.Children.Add(OverlayUi.SliderRow("Tamanho (px)", _vm.GridSize, 4, 128, v => _vm.GridSize = (int)v, "0"));
        stack.Children.Add(OverlayUi.SliderRow("Espessura", _vm.LineThickness, 0.5, 4, v => _vm.LineThickness = v));
        stack.Children.Add(OverlayUi.SliderRow("Opacidade", _vm.LineOpacity, 0, 1, v => _vm.LineOpacity = v));
        stack.Children.Add(OverlayUi.Label("Cor da linha", secondary: true));
        stack.Children.Add(OverlayUi.SwatchRow(hex => _vm.LineColor = hex, () => _vm.LineColor));

        // Status
        var status = OverlayUi.Label("", secondary: true);
        status.FontSize = 12;
        status.Margin = new Thickness(0, 12, 0, 0);
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(GridPageViewModel.Status)));
        stack.Children.Add(status);

        Content = stack;
    }

    private static System.Windows.DependencyProperty Selector_SelectedItem()
        => System.Windows.Controls.Primitives.Selector.SelectedItemProperty;
}
