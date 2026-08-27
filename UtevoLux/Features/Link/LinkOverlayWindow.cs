using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using UtevoLux.Core;
using UtevoLux.Services;
using UtevoLux.UI;

namespace UtevoLux.Features.Link;

/// <summary>
/// The always-on-top, click-through party-status HUD. Mirrors the original
/// <c>WindowReplicaApp.Views.LinkOverlayWindow</c>: a compact card listing each member with a
/// coloured presence dot, click-through + no-activate while locked, draggable while unlocked, with
/// a right-click menu to copy the code or leave. Built in code (no XAML resource) following the
/// fork's overlay convention (see <c>UI/Toast.cs</c>), themed via <see cref="ThemeAccess"/>, and
/// made click-through through <see cref="IWindowService"/> — no raw Win32 ex-style pokes here.
/// </summary>
public sealed class LinkOverlayWindow : Window
{
    private readonly IAppServices _services;
    private readonly LinkViewModel _viewModel;

    private readonly Grid _rootGrid;
    private readonly Border _linkCard;
    private readonly Border _selectionBorder;
    private readonly TextBlock _headerText;
    private readonly StackPanel _membersPanel;

    // WS_EX_TRANSPARENT only — the click-through bit. We toggle THIS alone (never WS_EX_LAYERED,
    // which WPF owns while AllowsTransparency is on) so unlocking the overlay to drag it doesn't
    // tear down its transparency. Matches how the original only flipped the transparent/no-activate
    // bits, leaving the layered style intact.
    private const long WS_EX_TRANSPARENT = 0x20;

    private IntPtr _hwnd;
    private bool _isDragging;
    private Point _dragStart;

