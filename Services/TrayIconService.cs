using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QuickOrbit.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _toggleItem;
    private readonly Func<bool> _isOverlayVisible;
    private readonly Drawing.Icon _icon;
    private readonly bool _ownsIcon;
    private bool _disposed;

    public TrayIconService(Action toggle, Action settings, Action exit, Func<bool> isOverlayVisible, string hotkey)
    {
        _isOverlayVisible = isOverlayVisible;

        _icon = LoadIcon(out _ownsIcon);
        _menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Font = new Drawing.Font("Segoe UI", 9.25f),
            BackColor = Drawing.Color.FromArgb(12, 18, 26),
            ForeColor = Drawing.Color.FromArgb(234, 242, 252),
            Padding = new Forms.Padding(4)
        };
        _menu.Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors());

        _toggleItem = CreateMenuItem("Показать", (_, _) => toggle());
        var settingsItem = CreateMenuItem("Настройки", (_, _) => settings());
        var exitItem = CreateMenuItem("Выход", (_, _) => exit());

        _menu.Items.Add(_toggleItem);
        _menu.Items.Add(settingsItem);
        _menu.Items.Add(new Forms.ToolStripSeparator());
        _menu.Items.Add(exitItem);
        _menu.Opening += (_, _) =>
            _toggleItem.Text = _isOverlayVisible() ? "Скрыть" : "Показать";

        var initialTip = "Quick Orbit — " + hotkey;
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = initialTip.Length > 63 ? initialTip[..63] : initialTip,
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += (_, _) => toggle();
    }

    public void UpdateHotkey(string hotkey)
    {
        var text = "Quick Orbit — " + hotkey;
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void ShowError(string title, string message)
    {
        ShowBalloon(title, message, Forms.ToolTipIcon.Warning);
    }

    public void ShowInfo(string title, string message)
    {
        ShowBalloon(title, message, Forms.ToolTipIcon.Info);
    }

    private void ShowBalloon(string title, string message, Forms.ToolTipIcon icon)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(5000);
        }
        catch
        {
            // Notifications are best effort.
        }
    }

    private static Forms.ToolStripMenuItem CreateMenuItem(string text, EventHandler handler)
    {
        return new Forms.ToolStripMenuItem(text)
        {
            ForeColor = Drawing.Color.FromArgb(234, 242, 252),
            BackColor = Drawing.Color.FromArgb(12, 18, 26),
            Padding = new Forms.Padding(8, 3, 8, 3),
            Font = new Drawing.Font("Segoe UI", 9.25f)
        }.With(handler);
    }

    private static Drawing.Icon LoadIcon(out bool ownsIcon)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var extracted = Drawing.Icon.ExtractAssociatedIcon(path);
                if (extracted is not null)
                {
                    ownsIcon = true;
                    return extracted;
                }
            }
        }
        catch
        {
            // Use the system fallback below.
        }

        ownsIcon = false;
        return Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        if (_ownsIcon)
        {
            _icon.Dispose();
        }
    }

    private sealed class DarkMenuColors : Forms.ProfessionalColorTable
    {
        public override Drawing.Color ToolStripDropDownBackground => Drawing.Color.FromArgb(12, 18, 26);
        public override Drawing.Color MenuItemSelected => Drawing.Color.FromArgb(29, 42, 57);
        public override Drawing.Color MenuItemSelectedGradientBegin => Drawing.Color.FromArgb(29, 42, 57);
        public override Drawing.Color MenuItemSelectedGradientEnd => Drawing.Color.FromArgb(29, 42, 57);
        public override Drawing.Color MenuItemBorder => Drawing.Color.FromArgb(41, 57, 75);
        public override Drawing.Color ImageMarginGradientBegin => Drawing.Color.FromArgb(12, 18, 26);
        public override Drawing.Color ImageMarginGradientMiddle => Drawing.Color.FromArgb(12, 18, 26);
        public override Drawing.Color ImageMarginGradientEnd => Drawing.Color.FromArgb(12, 18, 26);
        public override Drawing.Color SeparatorDark => Drawing.Color.FromArgb(36, 48, 64);
        public override Drawing.Color SeparatorLight => Drawing.Color.FromArgb(36, 48, 64);
    }
}

internal static class ToolStripMenuItemExtensions
{
    public static System.Windows.Forms.ToolStripMenuItem With(
        this System.Windows.Forms.ToolStripMenuItem item,
        EventHandler handler)
    {
        item.Click += handler;
        return item;
    }
}
