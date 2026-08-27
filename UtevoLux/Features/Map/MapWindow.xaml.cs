using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using UtevoLux.Core;
using UtevoLux.Features.Profiles;
using UtevoLux.UI;

namespace UtevoLux.Features.Map;

/// <summary>
/// The TibiaMaps window: a pan/zoom minimap viewer with floor rail, pins, multi-floor routes,
/// spawn clustering and type-ahead creature/NPC search. Clean-room reimplementation. <c>MapWindow</c> (2953 lines), with three deliberate adaptations to the fork:
///   * toasts / confirms / name prompts go through the fork's <see cref="IAppServices"/>,
///     <see cref="ThemedMessageBox"/> and <see cref="ProfileNameDialog"/> (instead of the
///     original's ToastService / ThemedMessageBox.ShowConfirm / RenameRegionDialog);
///   * multi-monitor DPI math (the original's DpiHelper) is simplified to
///     <see cref="SystemParameters.WorkArea"/>;
///   * chrome uses the fork's blue accent tokens. Route DIRECTION colors (orange same-floor,
///     green up, blue down) and the cyan spawn accent are kept exactly as the original.
/// </summary>
public partial class MapWindow : Window
{
    private enum SpriteKind { None, Creature, Npc }

    private const double MaxZoom = 16.0;
    private const double ZoomInStep = 1.25;
    private const double ZoomOutStep = 0.8;
    private const int PinIconId = 9;
    private const int NpcIconId = 6;
    private const int RareIconId = 12;

    private static readonly Color NpcPulseColor = Colors.White;
    private static readonly Color RarePulseColor = Color.FromRgb(224, 58, 58);

    private readonly IAppServices _services;
    private readonly MapTileIndex _tileIndex;
    private readonly FloorImageCache _floorCache;
    private readonly MapSettings _settings;
    private readonly IMarkerStore _markerStore;
    private readonly NpcDirectory _npcDirectory;
    private readonly RareCreatureDirectory _rareDirectory;
    // Not readonly: the "Atualizar criaturas (tibiaroute)" action rebuilds this in place after a
    // manual refresh so the creature search + reveal-on-map layers pick up the fresh dataset.
    private MonsterSpawnDirectory _spawnDirectory;
    private readonly IRouteStore _routeStore;

    private NpcEntry? _npcResult;
    private bool _suppressSearchChanged;
    private NpcEntry? _rareResult;
    private bool _suppressRareSearchChanged;
    private System.Threading.CancellationTokenSource? _lootCts;

    // Controllable clocks for the result-marker pulses (RepeatBehavior.Forever). Kept so the
    // pulses can be paused when the map is hidden or unfocused, otherwise they drive the GPU every
    // frame even while the user is in the game. Cleared whenever the marker layer is rebuilt.
    private readonly List<System.Windows.Media.Animation.ClockController> _pulseClocks = new();
    private bool _animPaused;

    // Animates multi-frame creature/item GIFs in the loot panel (WPF BitmapImage shows only frame 0).
    // Paused together with the marker pulses when the map is unfocused/hidden.
    private readonly GifAnimator _gif = new();

    private Canvas? _spawnClusterHost;
    private Path? _spawnDotBright;
    private Path? _spawnDotDim;
    private Path? _spawnDotGlow;
    private NpcEntry? _spawnClusterResult;
    private Color _spawnClusterColor;
    private Action? _spawnClusterClear;
    private DispatcherTimer? _spawnClusterRefreshTimer;
    private DateTime _lastSpawnClusterRefresh;

    private bool _iconPathSpriteMode;
    private bool _iconPathFloorOnly;
    private readonly List<Image> _iconPathSprites = new();

    private static readonly Color SpawnAccentColor = Color.FromRgb(0, 229, byte.MaxValue);

    private readonly List<RoutePoint> _routePoints = new();
    private Point _leftDownPoint;
    private bool _suppressRouteComboChanged;

    private int _currentFloor = 7;
    private readonly Button[] _floorButtons = new Button[16];

    private bool _isPanning;
    private Point _panLastPoint;

    private Canvas? _dragPinHost;
    private bool _pinDragMoved;
    private Point _pinDragStartCanvas;
    private bool _suppressNextRightUp;

    private readonly ScaleTransform _markerInverseScale = new(1.0, 1.0);

    private double _windowScale = 1.0;
    private bool _suppressScaleChanged;
    private static readonly double[] ScalePresets = { 1.0, 0.9, 0.8, 0.7, 0.6 };

    private const int DotRenderThreshold = 50;
    private const double ClusterCellScreenPx = 56.0;
    private const double SpriteZoomThreshold = 1.5;
    private const int MaxSpritesPerStack = 10;
    private const int PulseEverySpawnThreshold = 3;
    private const double CurrentFloorOnlyZoomThreshold = 2.86;

    private static readonly SolidColorBrush ClusterBadgeTextBrush = MakeFrozenBrush(6, 48, 59);

    private EventHandler? _flyToTick;
    private const double FlightPocketTiles = 30.0;

    private static readonly Brush RouteHaloBrush = Frozen(new SolidColorBrush(Color.FromArgb(190, 15, 17, 22)));
    private static readonly Brush RouteLineBrush = Frozen(new SolidColorBrush(Color.FromRgb(byte.MaxValue, 127, 0)));
    private static readonly Brush RouteDotBrush = RouteLineBrush;
    private static readonly Brush RouteStartBrush = Frozen(new SolidColorBrush(Color.FromRgb(46, 158, 68)));
    private static readonly Brush RouteUpBrush = Frozen(new SolidColorBrush(Color.FromRgb(46, 158, 68)));
    private static readonly Brush RouteDownBrush = Frozen(new SolidColorBrush(Color.FromRgb(47, 134, 224)));

    private Point _rightDownPoint;

    private bool RoutePlanMode => RoutePlanToggle.IsChecked == true;

    public MapWindow(IAppServices services)
    {
        _services = services;
        InitializeComponent();

        _settings = MapSettingsService.Load();
        _tileIndex = MapTileIndex.Load(MapTileIndex.ResolveTileDirectory());
        _floorCache = new FloorImageCache(_tileIndex);
        _markerStore = new JsonMarkerStore();
        _markerStore.MarkersChanged += delegate { RefreshMarkers(); };
        _npcDirectory = NpcDirectory.LoadDefault();
        _rareDirectory = RareCreatureDirectory.LoadDefault();
        _spawnDirectory = MonsterSpawnDirectory.LoadDefault();

        NpcSearchToggle.IsChecked = _settings.NpcSearchEnabled;
        ApplyNpcSearchVisibility();
        RareSearchToggle.IsChecked = _settings.RareSearchEnabled;
        ApplyRareSearchVisibility();
        PinsToggle.IsChecked = _settings.PinsPanelEnabled;
        PinsPanel.Visibility = _settings.PinsPanelEnabled ? Visibility.Visible : Visibility.Collapsed;
        RefreshPinsList();

        _routeStore = new JsonRouteStore();
        _routeStore.RoutesChanged += delegate { RefreshSavedRoutesCombo(); };
        RefreshSavedRoutesCombo();
        UpdateRouteCountLabel();
        PruneToSinglePin();

        MapCanvas.Width = Math.Max(_tileIndex.Bounds.Width, 1);
        MapCanvas.Height = Math.Max(_tileIndex.Bounds.Height, 1);
        BuildFloorRail();
        ApplyInitialWindowScale();
        RestoreWindowPosition();

        Loaded += MapWindow_Loaded;
        Closing += MapWindow_Closing;
        // Pause the forever-looping marker pulses whenever the map is not the focused, visible
        // window (user in the game / map hidden) so it stops repainting the GPU every frame.
        Activated += (_, _) => UpdateAnimationState();
        Deactivated += (_, _) => UpdateAnimationState();
        IsVisibleChanged += (_, _) => UpdateAnimationState();
        SizeChanged += delegate { ClampPan(); ApplyRailDensity(); };
        MainBorder.Loaded += delegate { UpdateContentClip(); };
        MainBorder.SizeChanged += delegate { UpdateContentClip(); };
    }

    // --------------------------------------------------------------------- work-area helper
    // The original resolved the monitor the window/point sat on via DpiHelper. Simplified here
    // to the primary work area (single-monitor). Functionally identical on one screen.
    private static Rect WorkArea() => SystemParameters.WorkArea;

    // --------------------------------------------------------------------- window scale
    private void ApplyInitialWindowScale()
    {
        double saved = _settings.WindowScale;
        _windowScale = ScalePresets.OrderBy(p => Math.Abs(p - saved)).First();
        WindowScale.ScaleX = WindowScale.ScaleY = _windowScale;
        _suppressScaleChanged = true;
        WindowScaleCombo.SelectedIndex = Array.IndexOf(ScalePresets, _windowScale);
        _suppressScaleChanged = false;
    }