    public LinkOverlayWindow(IAppServices services, LinkViewModel viewModel)
    {
        _services = services;
        _viewModel = viewModel;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;

        _headerText = new TextBlock
        {
            Foreground = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9"),
            FontFamily = ThemeAccess.Font("Font.Display", "Segoe UI Semibold"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };

        _membersPanel = new StackPanel();

        var content = new StackPanel();
        content.Children.Add(_headerText);
        content.Children.Add(_membersPanel);

        _linkCard = new Border
        {
            BorderBrush = ThemeAccess.Brush("BorderStrongBrush", "#FF454E5E"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            MinWidth = 160,
            Child = content
        };

        // Dashed selection frame shown only when unlocked, to signal the draggable region.
        _selectionBorder = new Border
        {
            BorderBrush = ThemeAccess.Brush("AccentBrush", "#FF3FA9F5"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(-3),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        _rootGrid = new Grid();
        _rootGrid.Children.Add(_selectionBorder);
        _rootGrid.Children.Add(_linkCard);
        Content = _rootGrid;

        BuildContextMenu();

        if (_viewModel.X > 0.0 || _viewModel.Y > 0.0)
        {
            Left = _viewModel.X;
            Top = _viewModel.Y;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;

        _viewModel.Members.CollectionChanged += OnMembersChanged;
        HookMemberNotifications();

        Loaded += (_, _) =>
        {
            RebuildMembers();
            ApplyContent();
            ApplyLockState();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // Tool-window + no-activate so the HUD never appears in Alt+Tab and never steals game focus.
        _services.Windows.SetOverlayChrome(_hwnd, true);
        ApplyLockState();
    }

    // ---- content ----

    /// <summary>Apply visibility, scale and background opacity from the view-model state.</summary>
    public void ApplyContent()
    {
        // With no members: keep the card visible only while unlocked (so it can be positioned);
        // once locked an empty party collapses to nothing.
        _linkCard.Visibility = (_viewModel.Members.Count == 0 && _viewModel.Locked)
            ? Visibility.Collapsed
            : Visibility.Visible;

        double scale = _viewModel.Scale > 0.0 ? _viewModel.Scale : 1.0;
        _rootGrid.LayoutTransform = scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);

        byte a = (byte)(Math.Clamp(_viewModel.BackgroundOpacity, 0.0, 1.0) * 255.0);
        _linkCard.Background = new SolidColorBrush(Color.FromArgb(a, 21, 24, 33));

        _headerText.Text = string.IsNullOrEmpty(_viewModel.PartyCode)
            ? "TibiaVision Link"
            : $"Party {_viewModel.PartyCode}";

        Opacity = 1.0;
    }

    /// <summary>Apply lock state: click-through + hidden selection frame when locked.</summary>
    public void ApplyLockState()
    {
        if (_hwnd == IntPtr.Zero)
            return; // deferred until OnSourceInitialized

        // Locked -> click-through (mouse falls to the game); unlocked -> hit-testable so it can be
        // dragged. Toggle ONLY the transparent bit; WS_EX_LAYERED stays (WPF AllowsTransparency).
        WindowFinder.SetExStyle(_hwnd, WS_EX_TRANSPARENT, _viewModel.Locked);
        _selectionBorder.Visibility = _viewModel.Locked ? Visibility.Collapsed : Visibility.Visible;
        Topmost = true;
        ApplyContent();
    }

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.Invoke(() =>
        {
            HookMemberNotifications();
            RebuildMembers();
            ApplyContent();
        });

    private void HookMemberNotifications()
    {
        foreach (PartyMember m in _viewModel.Members)
        {
            m.PropertyChanged -= OnMemberPropertyChanged;
            m.PropertyChanged += OnMemberPropertyChanged;
        }
    }

    private void OnMemberPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => Dispatcher.Invoke(RebuildMembers);

    private void RebuildMembers()
    {
        _membersPanel.Children.Clear();
        var nameBrush = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");
        var statusBrush = ThemeAccess.Brush("TextSecondaryBrush", "#FF9AA4B3");
        var font = ThemeAccess.Font("Font.App", "Segoe UI");

        foreach (PartyMember member in _viewModel.Members)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Ellipse
            {
                Width = 9,
                Height = 9,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = PartyStatusToBrushConverter.BrushFor(member.Status)
            };
            Grid.SetColumn(dot, 0);

            var name = new TextBlock
            {
                Text = member.Name,
                Foreground = nameBrush,
                FontFamily = font,
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(name, 1);

            var status = new TextBlock
            {
                Text = member.StatusText,
                Foreground = statusBrush,
                FontFamily = font,
                FontSize = 11,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(status, 2);

            row.Children.Add(dot);
            row.Children.Add(name);
            row.Children.Add(status);
            _membersPanel.Children.Add(row);
        }
    }

    // ---- drag (unlocked only) ----

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Locked)
            return;
        _isDragging = true;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || !IsMouseCaptured)
            return;

        // No-activate windows can't rely on DragMove; move manually. Compute the delta in physical
        // px (PointToScreen) and convert to DIPs with the owning monitor's scale before nudging
        // Left/Top (which are DIPs), so a mixed-DPI drag tracks the cursor exactly.
        Point cur = PointToScreen(e.GetPosition(this));
        Point start = PointToScreen(_dragStart);
        double scale = _services.Dpi.GetScaleForWindow(_hwnd);
        Left += _services.Dpi.ToDip(cur.X - start.X, scale);
        Top += _services.Dpi.ToDip(cur.Y - start.Y, scale);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;
        _isDragging = false;
        ReleaseMouseCapture();
        _viewModel.X = Left;
        _viewModel.Y = Top;
        _viewModel.NotifySettingsChanged();
    }

    // ---- context menu ----

    private void BuildContextMenu()
    {
        var fg = ThemeAccess.Brush("TextPrimaryBrush", "#FFF3F5F9");
        var bg = ThemeAccess.Brush("SurfaceAltBrush", "#FF232833");
        var menu = new ContextMenu { Background = bg, Foreground = fg };

        var copy = new MenuItem { Header = "Copiar codigo da party", Foreground = fg };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_viewModel.PartyCode))
            {
                try { Clipboard.SetText(_viewModel.PartyCode); } catch { /* clipboard busy */ }
            }
        };
        menu.Items.Add(copy);
        menu.Items.Add(new Separator());

        var leave = new MenuItem { Header = "Sair da party", Foreground = fg };
        leave.Click += async (_, _) => await _viewModel.LeavePartyAsync();
        menu.Items.Add(leave);

        ContextMenu = menu;
    }

    /// <summary>Persist the current position (called on shutdown before the window is torn down).</summary>
    public void SavePosition()
    {
        _viewModel.X = Left;
        _viewModel.Y = Top;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Members.CollectionChanged -= OnMembersChanged;
        foreach (PartyMember m in _viewModel.Members)
            m.PropertyChanged -= OnMemberPropertyChanged;
        base.OnClosed(e);
    }
}
