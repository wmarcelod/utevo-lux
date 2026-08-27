using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace UtevoLux.Features.Overlays.Marker;

/// <summary>The Marker dashboard, built in code. Built once, kept alive.</summary>
public sealed class MarkerPage : UserControl
{
    private readonly MarkerPageViewModel _vm;

    public MarkerPage(MarkerPageViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        Margin = new Thickness(16);

        var stack = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };

        stack.Children.Add(OverlayUi.Header("Marcador de localizacao"));
        stack.Children.Add(OverlayUi.Label(
            "Decoracao passiva: fica onde voce estaciona e NAO segue o personagem. Destrave para arrastar, trave para fixar (click-through).",
            secondary: true));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) };
        actions.Children.Add(OverlayUi.Button("Mostrar", () => _vm.ShowCommand.Execute(null), accent: true));
        actions.Children.Add(OverlayUi.Button("Ocultar", () => _vm.HideCommand.Execute(null)));
        var lockBtn = OverlayUi.Button(_vm.LockButtonText, () => { });
        lockBtn.MinWidth = 160;
        lockBtn.Click += (_, _) => { _vm.ToggleLockCommand.Execute(null); lockBtn.Content = _vm.LockButtonText; };
        actions.Children.Add(lockBtn);
        stack.Children.Add(actions);

        // Shape picker
        stack.Children.Add(OverlayUi.Header("Forma"));
        var shapes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        shapes.Children.Add(OverlayUi.Button("Circulo", () => _vm.Shape = "circle"));
        shapes.Children.Add(OverlayUi.Button("Seta", () => _vm.Shape = "arrow"));
        stack.Children.Add(shapes);

        stack.Children.Add(OverlayUi.Header("Aparencia"));
        stack.Children.Add(OverlayUi.SliderRow("Tamanho", _vm.Size, 16, 120, v => _vm.Size = v, "0"));
        stack.Children.Add(OverlayUi.SliderRow("Opacidade", _vm.Opacity, 0, 1, v => _vm.Opacity = v));
        stack.Children.Add(OverlayUi.Label("Cor", secondary: true));
        stack.Children.Add(OverlayUi.SwatchRow(hex => _vm.Color = hex, () => _vm.Color));

        var status = OverlayUi.Label("", secondary: true);
        status.FontSize = 12;
        status.Margin = new Thickness(0, 12, 0, 0);
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(MarkerPageViewModel.Status)));
        stack.Children.Add(status);

        Content = stack;
    }
}
