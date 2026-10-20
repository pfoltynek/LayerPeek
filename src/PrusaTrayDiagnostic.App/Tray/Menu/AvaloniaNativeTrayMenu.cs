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
        public void SetIcon(PrinterPicture? icon) => Native.Icon = icon is not null && _icons is not null ? _icons.Get(icon) : null;

        public INativeTrayMenu CreateSubmenu()
        {
            NativeMenu submenu = new();
            Native.Menu = submenu;
            return new AvaloniaNativeTrayMenu(submenu, _icons);
        }

        public void Detach() => Native.Click -= OnClick;

        private void OnClick(object? sender, EventArgs e) => _clicked();
    }
}
