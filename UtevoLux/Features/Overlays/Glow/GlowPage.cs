using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace UtevoLux.Features.Overlays.Glow;

/// <summary>The Cursor-Glow dashboard, built in code. Built once, kept alive.</summary>
public sealed class GlowPage : UserControl
{
    private readonly GlowPageViewModel _vm;

    public GlowPage(GlowPageViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        Margin = new Thickness(16);

        var stack = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };

        stack.Children.Add(OverlayUi.Header("Brilho do cursor"));
        stack.Children.Add(OverlayUi.Label(
            "Um anel de tres circulos concentricos que segue o cursor (decoracao, sempre click-through).",
            secondary: true));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 16) };
        actions.Children.Add(OverlayUi.Button("Ativar", () => _vm.EnableCommand.Execute(null), accent: true));
        actions.Children.Add(OverlayUi.Button("Desativar", () => _vm.DisableCommand.Execute(null)));
        stack.Children.Add(actions);

        stack.Children.Add(OverlayUi.Header("Aparencia"));
        stack.Children.Add(OverlayUi.SliderRow("Tamanho", _vm.OuterSize, 24, 160, v => _vm.OuterSize = v, "0"));
        stack.Children.Add(OverlayUi.SliderRow("Espessura", _vm.Thickness, 1, 8, v => _vm.Thickness = v));
        stack.Children.Add(OverlayUi.SliderRow("Opacidade", _vm.Opacity, 0, 1, v => _vm.Opacity = v));
        stack.Children.Add(OverlayUi.Label("Cor", secondary: true));
        stack.Children.Add(OverlayUi.SwatchRow(hex => _vm.Color = hex, () => _vm.Color));

        var status = OverlayUi.Label("", secondary: true);
        status.FontSize = 12;
        status.Margin = new Thickness(0, 12, 0, 0);
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(GlowPageViewModel.Status)));
        stack.Children.Add(status);

        Content = stack;
    }
}
