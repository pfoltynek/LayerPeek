using Avalonia.Controls;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Thin adapter over the <see cref="NativeMenu"/> of an Avalonia <see cref="TrayIcon"/> or of one of its submenus
/// (rendered by Avalonia on Windows, native NSMenu on macOS). All menu logic lives in <see cref="TrayMenuTarget"/>.
/// UI thread only.
/// </summary>
internal sealed class AvaloniaNativeTrayMenu : INativeTrayMenu
{
    private readonly NativeMenu _menu;

    public AvaloniaNativeTrayMenu(TrayIcon trayIcon)
    {
        ArgumentNullException.ThrowIfNull(trayIcon);
        _menu = trayIcon.Menu ??= new NativeMenu();
    }

    private AvaloniaNativeTrayMenu(NativeMenu menu) => _menu = menu;

    public INativeTrayMenuItem CreateItem(Action clicked) => new Item(clicked);

    public void Insert(int index, INativeTrayMenuItem item) => _menu.Items.Insert(index, ((Item)item).Native);

    public void Add(INativeTrayMenuItem item) => _menu.Items.Add(((Item)item).Native);

    public void AddSeparator() => _menu.Items.Add(new NativeMenuItemSeparator());

    public void RemoveAt(int index) => _menu.Items.RemoveAt(index);

    public void Clear() => _menu.Items.Clear();

    private sealed class Item : INativeTrayMenuItem
    {
        private readonly Action _clicked;

        public Item(Action clicked)
        {
            ArgumentNullException.ThrowIfNull(clicked);
            _clicked = clicked;
            Native.Click += OnClick;
        }

        public NativeMenuItem Native { get; } = new();

        public void SetHeader(string header) => Native.Header = header;

        public void SetEnabled(bool enabled) => Native.IsEnabled = enabled;

        public void SetToolTip(string? toolTip) => Native.ToolTip = toolTip;

        public void SetChecked(bool? isChecked)
        {
            Native.ToggleType = isChecked is null ? MenuItemToggleType.None : MenuItemToggleType.CheckBox;
            Native.IsChecked = isChecked == true;
        }

        public INativeTrayMenu CreateSubmenu()
        {
            NativeMenu submenu = new();
            Native.Menu = submenu;
            return new AvaloniaNativeTrayMenu(submenu);
        }

        public void Detach() => Native.Click -= OnClick;

        private void OnClick(object? sender, EventArgs e) => _clicked();
    }
}
