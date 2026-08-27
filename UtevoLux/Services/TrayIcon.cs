using System;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace UtevoLux.Services;

/// <summary>
/// System-tray presence via <see cref="WinForms.NotifyIcon"/> (WPF has no native tray icon).
/// The icon is drawn at runtime so no .ico asset is needed. Left-click restores the shell;
/// the context menu offers open, a run-at-startup toggle, and exit.
///
/// This is the ONE place WinForms is used; it lives entirely off the hot path.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _startupItem;
    private IntPtr _hicon;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        Drawing.Icon icon = BuildIcon();

        _startupItem = new WinForms.ToolStripMenuItem("Iniciar com o Windows")
        {
            CheckOnClick = true,
            Checked = StartupRegistration.IsEnabled()
        };
        _startupItem.CheckedChanged += (_, _) => StartupRegistration.SetEnabled(_startupItem.Checked);

        var menu = new WinForms.ContextMenuStrip();
        var openItem = new WinForms.ToolStripMenuItem("Abrir Utevo Lux");
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        var exitItem = new WinForms.ToolStripMenuItem("Sair");
        exitItem.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(openItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _icon = new WinForms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "Utevo Lux",
            ContextMenuStrip = menu
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
                OpenRequested?.Invoke();
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    /// <summary>Refresh the startup checkmark (e.g. if it changed elsewhere).</summary>
    public void SyncStartupState() => _startupItem.Checked = StartupRegistration.IsEnabled();

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(2500);
    }

    private Drawing.Icon BuildIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (Drawing.Graphics g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using var accent = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xFF, 0x3F, 0xA9, 0xF5));
            g.FillEllipse(accent, 2, 2, 28, 28);

            // Two overlapping frames echoing the Mirror glyph.
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(0xFF, 0x08, 0x13, 0x1C), 2.4f);
            g.DrawRectangle(pen, 8, 8, 10, 10);
            g.DrawRectangle(pen, 14, 14, 10, 10);
        }

        _hicon = bmp.GetHicon();
        // Clone into a managed Icon we own; the HICON is destroyed on dispose.
        return (Drawing.Icon)Drawing.Icon.FromHandle(_hicon).Clone();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _icon.Visible = false;
        _icon.Dispose();
        if (_hicon != IntPtr.Zero)
        {
            DestroyIcon(_hicon);
            _hicon = IntPtr.Zero;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
