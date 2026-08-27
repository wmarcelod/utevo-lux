using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UtevoLux.Core;
using UtevoLux.Services;

namespace UtevoLux.Features.Overlays.Notes;

/// <summary>
/// A floating sticky note: a Border + TextBlock over the game (spec). Background and text
/// opacity are independent (two baked frozen brushes). Locked = click-through; unlocked shows a
/// selection border, is draggable (base class) and has a corner resize grip. A locked note with
/// no text COLLAPSES (hidden) so empty notes never clutter the screen.
///
/// The note is display-only: it is WS_EX_NOACTIVATE and therefore cannot take keyboard focus,
/// so all text/colour/font editing lives in the dashboard page, not in this window.
/// </summary>
public sealed class NoteWindow : ClickThroughOverlayWindow
{
    private readonly NoteConfig _config;

    private readonly Border _selection;   // selection chrome (visible only when unlocked)
    private readonly Border _card;         // painted background (independent opacity)
    private readonly TextBlock _text;      // note text (independent opacity)
    private readonly Thumb _resizeGrip;

    private RECT _resizeAnchorBounds;

    public NoteWindow(IAppServices services, NoteConfig config) : base(services)
    {
        _config = config;

        _text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Top,
        };

        _card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Child = _text,
        };

        _resizeGrip = BuildResizeGrip();

        var grid = new Grid();
        grid.Children.Add(_card);
        grid.Children.Add(_resizeGrip);

        _selection = new Border
        {
            BorderBrush = OverlayUi.Brush("AccentBrush", "#FF3FA9F5"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Child = grid,
        };

        Content = _selection;
        Width = 240;   // provisional; real placement is physical via base InitialPlacementPhysical
        Height = 140;

        // Paint only during construction; visibility (collapse) is evaluated after the window
        // is shown, so we never call Show() from inside the constructor.
        RepaintContent();
    }

    public NoteConfig Config => _config;

    protected override RECT InitialPlacementPhysical
        => new(_config.Left, _config.Top, _config.Left + _config.Width, _config.Top + _config.Height);

    // ---- style / content ----

    /// <summary>Re-read the config and repaint (colours, opacity, font, text) + collapse rule.</summary>
    public void ApplyStyle()
    {
        RepaintContent();
        UpdateCollapse();
    }

    /// <summary>Repaint colours / opacity / font / text WITHOUT touching visibility.</summary>
    private void RepaintContent()
    {
        _card.Background = OverlayColor.FrozenBrush(
            _config.BackColor, _config.BackOpacity, Color.FromArgb(0xFF, 0x2C, 0x33, 0x40));

        _text.Foreground = OverlayColor.FrozenBrush(
            _config.TextColor, _config.TextOpacity, Color.FromArgb(0xFF, 0xF3, 0xF5, 0xF9));

        _text.Text = _config.Text;
        _text.FontSize = _config.FontSize;
        _text.FontFamily = string.IsNullOrWhiteSpace(_config.FontFamily)
            ? OverlayUi.AppFont()
            : new FontFamily(_config.FontFamily);
    }

    /// <summary>Re-evaluate visibility only (cheap: no brush rebuild). Used by show/hide/lock.</summary>
    public void RefreshVisibility() => UpdateCollapse();

    /// <summary>A locked, empty note collapses (hidden). Otherwise it follows config.Visible.</summary>
    private void UpdateCollapse()
    {
        bool empty = string.IsNullOrWhiteSpace(_config.Text);
        bool shouldShow = _config.Visible && !(IsLocked && empty);

        if (shouldShow && !IsVisible)
            Show();
        else if (!shouldShow && IsVisible)
            Hide();
    }

    protected override void OnLockChanged(bool locked)
    {
        _selection.BorderThickness = new Thickness(locked ? 0 : 2);
        _resizeGrip.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        _config.Locked = locked;
        UpdateCollapse();
    }

    // ---- resize grip (unlocked only) ----

    private Thumb BuildResizeGrip()
    {
        var accent = OverlayUi.Brush("AccentBrush", "#FF3FA9F5");

        var template = new ControlTemplate(typeof(Thumb));
        var body = new FrameworkElementFactory(typeof(Border));
        body.SetValue(Border.BackgroundProperty, accent);
        body.SetValue(Border.CornerRadiusProperty, new CornerRadius(0, 0, 8, 0));
        template.VisualTree = body;

        var grip = new Thumb
        {
            Width = 16,
            Height = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Visibility = Visibility.Collapsed,
            Template = template,
        };

        grip.DragStarted += (_, _) => _resizeAnchorBounds = GetBoundsPhysical();
        grip.DragDelta += OnResizeDelta;
        grip.DragCompleted += (_, _) =>
        {
            PersistBounds();
            RaiseStateChanged();
        };
        return grip;
    }

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        double scale = CurrentScale;
        int newWidth = _resizeAnchorBounds.Width + Services.Dpi.ToPhysical(e.HorizontalChange, scale);
        int newHeight = _resizeAnchorBounds.Height + Services.Dpi.ToPhysical(e.VerticalChange, scale);

        newWidth = Math.Max(newWidth, Services.Dpi.ToPhysical(80, scale));
        newHeight = Math.Max(newHeight, Services.Dpi.ToPhysical(48, scale));

        SetBoundsPhysical(_resizeAnchorBounds.Left, _resizeAnchorBounds.Top, newWidth, newHeight);
    }

    // ---- persistence of geometry (base raises OverlayStateChanged after a drag) ----

    public void PersistBounds()
    {
        RECT r = GetBoundsPhysical();
        if (r.Width <= 0 || r.Height <= 0)
            return;
        _config.Left = r.Left;
        _config.Top = r.Top;
        _config.Width = r.Width;
        _config.Height = r.Height;
    }
}
