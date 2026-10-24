using LayerPeek.App.Printers;

namespace LayerPeek.App.Tray.Menu;

/// <summary>
/// Minimal view of a platform menu or submenu (Avalonia <c>NativeMenu</c> in the app, a fake in tests). UI thread only.
/// </summary>
internal interface INativeTrayMenu
{
    /// <param name="clicked">Called when the user clicks the item, until <see cref="INativeTrayMenuItem.Detach"/>.</param>
    INativeTrayMenuItem CreateItem(Action clicked);

    void Insert(int index, INativeTrayMenuItem item);

    void Add(INativeTrayMenuItem item);

    void AddSeparator();

    void RemoveAt(int index);

    void Clear();
}

/// <summary>One platform menu item; setters write straight to the platform object.</summary>
internal interface INativeTrayMenuItem
{
    void SetHeader(string header);

    void SetEnabled(bool enabled);

    void SetToolTip(string? toolTip);

    /// <summary>The picture of printer <paramref name="printerId"/> shown left of the text (T-039); <see langword="null"/> = none.</summary>
    void SetIcon(PrinterPicture? icon, string? printerId);

    /// <summary>
    /// Gives the item a submenu (called at most once, right after creation, before the item is inserted); its items are
    /// created by the returned menu.
    /// </summary>
    INativeTrayMenu CreateSubmenu();

    /// <summary>Stops reporting clicks (the item is being removed).</summary>
    void Detach();
}
