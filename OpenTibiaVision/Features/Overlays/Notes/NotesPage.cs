using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenTibiaVision.Features.Overlays.Notes;

/// <summary>
/// The Notes dashboard (master list + detail editor), built in code so it needs no XAML include.
/// The left list is note previews; the right panel edits the selected note. Built once and
/// kept alive; the shell toggles its Visibility on navigation (optimization principle 3).
/// </summary>
public sealed class NotesPage : UserControl
{
    private readonly NotesPageViewModel _vm;
    private readonly Border _editorHost;

    public NotesPage(NotesPageViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        Margin = new Thickness(16);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(BuildLeft());

        _editorHost = new Border
        {
            BorderBrush = OverlayUi.Brush("BorderBrush", "#FF333A47"),
            BorderThickness = new Thickness(1),
            Background = OverlayUi.Brush("SurfaceBrush", "#FF1B1F27"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
        };
        Grid.SetColumn(_editorHost, 2);
        grid.Children.Add(_editorHost);

        Content = grid;

        _vm.PropertyChanged += OnVmPropertyChanged;
        RebuildEditor();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotesPageViewModel.SelectedNote))
            RebuildEditor();
    }

    // ---- left column: actions + list + status ----

    private UIElement BuildLeft()
    {
        var panel = new DockPanel { LastChildFill = true };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(OverlayUi.Button("Nova nota", () => _vm.AddNoteCommand.Execute(null), accent: true));
        actions.Children.Add(OverlayUi.Button("Mostrar todas", () => _vm.ShowAllCommand.Execute(null)));
        actions.Children.Add(OverlayUi.Button("Ocultar todas", () => _vm.HideAllCommand.Execute(null)));
        DockPanel.SetDock(actions, Dock.Top);
        panel.Children.Add(actions);

        var status = OverlayUi.Label("", secondary: true);
        status.FontSize = 12;
        status.Margin = new Thickness(0, 8, 0, 0);
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(NotesPageViewModel.Status)));
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);

        var list = new ListBox
        {
            Background = OverlayUi.Brush("SurfaceAltBrush", "#FF232833"),
            Foreground = OverlayUi.Brush("TextPrimaryBrush", "#FFF3F5F9"),
            BorderBrush = OverlayUi.Brush("BorderBrush", "#FF333A47"),
            BorderThickness = new Thickness(1),
            FontFamily = OverlayUi.AppFont(),
            DisplayMemberPath = nameof(NoteRowViewModel.Preview),
        };
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(NotesPageViewModel.Notes)));
        list.SetBinding(Selector_SelectedItemProperty(), new Binding(nameof(NotesPageViewModel.SelectedNote)) { Mode = BindingMode.TwoWay });
        panel.Children.Add(list);

        return panel;
    }

    private static System.Windows.DependencyProperty Selector_SelectedItemProperty()
        => System.Windows.Controls.Primitives.Selector.SelectedItemProperty;

    // ---- right column: detail editor for the selected note ----

    private void RebuildEditor()
    {
        if (_vm.SelectedNote is not NoteRowViewModel row)
        {
            _editorHost.Child = OverlayUi.Label("Selecione ou crie uma nota para editar.", secondary: true);
            return;
        }

        var stack = new StackPanel();

        // action bar
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var lockBtn = OverlayUi.Button(row.LockButtonText, () => { });
        lockBtn.Click += (_, _) => { row.ToggleLock(); lockBtn.Content = row.LockButtonText; };
        var visBtn = OverlayUi.Button(row.VisibleButtonText, () => { });
        visBtn.Click += (_, _) =>
        {
            if (row.Visible) row.Hide(); else row.Show();
            visBtn.Content = row.VisibleButtonText;
        };
        var removeBtn = OverlayUi.Button("Remover", () => row.RemoveCommand.Execute(null));
        bar.Children.Add(lockBtn);
        bar.Children.Add(visBtn);
        bar.Children.Add(removeBtn);
        stack.Children.Add(bar);

        // text
        stack.Children.Add(OverlayUi.Header("Texto"));
        var textBox = new TextBox
        {
            Text = row.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            Margin = new Thickness(0, 0, 0, 12),
            Background = OverlayUi.Brush("SurfaceAltBrush", "#FF232833"),
            Foreground = OverlayUi.Brush("TextPrimaryBrush", "#FFF3F5F9"),
            BorderBrush = OverlayUi.Brush("BorderBrush", "#FF333A47"),
            FontFamily = OverlayUi.AppFont(),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        textBox.TextChanged += (_, _) => row.Text = textBox.Text;
        stack.Children.Add(textBox);

        // font family + size
        stack.Children.Add(OverlayUi.Header("Fonte"));
        var fontCombo = new ComboBox
        {
            ItemsSource = _vm.SystemFonts,
            SelectedItem = _vm.SystemFonts.Contains(row.FontFamily) ? row.FontFamily : null,
            Margin = new Thickness(0, 0, 0, 8),
        };
        fontCombo.SelectionChanged += (_, _) =>
        {
            if (fontCombo.SelectedItem is string f)
                row.FontFamily = f;
        };
        stack.Children.Add(fontCombo);
        stack.Children.Add(OverlayUi.SliderRow("Tamanho", row.FontSize, 8, 48, v => row.FontSize = v, "0"));

        // background colour + opacity
        stack.Children.Add(OverlayUi.Header("Fundo"));
        stack.Children.Add(OverlayUi.SwatchRow(hex => row.BackColor = hex, () => row.BackColor));
        stack.Children.Add(OverlayUi.SliderRow("Opacidade", row.BackOpacity, 0, 1, v => row.BackOpacity = v));

        // text colour + opacity
        stack.Children.Add(OverlayUi.Header("Cor do texto"));
        stack.Children.Add(OverlayUi.SwatchRow(hex => row.TextColor = hex, () => row.TextColor));
        stack.Children.Add(OverlayUi.SliderRow("Opacidade", row.TextOpacity, 0, 1, v => row.TextOpacity = v));

        _editorHost.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = stack,
        };
    }
}
