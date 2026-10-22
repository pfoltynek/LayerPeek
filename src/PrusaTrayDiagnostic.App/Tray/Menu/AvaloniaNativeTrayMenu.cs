using Avalonia.Controls;

using PrusaTrayDiagnostic.App.Printers;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Thin adapter over the <see cref="NativeMenu"/> of an Avalonia <see cref="TrayIcon"/> or of one of its submenus
/// (rendered by Avalonia on Windows, native NSMenu on macOS). All menu logic lives in <see cref="TrayMenuTarget"/>.
/// UI thread only.
/// </summary>
internal sealed class AvaloniaNativeTrayMenu : INativeTrayMenu
{
    private readonly NativeMenu _menu;
    private readonly PrinterMenuIcons? _icons;

    /// <param name="icons">Bitmaps of printer icons (T-039); <see langword="null"/>: items get no icons.</param>
    public AvaloniaNativeTrayMenu(TrayIcon trayIcon, PrinterMenuIcons? icons = null)
    {
        ArgumentNullException.ThrowIfNull(trayIcon);
        _menu = trayIcon.Menu ??= new NativeMenu();
        _icons = icons;
    }

    private AvaloniaNativeTrayMenu(NativeMenu menu, PrinterMenuIcons? icons)
    {
        _menu = menu;
        _icons = icons;
    }

    public INativeTrayMenuItem CreateItem(Action clicked) => new Item(clicked, _icons);

    public void Insert(int index, INativeTrayMenuItem item) => _menu.Items.Insert(index, ((Item)item).Native);

    public void Add(INativeTrayMenuItem item) => _menu.Items.Add(((Item)item).Native);

    public void AddSeparator() => _menu.Items.Add(new NativeMenuItemSeparator());

    public void RemoveAt(int index) => _menu.Items.RemoveAt(index);

    public void Clear() => _menu.Items.Clear();

    private sealed class Item : INativeTrayMenuItem
    {
        private readonly Action _clicked;
        private readonly PrinterMenuIcons? _icons;

        // The custom picture this item shows, released with the item (T-039).
        private (string? PrinterId, byte[] Png)? _customIcon;

        public Item(Action clicked, PrinterMenuIcons? icons)
        {
            ArgumentNullException.ThrowIfNull(clicked);
            _clicked = clicked;
            _icons = icons;
            Native.Click += OnClick;
        }

        public NativeMenuItem Native { get; } = new();

        public void SetHeader(string header) => Native.Header = header;

        public void SetEnabled(bool enabled) => Native.IsEnabled = enabled;

        public void SetToolTip(string? toolTip) => Native.ToolTip = toolTip;

        // The bitmap belongs to the icon cache (shared illustrations, custom pictures released when the tray stops).
        public void SetIcon(PrinterPicture? icon, string? printerId)
        {
            Native.Icon = icon is not null && _icons is not null ? _icons.Get(icon, printerId) : null;
            (string? PrinterId, byte[] Png)? shown = icon?.CustomPng is { } png ? (printerId, png) : null;
            if (_icons is null || SameCustomIcon(shown, _customIcon))
            {
                return;
            }

            if (shown is { } now)
            {
                _icons.Acquire(now.PrinterId, now.Png);
            }

            if (_customIcon is { } previous)
            {
                // This item no longer shows that picture (an illustration, no icon, another picture or printer).
                _icons.Release(previous.PrinterId, previous.Png);
            }

            _customIcon = shown;
        }

        private static bool SameCustomIcon((string? PrinterId, byte[] Png)? a, (string? PrinterId, byte[] Png)? b) =>
            a is null ? b is null
            : b is not null && ReferenceEquals(a.Value.Png, b.Value.Png) && string.Equals(a.Value.PrinterId, b.Value.PrinterId, StringComparison.OrdinalIgnoreCase);

        public INativeTrayMenu CreateSubmenu()
        {
            NativeMenu submenu = new();
            Native.Menu = submenu;
            return new AvaloniaNativeTrayMenu(submenu, _icons);
        }

        /// <summary>The item is removed (printer removed or the menu rebuilt): its custom bitmap goes with it.</summary>
        public void Detach()
        {
            Native.Click -= OnClick;
            if (_customIcon is { } icon)
            {
                _icons?.Release(icon.PrinterId, icon.Png);
                _customIcon = null;
            }
        }

        private void OnClick(object? sender, EventArgs e) => _clicked();
    }
}