    private void WindowScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings != null && !_suppressScaleChanged
            && WindowScaleCombo.SelectedItem is ComboBoxItem { Tag: string tag }
            && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            ApplyWindowScale(result);
        }
    }

    private void ApplyWindowScale(double requested)
    {
        double num = Math.Max(0.6, Math.Min(requested, 1.0));
        double factor = num / _windowScale;
        if (Math.Abs(factor - 1.0) < 0.001)
            return;

        double width = Width;
        double height = Height;
        _windowScale = num;
        WindowScale.ScaleX = WindowScale.ScaleY = num;
        MinWidth = 720.0 * num;
        MinHeight = 600.0 * num;
        double newW = Math.Max(MinWidth, width * factor);
        double newH = Math.Max(MinHeight, height * factor);
        double newLeft = Left + (width - newW) / 2.0;
        double newTop = Top + (height - newH) / 2.0;
        try
        {
            Rect wa = WorkArea();
            newW = Math.Min(newW, wa.Width);
            newH = Math.Min(newH, wa.Height);
            newLeft = Math.Max(wa.Left, Math.Min(newLeft, wa.Right - newW));
            newTop = Math.Max(wa.Top, Math.Min(newTop, wa.Bottom - newH));
        }
        catch { }
        Width = newW;
        Height = newH;
        Left = newLeft;
        Top = newTop;
    }

    private void UpdateContentClip()
    {
        if (MainBorder.ActualWidth <= 0.0 || MainBorder.ActualHeight <= 0.0)
            return;
        MainBorder.Clip = new RectangleGeometry(
            new Rect(0.0, 0.0, MainBorder.ActualWidth, MainBorder.ActualHeight), 16.0, 16.0);
    }

    private void RestoreWindowPosition()
    {
        Rect rect = WorkArea();
        double s = _windowScale;
        MinWidth = Math.Min(720.0 * s, Math.Max(480.0 * s, rect.Width - 40.0));
        MinHeight = Math.Min(600.0 * s, Math.Max(420.0 * s, rect.Height - 40.0));
        double wantW = _settings.WindowWidth ?? (1000.0 * s);
        double wantH = _settings.WindowHeight ?? (760.0 * s);
        Width = Math.Max(MinWidth, Math.Min(wantW, rect.Width - 20.0));
        Height = Math.Max(MinHeight, Math.Min(wantH, rect.Height - 20.0));
        if (_settings.WindowX.HasValue && _settings.WindowY.HasValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Max(rect.Left, Math.Min(_settings.WindowX.Value, rect.Right - Width));
            Top = Math.Max(rect.Top, Math.Min(_settings.WindowY.Value, rect.Bottom - Height));
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void MapWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_tileIndex.HasTiles)
        {
            CoordReadout.Text = "Map files not found";
            return;
        }
        MapScale.ScaleX = MapScale.ScaleY = FitZoom();
        UpdateMarkerInverseScale();
        UpdateZoomReadout();
        ClampPan();
        SetFloor(7);
    }

    private void MapWindow_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            MapSettings s = MapSettingsService.Load();
            s.WindowX = Left;
            s.WindowY = Top;
            s.WindowWidth = Width;
            s.WindowHeight = Height;
            s.WindowScale = _windowScale;
            MapSettingsService.Save(s);
        }
        catch { }
    }

    public void ForceClose() => Close();

    public void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        try
        {
            Rect wa = WorkArea();
            Width = Math.Max(MinWidth, Math.Min(Width + e.HorizontalChange * _windowScale, wa.Width));
            Height = Math.Max(MinHeight, Math.Min(Height + e.VerticalChange * _windowScale, wa.Height));
        }
        catch { }
    }

    // --------------------------------------------------------------------- floors
    private static string FloorLabel(int z)
    {
        if (z == 7) return "0";
        return z < 7 ? $"+{7 - z}" : $"-{z - 7}";
    }

    private void BuildFloorRail()
    {
        for (int i = 0; i < 16; i++)
        {
            int floor = i;
            var button = new Button
            {
                Style = (Style)FindResource("FloorButton"),
                Content = FloorLabel(floor),
                ToolTip = $"Floor {floor}" + (floor == 7 ? " (ground)" : "")
            };
            button.Click += delegate { SetFloor(floor); };
            _floorButtons[i] = button;
            FloorRail.Items.Add(button);
        }
    }

    private void ApplyRailDensity()
    {
        bool dense = MapViewport.ActualHeight > 0.0 && MapViewport.ActualHeight < 570.0;
        double height = dense ? 20 : 26;
        double fontSize = dense ? 10 : 12;
        foreach (Button button in _floorButtons)
        {
            if (button != null) { button.Height = height; button.FontSize = fontSize; }
        }
        FloorUpButton.Height = height; FloorUpButton.FontSize = fontSize;
        FloorDownButton.Height = height; FloorDownButton.FontSize = fontSize;
    }

    private void FloorUpButton_Click(object sender, RoutedEventArgs e) => SetFloor(_currentFloor - 1);
    private void FloorDownButton_Click(object sender, RoutedEventArgs e) => SetFloor(_currentFloor + 1);

    private void FloorRail_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetFloor(_currentFloor + (e.Delta <= 0 ? 1 : -1));
        e.Handled = true;
    }

    private async void SetFloor(int z) => await SetFloorAsync(z);

    private async Task SetFloorAsync(int z)
    {
        z = Math.Max(0, Math.Min(z, 15));
        _currentFloor = z;
        for (int i = 0; i < _floorButtons.Length; i++)
            _floorButtons[i].Tag = i == z ? "Selected" : null;
        FloorUpButton.IsEnabled = z > 0;
        FloorDownButton.IsEnabled = z < 15;
        if (!_tileIndex.HasTiles)
            return;

        Task<BitmapSource> floorAsync = _floorCache.GetFloorAsync(z);
        if (!floorAsync.IsCompleted)
            LoadingBadge.Visibility = Visibility.Visible;
        try
        {
            BitmapSource source = await floorAsync;
            if (_currentFloor == z)
            {
                FloorImage.Source = source;
                RefreshMarkers();
                RefreshRoute();
            }
        }
        catch { }
        finally
        {
            if (_currentFloor == z)
                LoadingBadge.Visibility = Visibility.Collapsed;
        }
    }

    // --------------------------------------------------------------------- pins
    private MapMarker? GetPin() =>
        _markerStore.GetAll().Where(m => !m.IsSaved).OrderByDescending(m => m.CreatedAt).FirstOrDefault();

    private List<MapMarker> GetSavedPins() =>
        _markerStore.GetAll().Where(m => m.IsSaved).OrderByDescending(m => m.CreatedAt).ToList();

    private void PruneToSinglePin()
    {
        try
        {
            var list = _markerStore.GetAll().Where(m => !m.IsSaved).ToList();
            if (list.Count <= 1)
                return;
            MapMarker keep = list.OrderByDescending(m => m.CreatedAt).First();
            foreach (MapMarker item in list.Where(m => m.Id != keep.Id))
                _markerStore.Remove(item.Id);
        }
        catch { }
    }

    private void PlacePinAt(int worldX, int worldY)
    {
        MapMarker? pin = GetPin();
        if (pin == null)
        {
            _markerStore.Add(new MapMarker { X = worldX, Y = worldY, Z = _currentFloor, Icon = PinIconId });
        }
        else
        {
            _markerStore.Update(new MapMarker
            {
                Id = pin.Id,
                X = worldX,
                Y = worldY,
                Z = _currentFloor,
                Icon = pin.Icon,
                Description = pin.Description,
                CreatedAt = pin.CreatedAt
            });
        }
    }

    private void SavePinButton_Click(object sender, RoutedEventArgs e)
    {
        MapMarker? pin = GetPin();
        if (pin == null)
            return;
        string? name = ProfileNameDialog.Prompt(this, "Name this pin",
            "What do you want to call this spot?", $"Pin {GetSavedPins().Count + 1}");
        if (string.IsNullOrWhiteSpace(name))
            return;
        _markerStore.Add(new MapMarker
        {
            X = pin.X,
            Y = pin.Y,
            Z = pin.Z,
            Icon = pin.Icon,
            Description = ShareCodeService.SanitizeText(name, MapMarker.MaxDescriptionLength),
            IsSaved = true
        });
        _markerStore.Remove(pin.Id);
        if (PinsToggle.IsChecked != true)
            PinsToggle.IsChecked = true;
        RefreshPinsList();
    }

    private void PinsToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool on = PinsToggle.IsChecked == true;
        PinsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
            RefreshPinsList();
        try
        {
            MapSettings s = MapSettingsService.Load();
            s.PinsPanelEnabled = on;
            MapSettingsService.Save(s);
        }
        catch { }
    }

    private void RefreshPinsList()
    {
        List<MapMarker> savedPins = GetSavedPins();
        PinsList.ItemsSource = savedPins;
        PinsEmptyHint.Visibility = savedPins.Count != 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private MapMarker? FindSavedPin(object sender)
    {
        object? tag = (sender as FrameworkElement)?.Tag;
        if (tag is Guid guid)
            return _markerStore.GetAll().FirstOrDefault(m => m.Id == guid);
        return null;
    }

    private async void SavedPin_GoTo(object sender, MouseButtonEventArgs e)
    {
        MapMarker? pin = FindSavedPin(sender);
        if (pin != null)
        {
            await SetFloorAsync(pin.Z);
            GoToWorld(pin.X, pin.Y);
        }
    }

    private void SavedPin_Rename(object sender, RoutedEventArgs e)
    {
        MapMarker? pin = FindSavedPin(sender);
        if (pin == null)
            return;
        string? name = ProfileNameDialog.Prompt(this, "Rename pin", "New name for this pin:", pin.Description);
        if (string.IsNullOrWhiteSpace(name))
            return;
        _markerStore.Update(new MapMarker
        {
            Id = pin.Id,
            X = pin.X,
            Y = pin.Y,
            Z = pin.Z,
            Icon = pin.Icon,
            Description = ShareCodeService.SanitizeText(name, MapMarker.MaxDescriptionLength),
            CreatedAt = pin.CreatedAt,
            IsSaved = true
        });
        RefreshPinsList();
    }

    private void SavedPin_Delete(object sender, RoutedEventArgs e)
    {
        MapMarker? pin = FindSavedPin(sender);
        if (pin != null)
        {
            _markerStore.Remove(pin.Id);
            RefreshPinsList();
        }
    }

    private void RenderSavedPins()
    {
        foreach (MapMarker savedPin in GetSavedPins())
        {
            bool onFloor = savedPin.Z == _currentFloor;
            var image = new Image { Width = 22.0, Height = 22.0, Source = MarkerIconProvider.GetIcon(savedPin.Icon) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(image, -11.0);
            Canvas.SetTop(image, -11.0);
            var label = new TextBlock
            {
                Text = savedPin.Description,
                Foreground = Brushes.White,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 0.0, BlurRadius = 4.0, Opacity = 0.9 }
            };
            Canvas.SetLeft(label, 13.0);
            Canvas.SetTop(label, -8.0);
            string toolTip = savedPin.Description + $"\n{savedPin.X}, {savedPin.Y}, {savedPin.Z}"
                + (onFloor ? "" : $"\n(this pin is on floor {savedPin.Z})");
            var canvas = new Canvas
            {
                Tag = savedPin.Id,
                RenderTransform = _markerInverseScale,
                Cursor = Cursors.Hand,
                Opacity = onFloor ? 1.0 : 0.45,
                ToolTip = toolTip
            };
            canvas.MouseLeftButtonDown += (s, ev) => { ev.Handled = true; SavedPin_GoTo(s, ev); };
            canvas.Children.Add(image);
            canvas.Children.Add(label);
            var (px, py) = _tileIndex.Bounds.WorldToPixel(savedPin.X, savedPin.Y);
            Canvas.SetLeft(canvas, Math.Max(0.5, Math.Min(px + 0.5, MapCanvas.Width - 0.5)));
            Canvas.SetTop(canvas, Math.Max(0.5, Math.Min(py + 0.5, MapCanvas.Height - 0.5)));
            MarkerLayer.Children.Add(canvas);
        }
    }

    private void RefreshMarkers()
    {
        MarkerLayer.Children.Clear();
        _pulseClocks.Clear();
        _iconPathSprites.Clear();
        _spawnClusterHost = null;
        _spawnDotBright = _spawnDotDim = _spawnDotGlow = null;
        _spawnClusterResult = null;
        _spawnClusterClear = null;

        MapMarker? pin = GetPin();
        CopyCodeButton.IsEnabled = pin != null;
        GoToPinButton.IsEnabled = pin != null;
        if (SavePinButton != null)
            SavePinButton.IsEnabled = pin != null;

        if (!_tileIndex.HasTiles)
            return;

        RenderSavedPins();
        RenderResultMarker(_rareResult, RareIconId, RarePulseColor,
            () => ClearRareResult(clearSearchBox: true), SpriteKind.Creature);
        RenderResultMarker(_npcResult, NpcIconId, NpcPulseColor,
            () => ClearNpcResult(clearSearchBox: true), SpriteKind.Npc);

        _iconPathSpriteMode = MapScale.ScaleX >= SpriteZoomThreshold;
        _iconPathFloorOnly = MapScale.ScaleX >= CurrentFloorOnlyZoomThreshold;

        if (pin != null && pin.Z == _currentFloor)
        {
            var image = new Image { Width = 24.0, Height = 24.0, Source = MarkerIconProvider.GetIcon(pin.Icon) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var canvas = new Canvas
            {
                Tag = pin.Id,
                RenderTransform = _markerInverseScale,
                Cursor = Cursors.Hand,
                ToolTip = string.IsNullOrWhiteSpace(pin.Description)
                    ? $"{pin.X}, {pin.Y}, {pin.Z}  ·  drag to move"
                    : $"{pin.Description}\n{pin.X}, {pin.Y}, {pin.Z}"
            };
            AddPinPulse(canvas);
            Canvas.SetLeft(image, -12.0);
            Canvas.SetTop(image, -12.0);
            canvas.Children.Add(image);
            canvas.MouseLeftButtonDown += Pin_MouseLeftButtonDown;
            canvas.MouseMove += Pin_MouseMove;
            canvas.MouseLeftButtonUp += Pin_MouseLeftButtonUp;
            canvas.MouseRightButtonDown += Pin_MouseRightButtonDown;
            var (px, py) = _tileIndex.Bounds.WorldToPixel(pin.X, pin.Y);
            Canvas.SetLeft(canvas, px + 0.5);
            Canvas.SetTop(canvas, py + 0.5);
            MarkerLayer.Children.Add(canvas);
        }
    }

    private void Pin_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Canvas canvas)
        {
            _dragPinHost = canvas;
            _pinDragMoved = false;
            _pinDragStartCanvas = e.GetPosition(MapCanvas);
            canvas.CaptureMouse();
        }
    }

    private void Pin_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPinHost == null || !_dragPinHost.IsMouseCaptured)
            return;
        Point position = e.GetPosition(MapCanvas);
        if (!_pinDragMoved)
        {
            double slop = 3.0 / Math.Max(MapScale.ScaleX, 0.0001);
            if (Math.Abs(position.X - _pinDragStartCanvas.X) < slop && Math.Abs(position.Y - _pinDragStartCanvas.Y) < slop)
                return;
            _pinDragMoved = true;
        }
        Canvas.SetLeft(_dragPinHost, position.X);
        Canvas.SetTop(_dragPinHost, position.Y);
    }

    private void Pin_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragPinHost == null)
            return;
        e.Handled = true;
        Canvas host = _dragPinHost;
        _dragPinHost = null;
        host.ReleaseMouseCapture();
        if (_pinDragMoved)
        {
            Point position = e.GetPosition(MapCanvas);
            int pixelX = Math.Max(0, Math.Min((int)Math.Floor(position.X), (int)MapCanvas.Width - 1));
            int pixelY = Math.Max(0, Math.Min((int)Math.Floor(position.Y), (int)MapCanvas.Height - 1));
            var (worldX, worldY) = _tileIndex.Bounds.PixelToWorld(pixelX, pixelY);
            PlacePinAt(worldX, worldY);
        }
    }

    private void Pin_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _suppressNextRightUp = true;
        MapMarker? pin = GetPin();
        if (pin != null)
            _markerStore.Remove(pin.Id);
    }

    /// <summary>
    /// Applies an animation as a controllable clock (instead of BeginAnimation) so the marker
    /// pulses can be paused/resumed. A forever-looping animation otherwise keeps the composition
    /// thread repainting every frame even when the map is unfocused or hidden.
    /// </summary>
    private void Animate(System.Windows.Media.Animation.IAnimatable target, DependencyProperty prop,
                         System.Windows.Media.Animation.AnimationTimeline anim)
    {
        var clock = anim.CreateClock();
        target.ApplyAnimationClock(prop, clock);
        if (clock.Controller is { } controller)
        {
            _pulseClocks.Add(controller);
            if (_animPaused)
                controller.Pause();
        }
    }

    /// <summary>Pause marker pulses while the window is hidden or not focused; resume otherwise.</summary>
    private void UpdateAnimationState()
    {
        bool shouldPause = !(IsVisible && IsActive);
        if (shouldPause == _animPaused)
            return;
        _animPaused = shouldPause;
        _gif.Paused = shouldPause;
        foreach (var controller in _pulseClocks)
        {
            try
            {
                if (shouldPause)
                    controller.Pause();
                else
                    controller.Resume();
            }
            catch
            {
                // a controller whose element was already torn down is harmless to skip
            }
        }
    }

    private void AddPinPulse(Canvas host) => AddPulse(host, Color.FromRgb(byte.MaxValue, 127, 0));

    private void AddPulse(Canvas host, Color accent, double sizeScale = 1.0)
    {
        var ellipse = new Ellipse
        {
            Width = 26.0 * sizeScale,
            Height = 26.0 * sizeScale,
            Fill = new SolidColorBrush(accent),
            Opacity = 0.2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(ellipse, -13.0 * sizeScale);
        Canvas.SetTop(ellipse, -13.0 * sizeScale);
        var animation = new DoubleAnimation(0.15, 0.5, TimeSpan.FromMilliseconds(900.0))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Animate(ellipse, UIElement.OpacityProperty, animation);
        host.Children.Add(ellipse);
        host.Children.Add(CreateRingPulse(TimeSpan.Zero, accent, sizeScale));
        host.Children.Add(CreateRingPulse(TimeSpan.FromMilliseconds(900.0), accent, sizeScale));
    }

    private Canvas CreateRingPulse(TimeSpan delay, Color accent, double sizeScale = 1.0)
    {
        TimeSpan dur = TimeSpan.FromMilliseconds(1800.0);
        var scale = new ScaleTransform(0.4, 0.4);
        Ellipse ring1 = MakeRing(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), 5.5);
        var accentBrush = new SolidColorBrush(accent);
        Ellipse ring2 = MakeRing(accentBrush, 3.0);
        var grow = new DoubleAnimation(0.4, 2.1, dur)
        {
            BeginTime = delay,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fade = new DoubleAnimationUsingKeyFrames { Duration = dur, BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.45)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
        var recolor = new ColorAnimation(accent, Colors.White, dur) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever };
        Animate(scale, ScaleTransform.ScaleXProperty, grow);
        Animate(scale, ScaleTransform.ScaleYProperty, grow);
        Animate(ring1, UIElement.OpacityProperty, fade);
        Animate(ring2, UIElement.OpacityProperty, fade);
        Animate(accentBrush, SolidColorBrush.ColorProperty, recolor);
        return new Canvas { IsHitTestVisible = false, Children = { ring1, ring2 } };

        Ellipse MakeRing(Brush stroke, double thickness)
        {
            var e = new Ellipse
            {
                Width = 30.0 * sizeScale,
                Height = 30.0 * sizeScale,
                Stroke = stroke,
                StrokeThickness = thickness,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
                Opacity = 0.0,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(e, -15.0 * sizeScale);
            Canvas.SetTop(e, -15.0 * sizeScale);
            return e;
        }
    }

    // --------------------------------------------------------------------- result markers
    private void RenderResultMarker(NpcEntry? result, int iconId, Color pulse, Action onClear, SpriteKind spriteKind = SpriteKind.None)
    {
        if (result == null)
            return;
        if (result.IsSpawnData || result.Positions.Count > DotRenderThreshold)
        {
            RenderSpawnDotLayer(result, SpawnAccentColor, onClear);
            return;
        }
        double scaleX = MapScale.ScaleX;
        ImageSource? sprite = scaleX < SpriteZoomThreshold ? null : spriteKind switch
        {
            SpriteKind.Creature => SpriteProvider.GetCreature(result.Name),
            SpriteKind.Npc => SpriteProvider.GetNpc(result.Name),
            _ => null,
        };
        bool floorOnly = spriteKind == SpriteKind.Creature && scaleX >= CurrentFloorOnlyZoomThreshold;
        foreach (NpcPosition position in result.Positions)
        {
            if (floorOnly && position.Z != _currentFloor)
                continue;
            double size = sprite != null ? SpriteSizeForZoom(scaleX) : 24.0;
            var image = new Image
            {
                Width = size,
                Height = size,
                Source = sprite ?? MarkerIconProvider.GetIcon(iconId)
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            if (sprite != null)
                _iconPathSprites.Add(image);
            bool onFloor = position.Z == _currentFloor;
            string text = result.Name;
            if (!string.IsNullOrEmpty(result.Location))
                text = text + "\n" + result.Location;
            text += $"\n{position.X}, {position.Y}, {position.Z}";
            if (position.SpawnTimeSeconds > 0)
                text = text + "\nrespawns ~" + FormatSpawnTime(position.SpawnTimeSeconds);
            if (!onFloor)
                text += $"\n(this spot is on floor {position.Z})";
            text += "\nRight-click to clear";
            var canvas = new Canvas
            {
                RenderTransform = _markerInverseScale,
                Opacity = onFloor ? 1.0 : 0.55,
                ToolTip = text
            };
            canvas.MouseLeftButtonDown += (s, ev) => ev.Handled = true;
            canvas.MouseRightButtonDown += (s, ev) => { ev.Handled = true; _suppressNextRightUp = true; onClear(); };
            AddPulse(canvas, pulse);
            Canvas.SetLeft(image, -size / 2.0);
            Canvas.SetTop(image, -size / 2.0);
            canvas.Children.Add(image);
            var (px, py) = _tileIndex.Bounds.WorldToPixel(position.X, position.Y);
            Canvas.SetLeft(canvas, Math.Max(0.5, Math.Min(px + 0.5, MapCanvas.Width - 0.5)));
            Canvas.SetTop(canvas, Math.Max(0.5, Math.Min(py + 0.5, MapCanvas.Height - 0.5)));
            MarkerLayer.Children.Add(canvas);
        }
    }

    private static string FormatSpawnTime(int seconds)
    {
        if (seconds >= 120 && seconds % 60 == 0)
            return $"{seconds / 60} min";
        return $"{seconds}s";
    }

    // --------------------------------------------------------------------- spawn cluster layer
    private void RenderSpawnDotLayer(NpcEntry result, Color color, Action onClear)
    {
        string tooltip = $"{result.Name} — {result.Positions.Count:N0} spawn points"
            + "\nDimmed circles are on other floors\nRight-click a circle to clear";
        var fill = new SolidColorBrush(color); fill.Freeze();
        var white = new SolidColorBrush(Colors.White); white.Freeze();
        _spawnClusterResult = result;
        _spawnClusterColor = color;
        _spawnClusterClear = onClear;
        _spawnClusterHost = new Canvas();
        _spawnDotGlow = MakeLayer(0.3, hitTestable: false);
        _spawnDotDim = MakeLayer(0.55, hitTestable: true);
        _spawnDotBright = MakeLayer(1.0, hitTestable: true);
        _spawnDotBright.Stroke = white;
        _spawnDotDim.Fill = null;
        _spawnDotDim.Stroke = fill;
        Animate(_spawnDotGlow, UIElement.OpacityProperty, new DoubleAnimation(0.15, 0.5, TimeSpan.FromMilliseconds(900.0))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
        _spawnClusterHost.Children.Add(_spawnDotGlow);
        _spawnClusterHost.Children.Add(_spawnDotDim);
        _spawnClusterHost.Children.Add(_spawnDotBright);
        RefreshSpawnClusters();
        MarkerLayer.Children.Add(_spawnClusterHost);

        Path MakeLayer(double opacity, bool hitTestable)
        {
            var path = new Path
            {
                Fill = fill,
                Opacity = opacity,
                IsHitTestVisible = hitTestable,
                ToolTip = hitTestable ? tooltip : null
            };
            if (hitTestable)
            {
                path.MouseLeftButtonDown += (s, ev) => ev.Handled = true;
                path.MouseRightButtonDown += (s, ev) => { ev.Handled = true; _suppressNextRightUp = true; onClear(); };
            }
            return path;
        }
    }

    private void RequestSpawnClusterRefresh()
    {
        if (_spawnClusterHost == null)
            return;
        if ((DateTime.UtcNow - _lastSpawnClusterRefresh).TotalMilliseconds > 300.0)
        {
            _spawnClusterRefreshTimer?.Stop();
            RefreshSpawnClusters();
            return;
        }
        if (_spawnClusterRefreshTimer == null)
        {
            _spawnClusterRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90.0) };
            _spawnClusterRefreshTimer.Tick += delegate { _spawnClusterRefreshTimer!.Stop(); RefreshSpawnClusters(); };
        }
        _spawnClusterRefreshTimer.Stop();
        _spawnClusterRefreshTimer.Start();
    }

    private void RefreshSpawnClusters()
    {
        if (_spawnClusterHost == null || _spawnClusterResult == null || _spawnDotBright == null
            || _spawnDotDim == null || _spawnDotGlow == null)
            return;
        double scaleX = MapScale.ScaleX;
        if (scaleX <= 0.0)
            return;
        _lastSpawnClusterRefresh = DateTime.UtcNow;
        while (_spawnClusterHost.Children.Count > 3)
            _spawnClusterHost.Children.RemoveAt(_spawnClusterHost.Children.Count - 1);

        bool spriteMode = scaleX >= SpriteZoomThreshold;
        ImageSource? sprite = spriteMode ? SpriteProvider.GetCreature(_spawnClusterResult.Name) : null;
        IReadOnlyList<NpcPosition> positions = scaleX >= CurrentFloorOnlyZoomThreshold
            ? _spawnClusterResult.Positions.Where(p => p.Z == _currentFloor).ToList()
            : _spawnClusterResult.Positions;
        double cell = scaleX >= 6.0 ? 20.0 : 56.0;
        List<SpawnClusterer.Cluster> clusters = SpawnClusterer.Build(positions, cell / scaleX);
        bool fewPoints = positions.Count <= 3;

        double vMinX = double.MinValue, vMinY = double.MinValue, vMaxX = double.MaxValue, vMaxY = double.MaxValue;
        if (MapViewport.ActualWidth > 0.0 && MapViewport.ActualHeight > 0.0)
        {
            double vw = MapViewport.ActualWidth / scaleX;
            double vh = MapViewport.ActualHeight / scaleX;
            vMinX = (-MapTranslate.X) / scaleX - vw / 2.0;
            vMinY = (-MapTranslate.Y) / scaleX - vh / 2.0;
            vMaxX = vMinX + vw * 2.0;
            vMaxY = vMinY + vh * 2.0;
        }
        double r1 = 7.0 / scaleX;
        double r2 = 5.0 / scaleX;
        double r3 = 13.0 / scaleX;
        _spawnDotBright.StrokeThickness = 1.5 / scaleX;
        _spawnDotDim.StrokeThickness = 1.5 / scaleX;
        var gBright = new GeometryGroup { FillRule = FillRule.Nonzero };
        var gDim = new GeometryGroup { FillRule = FillRule.Nonzero };
        var gGlow = new GeometryGroup { FillRule = FillRule.Nonzero };

        SpawnClusterer.Cluster? biggest = null;
        if (scaleX <= FitZoom() * 1.02)
        {
            foreach (SpawnClusterer.Cluster c in clusters)
                if (c.Count >= 2 && (biggest == null || c.Count > biggest.Count))
                    biggest = c;
        }
        foreach (SpawnClusterer.Cluster cluster in clusters)
        {
            var (cx, cy) = _tileIndex.Bounds.WorldToPixel((int)Math.Round(cluster.CenterX), (int)Math.Round(cluster.CenterY));
            if (cx < vMinX || cx > vMaxX || cy < vMinY || cy > vMaxY)
                continue;
            if (cluster.Count >= 2)
            {
                if (spriteMode && sprite != null && cluster.Count <= MaxSpritesPerStack)
                {
                    foreach (NpcPosition member in cluster.Members)
                        AddSpawnSprite(sprite, SpawnCenter(member), member.Z == _currentFloor, member);
                }
                else
                {
                    AddClusterBadge(cluster, cluster == biggest);
                }
                continue;
            }
            NpcPosition pos = cluster.Members[0];
            Point center = SpawnCenter(pos);
            if (spriteMode && sprite != null)
            {
                AddSpawnSprite(sprite, center, pos.Z == _currentFloor, pos, fewPoints);
            }
            else if (fewPoints)
            {
                AddPulsingSpawnDot(center, pos.Z == _currentFloor, pos);
            }
            else if (pos.Z == _currentFloor)
            {
                gBright.Children.Add(new EllipseGeometry(center, r1, r1));
                gGlow.Children.Add(new EllipseGeometry(center, r3, r3));
            }
            else
            {
                gDim.Children.Add(new EllipseGeometry(center, r2, r2));
            }
        }
        gBright.Freeze();
        gDim.Freeze();
        gGlow.Freeze();
        _spawnDotBright.Data = gBright;
        _spawnDotDim.Data = gDim;
        _spawnDotGlow.Data = gGlow;
    }

    private Point SpawnCenter(NpcPosition pos)
    {
        var (px, py) = _tileIndex.Bounds.WorldToPixel(pos.X, pos.Y);
        return new Point(
            Math.Max(0.5, Math.Min(px + 0.5, MapCanvas.Width - 0.5)),
            Math.Max(0.5, Math.Min(py + 0.5, MapCanvas.Height - 0.5)));
    }

    private static double SpriteSizeForZoom(double zoom) => Math.Clamp(8.0 * zoom, 32.0, 96.0);

    private void AddSpawnSprite(ImageSource sprite, Point center, bool onThisFloor, NpcPosition pos, bool pulse = false)
    {
        if (_spawnClusterHost == null || _spawnClusterResult == null)
            return;
        double size = SpriteSizeForZoom(MapScale.ScaleX);
        var image = new Image { Source = sprite, Width = size, Height = size, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(image, -size / 2.0);
        Canvas.SetTop(image, -size / 2.0);
        string text = $"{_spawnClusterResult.Name}\n{pos.X}, {pos.Y}, {pos.Z}";
        if (pos.SpawnTimeSeconds > 0)
            text = text + "\nrespawns ~" + FormatSpawnTime(pos.SpawnTimeSeconds);
        if (!onThisFloor)
            text += $"\n(this spot is on floor {pos.Z})";
        text += "\nRight-click to clear";
        var canvas = new Canvas
        {
            RenderTransform = _markerInverseScale,
            Opacity = onThisFloor ? 1.0 : 0.45,
            ToolTip = text
        };
        if (pulse)
            AddPulse(canvas, SpawnAccentColor);
        canvas.Children.Add(image);
        canvas.MouseLeftButtonDown += (s, ev) => ev.Handled = true;
        canvas.MouseRightButtonDown += (s, ev) => { ev.Handled = true; _suppressNextRightUp = true; _spawnClusterClear?.Invoke(); };
        Canvas.SetLeft(canvas, center.X);
        Canvas.SetTop(canvas, center.Y);
        _spawnClusterHost.Children.Add(canvas);
    }

    private void AddPulsingSpawnDot(Point center, bool onThisFloor, NpcPosition pos)
    {
        if (_spawnClusterHost == null || _spawnClusterResult == null)
            return;
        var accent = new SolidColorBrush(SpawnAccentColor); accent.Freeze();
        var dot = new Ellipse
        {
            Width = 14.0,
            Height = 14.0,
            Fill = accent,
            Stroke = Brushes.White,
            StrokeThickness = 2.0,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(dot, -7.0);
        Canvas.SetTop(dot, -7.0);
        string text = $"{_spawnClusterResult.Name}\n{pos.X}, {pos.Y}, {pos.Z}";
        if (pos.SpawnTimeSeconds > 0)
            text = text + "\nrespawns ~" + FormatSpawnTime(pos.SpawnTimeSeconds);
        if (!onThisFloor)
            text += $"\n(this spot is on floor {pos.Z})";
        text += "\nRight-click to clear";
        var canvas = new Canvas
        {
            RenderTransform = _markerInverseScale,
            Opacity = onThisFloor ? 1.0 : 0.5,
            ToolTip = text
        };
        AddPulse(canvas, SpawnAccentColor);
        canvas.Children.Add(dot);
        canvas.MouseLeftButtonDown += (s, ev) => ev.Handled = true;
        canvas.MouseRightButtonDown += (s, ev) => { ev.Handled = true; _suppressNextRightUp = true; _spawnClusterClear?.Invoke(); };
        Canvas.SetLeft(canvas, center.X);
        Canvas.SetTop(canvas, center.Y);
        _spawnClusterHost.Children.Add(canvas);
    }

    private static SolidColorBrush MakeFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void AddClusterBadge(SpawnClusterer.Cluster cluster, bool pulse)
    {
        if (_spawnClusterHost == null || _spawnClusterResult == null)
            return;
        int count = cluster.Count;
        double size = count >= 25 ? 42 : (count >= 10 ? 34 : 28);
        var fill = new SolidColorBrush(Color.FromArgb(224, _spawnClusterColor.R, _spawnClusterColor.G, _spawnClusterColor.B));
        fill.Freeze();
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Fill = fill, Stroke = Brushes.White, StrokeThickness = 2.0 });
        grid.Children.Add(new TextBlock
        {
            Text = count.ToString(),
            Foreground = ClusterBadgeTextBrush,
            FontWeight = FontWeights.Bold,
            FontSize = count >= 100 ? 11.5 : 12.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Canvas.SetLeft(grid, -size / 2.0);
        Canvas.SetTop(grid, -size / 2.0);
        var canvas = new Canvas
        {
            RenderTransform = _markerInverseScale,
            Cursor = Cursors.Hand,
            Opacity = cluster.AnyOnFloor(_currentFloor) ? 1.0 : 0.5,
            ToolTip = $"{_spawnClusterResult.Name} — {count} spawn points in this area"
                + "\nClick to zoom in · Right-click to clear"
        };
        if (pulse)
            AddPulse(canvas, SpawnAccentColor, size / 24.0);
        canvas.Children.Add(grid);
        canvas.MouseLeftButtonDown += async (s, ev) => { ev.Handled = true; await ZoomIntoCluster(cluster); };
        canvas.MouseRightButtonDown += (s, ev) => { ev.Handled = true; _suppressNextRightUp = true; _spawnClusterClear?.Invoke(); };
        var (cx, cy) = _tileIndex.Bounds.WorldToPixel((int)Math.Round(cluster.CenterX), (int)Math.Round(cluster.CenterY));
        Canvas.SetLeft(canvas, Math.Max(0.5, Math.Min(cx + 0.5, MapCanvas.Width - 0.5)));
        Canvas.SetTop(canvas, Math.Max(0.5, Math.Min(cy + 0.5, MapCanvas.Height - 0.5)));
        _spawnClusterHost.Children.Add(canvas);
    }

    private async Task ZoomIntoCluster(SpawnClusterer.Cluster cluster)
    {
        await SetFloorAsync(FlightFloor(cluster));
        var (minX, minY, maxX, maxY) = cluster.Bounds();
        double boxW = maxX - minX + 50;
        double boxH = maxY - minY + 50;
        double vw = MapViewport.ActualWidth;
        double vh = MapViewport.ActualHeight;
        double fit = (vw > 0.0 && vh > 0.0)
            ? Math.Min(vw / Math.Max(boxW, 1.0), vh / Math.Max(boxH, 1.0))
            : MapScale.ScaleX * 2.5;
        double target = Math.Max(MapScale.ScaleX * 1.5, Math.Min(fit, MaxZoom));
        target = Math.Max(FitZoom(), Math.Min(target, MaxZoom));
        FlyTo(target, (minX + maxX) / 2, (minY + maxY) / 2);
    }

    private void CancelFlyTo()
    {
        if (_flyToTick != null)
        {
            CompositionTarget.Rendering -= _flyToTick;
            _flyToTick = null;
        }
    }

    private void FlyTo(double targetZoom, int worldX, int worldY)
    {
        CancelFlyTo();
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        double s0 = MapScale.ScaleX;
        if (s0 <= 0.0 || MapViewport.ActualWidth <= 0.0 || MapViewport.ActualHeight <= 0.0)
            return;
        var (px, py) = _tileIndex.Bounds.WorldToPixel(worldX, worldY);
        double anchorX = px + 0.5;
        double anchorY = py + 0.5;
        Point startScreen = new(anchorX * s0 + MapTranslate.X, anchorY * s0 + MapTranslate.Y);
        var (endTx, endTy) = ClampedTranslate(
            MapViewport.ActualWidth / 2.0 - anchorX * targetZoom,
            MapViewport.ActualHeight / 2.0 - anchorY * targetZoom, targetZoom);
        Point endScreen = new(anchorX * targetZoom + endTx, anchorY * targetZoom + endTy);
        DateTime started = DateTime.UtcNow;
        _flyToTick = delegate
        {
            double t = Math.Min(1.0, (DateTime.UtcNow - started).TotalMilliseconds / 550.0);
            double smooth = t * t * (3.0 - 2.0 * t);
            double z = s0 * Math.Pow(targetZoom / s0, smooth);
            double sx = startScreen.X + (endScreen.X - startScreen.X) * smooth;
            double sy = startScreen.Y + (endScreen.Y - startScreen.Y) * smooth;
            MapScale.ScaleX = MapScale.ScaleY = z;
            MapTranslate.X = sx - anchorX * z;
            MapTranslate.Y = sy - anchorY * z;
            UpdateMarkerInverseScale();
            UpdateZoomReadout();
            if (t >= 1.0)
            {
                CancelFlyTo();
                ClampPan();
                RefreshSpawnClusters();
            }
        };
        CompositionTarget.Rendering += _flyToTick;
    }

    // --------------------------------------------------------------------- NPC search
    private void NpcSearchToggle_Changed(object sender, RoutedEventArgs e)
    {
        ApplyNpcSearchVisibility();
        try
        {
            MapSettings s = MapSettingsService.Load();
            s.NpcSearchEnabled = NpcSearchToggle.IsChecked == true;
            MapSettingsService.Save(s);
        }
        catch { }
    }

    private void ApplyNpcSearchVisibility()
    {
        bool on = NpcSearchToggle.IsChecked == true;
        NpcSearchPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            HideNpcResults();
            ClearNpcResult(clearSearchBox: true);
        }
    }

    private void NpcSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(NpcSearchBox.Text) && NpcResults.Visibility != Visibility.Visible)
            ShowNpcMatches(NpcSearchBox.Text);
    }

    private void NpcSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        NpcSearchHint.Visibility = string.IsNullOrEmpty(NpcSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_suppressSearchChanged)
            return;
        string text = NpcSearchBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            HideNpcResults();
            ClearNpcResult(clearSearchBox: false);
        }
        else
        {
            ShowNpcMatches(text);
        }
    }

    private void ShowNpcMatches(string query) => PopulateResults(_npcDirectory.Search(query), NpcResults);

    private void PopulateResults(IReadOnlyList<NpcEntry> matches, ListBox results)
    {
        results.Items.Clear();
        if (matches.Count == 0)
        {
            results.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (NpcEntry match in matches)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = match.Name,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            if (!string.IsNullOrEmpty(match.Location))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = match.Location,
                    FontSize = 10.5,
                    Foreground = (Brush)FindResource("SubtleTextColor"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }
            results.Items.Add(new ListBoxItem { Content = stack, Tag = match });
        }
        results.SelectedIndex = 0;
        results.Visibility = Visibility.Visible;
    }

    private bool HandleSearchKey(KeyEventArgs e, ListBox results, NpcEntry? current, Action<NpcEntry> pick,
        TextBox box, TextBlock hint, ref bool suppressFlag, Action hideResults, Action clearResult)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (results.Visibility == Visibility.Visible)
                {
                    results.SelectedIndex = Math.Min(results.SelectedIndex + 1, results.Items.Count - 1);
                    results.ScrollIntoView(results.SelectedItem);
                    return true;
                }
                break;
            case Key.Up:
                if (results.Visibility == Visibility.Visible)
                {
                    results.SelectedIndex = Math.Max(results.SelectedIndex - 1, 0);
                    results.ScrollIntoView(results.SelectedItem);
                    return true;
                }
                break;
            case Key.Return:
                if (results.Visibility == Visibility.Visible && results.SelectedItem is ListBoxItem { Tag: NpcEntry tag })
                    pick(tag);
                else if (current != null)
                    pick(current);
                return true;
            case Key.Escape:
                suppressFlag = true;
                box.Text = "";
                suppressFlag = false;
                hint.Visibility = Visibility.Visible;
                hideResults();
                clearResult();
                return true;
        }
        return false;
    }

    private void NpcSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleSearchKey(e, NpcResults, _npcResult, PickNpc, NpcSearchBox, NpcSearchHint,
                ref _suppressSearchChanged, HideNpcResults, () => ClearNpcResult(clearSearchBox: false)))
            e.Handled = true;
    }

    private void NpcResults_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (NpcResults.SelectedItem is ListBoxItem { Tag: NpcEntry tag })
            PickNpc(tag);
    }

    private async void PickNpc(NpcEntry npc)
    {
        _npcResult = npc;
        _suppressSearchChanged = true;
        NpcSearchBox.Text = npc.Name;
        _suppressSearchChanged = false;
        NpcSearchHint.Visibility = Visibility.Collapsed;
        HideNpcResults();
        RefreshMarkers();
        await GoToEntry(npc);
    }

    private async Task GoToEntry(NpcEntry entry)
    {
        if (entry.IsSpawnData)
        {
            await SetFloorAsync(DominantFloor(entry.Positions));
            ZoomToWholeMap();
        }
        else if (entry.Positions.Count == 1)
        {
            NpcPosition p = entry.Primary;
            await SetFloorAsync(p.Z);
            GoToWorld(p.X, p.Y);
        }
        else
        {
            await SetFloorAsync(DominantFloor(entry.Positions));
            FitViewToPositions(entry.Positions);
        }
    }

    private static int DominantFloor(IReadOnlyList<NpcPosition> positions) =>
        positions.GroupBy(p => p.Z).OrderByDescending(g => g.Count()).First().Key;

    private static int FlightFloor(SpawnClusterer.Cluster cluster) =>
        DominantFloor(SpawnClusterer.Build(cluster.Members, FlightPocketTiles)
            .OrderByDescending(p => p.Count).First().Members);

    private void ZoomToWholeMap()
    {
        double fit = FitZoom();
        if (fit <= 0.0)
            return;
        CancelFlyTo();
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        MapScale.ScaleX = MapScale.ScaleY = fit;
        ClampPan();
        UpdateMarkerInverseScale();
        UpdateZoomReadout();
    }

    // --------------------------------------------------------------------- rare/creature search
    private void RareSearchToggle_Changed(object sender, RoutedEventArgs e)
    {
        ApplyRareSearchVisibility();
        try
        {
            MapSettings s = MapSettingsService.Load();
            s.RareSearchEnabled = RareSearchToggle.IsChecked == true;
            MapSettingsService.Save(s);
        }
        catch { }
    }

    private void ApplyRareSearchVisibility()
    {
        bool on = RareSearchToggle.IsChecked == true;
        RareSearchPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on)
        {
            HideRareResults();
            ClearRareResult(clearSearchBox: true);
        }
    }

    private void RareSearchBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(RareSearchBox.Text) && RareResults.Visibility != Visibility.Visible)
            ShowRareMatches(RareSearchBox.Text);
    }

    private void RareSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RareSearchHint.Visibility = string.IsNullOrEmpty(RareSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (_suppressRareSearchChanged)
            return;
        if (string.IsNullOrWhiteSpace(RareSearchBox.Text))
        {
            HideRareResults();
            ClearRareResult(clearSearchBox: false);
        }
        else
        {
            ShowRareMatches(RareSearchBox.Text);
        }
    }

    private void ShowRareMatches(string query) => PopulateResults(SearchCreatures(query), RareResults);

    // The creature/rare search feeds off _spawnDirectory, which prefers the live tibiaroute.com
    // dataset (fetched once per launch in the background by MapModule) and otherwise the bundled
    // .dat. There is no manual-refresh UI; the once-per-launch auto fetch keeps it current.
    private IReadOnlyList<NpcEntry> SearchCreatures(string query, int max = 8)
    {
        string q = (query ?? "").Trim();
        if (q.Length == 0)
            return Array.Empty<NpcEntry>();
        return _rareDirectory.Search(q, max).Select(e => (Entry: e, Source: 0))
            .Concat(_spawnDirectory.Search(q, max).Select(e => (Entry: e, Source: 1)))
            .OrderBy(t => Tier(t.Entry.Name, q))
            .ThenBy(t => t.Source)
            .ThenBy(t => t.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Entry)
            .Take(max)
            .ToList();

        static int Tier(string name, string term)
        {
            if (name.Equals(term, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                return 1;
            return 2;
        }
    }

    private void RareSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleSearchKey(e, RareResults, _rareResult, PickRare, RareSearchBox, RareSearchHint,
                ref _suppressRareSearchChanged, HideRareResults, () => ClearRareResult(clearSearchBox: false)))
            e.Handled = true;
    }

    private void RareResults_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (RareResults.SelectedItem is ListBoxItem { Tag: NpcEntry tag })
            PickRare(tag);
    }

    private async void PickRare(NpcEntry creature)
    {
        _rareResult = creature;
        _suppressRareSearchChanged = true;
        RareSearchBox.Text = creature.Name;
        _suppressRareSearchChanged = false;
        RareSearchHint.Visibility = Visibility.Collapsed;
        HideRareResults();
        RefreshMarkers();
        ShowLootFor(creature.Name);
        await GoToEntry(creature);
    }

    private void HideRareResults()
    {
        RareResults.Visibility = Visibility.Collapsed;
        RareResults.Items.Clear();
    }

    private void ClearRareResult(bool clearSearchBox)
    {
        HideLoot();
        if (clearSearchBox)
        {
            _suppressRareSearchChanged = true;
            RareSearchBox.Text = "";
            _suppressRareSearchChanged = false;
            RareSearchHint.Visibility = Visibility.Visible;
        }
        if (_rareResult != null)
        {
            _rareResult = null;
            RefreshMarkers();
        }
    }

    // --------------------------------------------------------------------- creature loot panel
    // When a creature (rare boss or spawn) is picked, show its drops: names come from TibiaData
    // (CreatureLootProvider), each paired with an icon from our extracted Resources/items bank
    // (ItemSpriteProvider). Fully async + cancelable so rapidly switching creatures cancels the
    // previous lookup. Never blocks the UI thread and never throws.
    private async void ShowLootFor(string creatureName)
    {
        _lootCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _lootCts = cts;
        System.Threading.CancellationToken ct = cts.Token;

        LootCreatureName.Text = creatureName;
        string? creaturePath = SpriteProvider.GetCreaturePath(creatureName);
        if (creaturePath != null)
            _gif.Register(LootCreatureIcon, creaturePath);
        else
            LootCreatureIcon.Source = null;
        LootGrid.Children.Clear();
        LootStatus.Text = "carregando loot...";
        LootPanel.Visibility = Visibility.Visible;

        IReadOnlyList<string>? loot;
        try
        {
            loot = await CreatureLootProvider.Shared.GetLootNamesAsync(creatureName, ct);
        }
        catch
        {
            loot = null;
        }

        // A newer pick (or a close) superseded this lookup: drop the stale result.
        if (ct.IsCancellationRequested || !ReferenceEquals(_lootCts, cts))
            return;

        if (loot == null)
        {
            LootStatus.Text = "loot indisponivel agora (sem conexao?)";
            return;
        }
        if (loot.Count == 0)
        {
            LootStatus.Text = "sem loot conhecido na TibiaData";
            return;
        }

        int withIcon = 0;
        foreach (string itemName in loot)
        {
            string? path = ItemSpriteProvider.GetItemPath(itemName);
            if (path != null)
                withIcon++;
            LootGrid.Children.Add(BuildLootCell(itemName, path));
        }
        LootStatus.Text = $"{loot.Count} itens · {withIcon} com icone · fonte: TibiaData";
    }

    private void HideLoot()
    {
        _lootCts?.Cancel();
        _lootCts = null;
        if (LootPanel != null)
        {
            LootPanel.Visibility = Visibility.Collapsed;
            LootGrid.Children.Clear();
        }
    }

    private void LootCloseButton_Click(object sender, RoutedEventArgs e) => HideLoot();

    /// <summary>An icon cell (drop name in the tooltip), or a small text chip when we lack the icon.</summary>
    private FrameworkElement BuildLootCell(string itemName, string? path)
    {
        var cell = new Border
        {
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("SurfaceAltBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            ToolTip = itemName
        };
        if (path != null)
        {
            cell.Width = 40;
            cell.Height = 40;
            var img = new Image
            {
                Width = 32,
                Height = 32,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            _gif.Register(img, path); // sets first frame + animates if the gif has >1 frame
            cell.Child = img;
        }
        else
        {
            cell.Height = 22;
            cell.Padding = new Thickness(6, 2, 6, 2);
            cell.Child = new TextBlock
            {
                Text = itemName,
                FontSize = 10,
                MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecondaryBrush")
            };
        }
        return cell;
    }

    private void FitViewToPositions(IReadOnlyList<NpcPosition> positions) =>
        FitViewToBox(positions.Min(p => p.X), positions.Min(p => p.Y), positions.Max(p => p.X), positions.Max(p => p.Y));

    private void FitViewToBox(int minX, int minY, int maxX, int maxY)
    {
        minX -= 40; maxX += 40; minY -= 40; maxY += 40;
        double vw = MapViewport.ActualWidth;
        double vh = MapViewport.ActualHeight;
        if (vw <= 0.0 || vh <= 0.0)
            return;
        double fit = Math.Min(vw / Math.Max(maxX - minX, 1), vh / Math.Max(maxY - minY, 1));
        fit = Math.Max(FitZoom(), Math.Min(fit, 2.0));
        if (Math.Abs(MapScale.ScaleX - fit) > 0.0001)
        {
            MapScale.ScaleX = MapScale.ScaleY = fit;
            UpdateMarkerInverseScale();
            UpdateZoomReadout();
        }
        AnimatePanToWorld((minX + maxX) / 2, (minY + maxY) / 2);
    }

    private void HideNpcResults()
    {
        NpcResults.Visibility = Visibility.Collapsed;
        NpcResults.Items.Clear();
    }

    private void ClearNpcResult(bool clearSearchBox)
    {
        if (clearSearchBox)
        {
            _suppressSearchChanged = true;
            NpcSearchBox.Text = "";
            _suppressSearchChanged = false;
            NpcSearchHint.Visibility = Visibility.Visible;
        }
        if (_npcResult != null)
        {
            _npcResult = null;
            RefreshMarkers();
        }
    }

    private static Brush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    // --------------------------------------------------------------------- routes
    private void RoutePlanToggle_Changed(object sender, RoutedEventArgs e)
    {
        RoutePanel.Visibility = RoutePlanMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateRouteCountLabel();
    }

    private void AddRoutePoint(int worldX, int worldY)
    {
        if (_routePoints.Count >= MapRoute.MaxPoints)
        {
            _services.ShowToast($"Route limit reached ({MapRoute.MaxPoints} points)");
            return;
        }
        _routePoints.Add(new RoutePoint(worldX, worldY, _currentFloor));
        UpdateRouteCountLabel();
        RefreshRoute();
    }

    private void UpdateRouteCountLabel()
    {
        RouteCountLabel.Text = (RoutePlanMode || _routePoints.Count > 0)
            ? $"{_routePoints.Count}/{MapRoute.MaxPoints}" : "";
    }

    private void RefreshRoute()
    {
        RouteLayer.Children.Clear();
        if (_routePoints.Count == 0 || !_tileIndex.HasTiles)
            return;
        double scale = Math.Max(MapScale.ScaleX, 0.0001);
        MapBounds bounds = _tileIndex.Bounds;
        for (int i = 0; i + 1 < _routePoints.Count; i++)
        {
            RoutePoint a = _routePoints[i];
            RoutePoint b = _routePoints[i + 1];
            if (a.Z != _currentFloor && b.Z != _currentFloor)
                continue;
            Brush brush = a.Z == b.Z ? RouteLineBrush : (b.Z < a.Z ? RouteUpBrush : RouteDownBrush);
            var (ax, ay) = bounds.WorldToPixel(a.X, a.Y);
            var (bx, by) = bounds.WorldToPixel(b.X, b.Y);
            double x1 = ax + 0.5, y1 = ay + 0.5, x2 = bx + 0.5, y2 = by + 0.5;
            RouteLayer.Children.Add(MakeRouteLine(x1, y1, x2, y2, RouteHaloBrush, 4.5 / scale));
            Line line = MakeRouteLine(x1, y1, x2, y2, brush, 2.5 / scale);
            ApplyDirectionFlow(line);
            RouteLayer.Children.Add(line);
            double dx = x2 - x1, dy = y2 - y1;
            double minLen = 14.0 / scale;
            if (dx * dx + dy * dy >= minLen * minLen)
            {
                double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                RouteLayer.Children.Add(CreateSegmentArrow((x1 + x2) / 2.0, (y1 + y2) / 2.0, angle, brush));
            }
            if (a.Z != b.Z)
                RouteLayer.Children.Add(CreateFloorChangeLabel((x1 + x2) / 2.0, (y1 + y2) / 2.0, b.Z < a.Z ? "Up" : "Down", brush));
        }
        for (int j = 0; j < _routePoints.Count; j++)
        {
            RoutePoint p = _routePoints[j];
            if (p.Z != _currentFloor)
                continue;
            var dot = new Ellipse
            {
                Width = 9.0,
                Height = 9.0,
                Fill = j == 0 ? RouteStartBrush : RouteDotBrush,
                Stroke = RouteHaloBrush,
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            var canvas = new Canvas { RenderTransform = _markerInverseScale, IsHitTestVisible = false };
            Canvas.SetLeft(dot, -4.5);
            Canvas.SetTop(dot, -4.5);
            canvas.Children.Add(dot);
            var (px, py) = bounds.WorldToPixel(p.X, p.Y);
            Canvas.SetLeft(canvas, px + 0.5);
            Canvas.SetTop(canvas, py + 0.5);
            RouteLayer.Children.Add(canvas);
        }
    }

    private void ApplyDirectionFlow(Line line)
    {
        line.StrokeDashArray = new DoubleCollection { 3.0, 2.2 };
        var animation = new DoubleAnimation(0.0, -5.2, TimeSpan.FromMilliseconds(520.0)) { RepeatBehavior = RepeatBehavior.Forever };
        Animate(line, Shape.StrokeDashOffsetProperty, animation);
    }

    private UIElement CreateFloorChangeLabel(double mapX, double mapY, string text, Brush colour)
    {
        var border = new Border
        {
            Background = RouteHaloBrush,
            CornerRadius = new CornerRadius(4.0),
            Padding = new Thickness(5.0, 1.0, 5.0, 1.0),
            IsHitTestVisible = false,
            Child = new TextBlock { Text = text, FontSize = 10.0, FontWeight = FontWeights.Bold, Foreground = colour }
        };
        Canvas.SetLeft(border, 8.0);
        Canvas.SetTop(border, -18.0);
        var host = new Canvas { RenderTransform = _markerInverseScale, IsHitTestVisible = false, Children = { border } };
        Canvas.SetLeft(host, mapX);
        Canvas.SetTop(host, mapY);
        return host;
    }

    private static Line MakeRouteLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness) =>
        new()
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };

    private UIElement CreateSegmentArrow(double mapX, double mapY, double angleDegrees, Brush fill)
    {
        var arrow = new Polygon
        {
            Points = new PointCollection { new(-4.0, -4.5), new(5.0, 0.0), new(-4.0, 4.5) },
            Fill = fill,
            Stroke = RouteHaloBrush,
            StrokeThickness = 1.2,
            IsHitTestVisible = false
        };
        var rotated = new Canvas { RenderTransform = new RotateTransform(angleDegrees), IsHitTestVisible = false };
        rotated.Children.Add(arrow);
        var host = new Canvas { RenderTransform = _markerInverseScale, IsHitTestVisible = false, Children = { rotated } };
        Canvas.SetLeft(host, mapX);
        Canvas.SetTop(host, mapY);
        return host;
    }

    private void RouteSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_routePoints.Count < 2)
        {
            _services.ShowToast("Add at least 2 route points before saving");
            return;
        }
        var dialog = new RouteSaveDialog($"Route {DateTime.Now:MMM d, HH:mm}") { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _routeStore.Add(new MapRoute
            {
                Name = dialog.RouteName,
                Points = _routePoints.Select(p => new RoutePoint(p.X, p.Y, p.Z)).ToList()
            });
            _services.ShowToast("Route \"" + dialog.RouteName + "\" saved");
        }
    }

    private void RouteCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_routePoints.Count < 2)
        {
            _services.ShowToast("Add at least 2 route points to share");
            return;
        }
        string code = ShareCodeService.EncodeRoute(new MapRoute { Points = _routePoints });
        _services.ShowToast(TryCopyToClipboard(code) ? "Route code copied to clipboard" : "Couldn't access the clipboard - please try again");
    }

    private void RouteClearButton_Click(object sender, RoutedEventArgs e)
    {
        _routePoints.Clear();
        UpdateRouteCountLabel();
        RefreshRoute();
    }

    private void RefreshSavedRoutesCombo()
    {
        _suppressRouteComboChanged = true;
        SavedRoutesCombo.Items.Clear();
        foreach (MapRoute route in _routeStore.GetAll().OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            SavedRoutesCombo.Items.Add(new ComboBoxItem { Content = $"{route.Name} ({route.Points.Count})", Tag = route });
        SavedRoutesCombo.SelectedIndex = -1;
        _suppressRouteComboChanged = false;
        bool any = SavedRoutesCombo.Items.Count > 0;
        SavedRoutesCombo.IsEnabled = any;
        RouteDeleteButton.IsEnabled = any;
        UpdateRouteComboHint();
    }

    private void SavedRoutesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateRouteComboHint();
        if (!_suppressRouteComboChanged && SavedRoutesCombo.SelectedItem is ComboBoxItem { Tag: MapRoute tag })
            LoadRouteIntoPlanner(tag);
    }

    private void UpdateRouteComboHint()
    {
        if (SavedRoutesCombo.SelectedItem != null)
        {
            RouteComboHint.Visibility = Visibility.Collapsed;
            return;
        }
        int count = SavedRoutesCombo.Items.Count;
        RouteComboHint.Text = count > 0 ? $"Load route... ({count})" : "No saved routes";
        RouteComboHint.Visibility = Visibility.Visible;
    }

    private void RouteDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SavedRoutesCombo.SelectedItem is ComboBoxItem { Tag: MapRoute tag })
        {
            if (ThemedMessageBox.Show(this, "Delete Route", "Delete route \"" + tag.Name + "\"?",
                    ThemedMessageBox.Buttons.OkCancel) == ThemedMessageBox.Result.Ok)
                _routeStore.Remove(tag.Id);
        }
        else
        {
            _services.ShowToast("Select a saved route to delete");
        }
    }

    private async void LoadRouteIntoPlanner(MapRoute route)
    {
        RoutePlanToggle.IsChecked = true;
        _routePoints.Clear();
        _routePoints.AddRange(route.Points.Take(MapRoute.MaxPoints).Select(p => new RoutePoint(p.X, p.Y, p.Z)));
        UpdateRouteCountLabel();
        RefreshRoute();
        if (_routePoints.Count != 0)
        {
            await SetFloorAsync(_routePoints[0].Z);
            FitViewToBox(_routePoints.Min(p => p.X), _routePoints.Min(p => p.Y),
                _routePoints.Max(p => p.X), _routePoints.Max(p => p.Y));
        }
    }

    // --------------------------------------------------------------------- share codes
    private static bool TryCopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch
        {
            try { Clipboard.SetText(text); return true; }
            catch { return false; }
        }
    }

    private void CopyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        MapMarker? pin = GetPin();
        if (pin == null)
            return;
        string code = ShareCodeService.Encode(pin);
        _services.ShowToast(TryCopyToClipboard(code) ? "Pin code copied to clipboard" : "Couldn't access the clipboard - please try again");
    }

    private async void EnterCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ShareCodeEntryDialog(_tileIndex.Bounds) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        if (dialog.Route != null)
        {
            LoadRouteIntoPlanner(dialog.Route);
            _services.ShowToast("Route imported");
        }
        else if (dialog.Marker != null)
        {
            MapMarker marker = dialog.Marker;
            foreach (MapMarker item in _markerStore.GetAll().ToList())
                _markerStore.Remove(item.Id);
            _markerStore.Add(marker);
            await SetFloorAsync(marker.Z);
            GoToWorld(marker.X, marker.Y);
        }
    }

    private async void GoToPinButton_Click(object sender, RoutedEventArgs e)
    {
        MapMarker? pin = GetPin();
        if (pin != null)
        {
            await SetFloorAsync(pin.Z);
            GoToWorld(pin.X, pin.Y);
        }
    }

    private void GoToWorld(int worldX, int worldY)
    {
        double target = Math.Max(FitZoom(), Math.Min(2.0, MaxZoom));
        if (Math.Abs(MapScale.ScaleX - target) > 0.0001)
        {
            MapScale.ScaleX = MapScale.ScaleY = target;
            UpdateMarkerInverseScale();
            UpdateZoomReadout();
        }
        AnimatePanToWorld(worldX, worldY);
    }

    // --------------------------------------------------------------------- zoom / pan
    private double FitZoom()
    {
        if (MapViewport.ActualWidth <= 0.0 || MapViewport.ActualHeight <= 0.0)
            return 0.25;
        return Math.Min(MapViewport.ActualWidth / MapCanvas.Width, MapViewport.ActualHeight / MapCanvas.Height);
    }

    private void MapViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(MapViewport), e.Delta > 0 ? ZoomInStep : ZoomOutStep);
        e.Handled = true;
    }

    private void ZoomAt(Point viewportPoint, double factor)
    {
        CancelFlyTo();
        double scaleX = MapScale.ScaleX;
        double target = Math.Max(FitZoom(), Math.Min(scaleX * factor, MaxZoom));
        if (Math.Abs(target - scaleX) < 0.0001)
            return;
        MapTranslate.X = viewportPoint.X - (viewportPoint.X - MapTranslate.X) * (target / scaleX);
        MapTranslate.Y = viewportPoint.Y - (viewportPoint.Y - MapTranslate.Y) * (target / scaleX);
        MapScale.ScaleX = MapScale.ScaleY = target;
        ClampPan();
        UpdateMarkerInverseScale();
        UpdateZoomReadout();
    }

    private void UpdateMarkerInverseScale()
    {
        double scaleX = MapScale.ScaleX;
        if (scaleX <= 0.0)
            return;
        _markerInverseScale.ScaleX = _markerInverseScale.ScaleY = 1.0 / scaleX;
        RequestSpawnClusterRefresh();
        bool spriteMode = scaleX >= SpriteZoomThreshold;
        bool floorOnly = scaleX >= CurrentFloorOnlyZoomThreshold;
        if (((_rareResult != null && !_rareResult.IsSpawnData && _rareResult.Positions.Count <= DotRenderThreshold)
                || (_npcResult != null && _npcResult.Positions.Count <= DotRenderThreshold))
            && (spriteMode != _iconPathSpriteMode || floorOnly != _iconPathFloorOnly))
        {
            RefreshMarkers();
        }
        else
        {
            UpdateIconPathSpriteSizes();
        }
        RefreshRoute();
    }

    private void UpdateIconPathSpriteSizes()
    {
        if (_iconPathSprites.Count == 0)
            return;
        double size = SpriteSizeForZoom(MapScale.ScaleX);
        foreach (Image sprite in _iconPathSprites)
        {
            sprite.Width = size;
            sprite.Height = size;
            Canvas.SetLeft(sprite, -size / 2.0);
            Canvas.SetTop(sprite, -size / 2.0);
        }
    }

    private void UpdateZoomReadout() => ZoomReadout.Text = $"×{MapScale.ScaleX:0.0#}";

    private (double x, double y) ClampedTranslate(double tx, double ty) => ClampedTranslate(tx, ty, MapScale.ScaleX);

    private (double x, double y) ClampedTranslate(double tx, double ty, double s)
    {
        double vw = MapViewport.ActualWidth;
        double vh = MapViewport.ActualHeight;
        if (vw <= 0.0 || vh <= 0.0)
            return (tx, ty);
        double w = MapCanvas.Width * s;
        double h = MapCanvas.Height * s;
        double x = w >= vw ? Math.Max(vw - w, Math.Min(tx, 0.0)) : (vw - w) / 2.0;
        double y = h >= vh ? Math.Max(vh - h, Math.Min(ty, 0.0)) : (vh - h) / 2.0;
        return (x, y);
    }

    private void ClampPan()
    {
        RequestSpawnClusterRefresh();
        var (x, y) = ClampedTranslate(MapTranslate.X, MapTranslate.Y);
        MapTranslate.X = x;
        MapTranslate.Y = y;
    }

    private void MapViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled)
            return;
        CancelFlyTo();
        if (NpcResults.Visibility == Visibility.Visible)
            HideNpcResults();
        if (RareResults.Visibility == Visibility.Visible)
            HideRareResults();
        _leftDownPoint = e.GetPosition(MapViewport);
        _isPanning = true;
        _panLastPoint = _leftDownPoint;
        MapViewport.CaptureMouse();
        MapViewport.Cursor = Cursors.SizeAll;
    }

    private void MapViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndPan(e);

    private void EndPan(MouseButtonEventArgs? e)
    {
        if (!_isPanning)
            return;
        _isPanning = false;
        MapViewport.ReleaseMouseCapture();
        MapViewport.Cursor = Cursors.Arrow;
        if (!RoutePlanMode || e == null)
            return;
        Point position = e.GetPosition(MapViewport);
        if (Math.Abs(position.X - _leftDownPoint.X) <= 4.0 && Math.Abs(position.Y - _leftDownPoint.Y) <= 4.0)
        {
            var world = WorldAtViewportPoint(position);
            if (world.HasValue)
                AddRoutePoint(world.Value.x, world.Value.y);
        }
    }

    private void MapViewport_MouseMove(object sender, MouseEventArgs e)
    {
        Point position = e.GetPosition(MapViewport);
        if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
        {
            MapTranslate.X += position.X - _panLastPoint.X;
            MapTranslate.Y += position.Y - _panLastPoint.Y;
            _panLastPoint = position;
            ClampPan();
        }
        else if (_isPanning)
        {
            EndPan(null);
        }
        UpdateCoordReadout(position);
    }

    private void MapViewport_MouseLeave(object sender, MouseEventArgs e) => CoordReadout.Text = "—";

    private void MapViewport_MouseRightButtonDown(object sender, MouseButtonEventArgs e) =>
        _rightDownPoint = e.GetPosition(MapViewport);

    private void MapViewport_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressNextRightUp)
        {
            _suppressNextRightUp = false;
            return;
        }
        Point position = e.GetPosition(MapViewport);
        if (Math.Abs(position.X - _rightDownPoint.X) > 4.0 || Math.Abs(position.Y - _rightDownPoint.Y) > 4.0)
            return;
        if (RoutePlanMode)
        {
            if (_routePoints.Count > 0)
            {
                _routePoints.RemoveAt(_routePoints.Count - 1);
                UpdateRouteCountLabel();
                RefreshRoute();
            }
        }
        else
        {
            var world = WorldAtViewportPoint(position);
            if (world.HasValue)
                PlacePinAt(world.Value.x, world.Value.y);
        }
    }

    private void AnimatePanToWorld(int worldX, int worldY)
    {
        double scaleX = MapScale.ScaleX;
        var (px, py) = _tileIndex.Bounds.WorldToPixel(worldX, worldY);
        var (targetX, targetY) = ClampedTranslate(
            MapViewport.ActualWidth / 2.0 - (px + 0.5) * scaleX,
            MapViewport.ActualHeight / 2.0 - (py + 0.5) * scaleX);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var animX = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(320.0)) { EasingFunction = ease };
        var animY = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(320.0)) { EasingFunction = ease };
        animX.Completed += delegate
        {
            MapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            MapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            MapTranslate.X = targetX;
            MapTranslate.Y = targetY;
            ClampPan();
        };
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, animX);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, animY);
    }

    private (int x, int y)? WorldAtViewportPoint(Point viewportPoint)
    {
        double scaleX = MapScale.ScaleX;
        if (scaleX <= 0.0)
            return null;
        double fx = (viewportPoint.X - MapTranslate.X) / scaleX;
        double fy = (viewportPoint.Y - MapTranslate.Y) / scaleX;
        int px = (int)Math.Floor(fx);
        int py = (int)Math.Floor(fy);
        if (px < 0 || py < 0 || px >= (int)MapCanvas.Width || py >= (int)MapCanvas.Height)
            return null;
        return _tileIndex.Bounds.PixelToWorld(px, py);
    }

    private void UpdateCoordReadout(Point viewportPoint)
    {
        var world = WorldAtViewportPoint(viewportPoint);
        CoordReadout.Text = world.HasValue ? $"{world.Value.x}, {world.Value.y}, {_currentFloor}" : "—";
    }

    private void CenterOnWorld(int worldX, int worldY)
    {
        double scaleX = MapScale.ScaleX;
        var (px, py) = _tileIndex.Bounds.WorldToPixel(worldX, worldY);
        MapTranslate.X = MapViewport.ActualWidth / 2.0 - (px + 0.5) * scaleX;
        MapTranslate.Y = MapViewport.ActualHeight / 2.0 - (py + 0.5) * scaleX;
        ClampPan();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled
            || (NpcSearchBox != null && NpcSearchBox.IsKeyboardFocusWithin)
            || (RareSearchBox != null && RareSearchBox.IsKeyboardFocusWithin))
            return;
        switch (e.Key)
        {
            case Key.Prior:
                SetFloor(_currentFloor - 1);
                e.Handled = true;
                break;
            case Key.Next:
                SetFloor(_currentFloor + 1);
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                ZoomAt(new Point(MapViewport.ActualWidth / 2.0, MapViewport.ActualHeight / 2.0), ZoomInStep);
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomAt(new Point(MapViewport.ActualWidth / 2.0, MapViewport.ActualHeight / 2.0), ZoomOutStep);
                e.Handled = true;
                break;
        }
    }
}
