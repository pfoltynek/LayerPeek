namespace PrusaTrayDiagnostic.App.Tray.Menu;

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

    /// <summary>
    /// <see langword="null"/>: a plain item; otherwise a check item (check mark when <see langword="true"/>). Native menus
    /// have no switch, so a toggle is a check item (T-040).
    /// </summary>
    void SetChecked(bool? isChecked);

    /// <summary>
    /// Gives the item a submenu (called at most once, right after creation, before the item is inserted); its items are
    /// created by the returned menu.
    /// </summary>
    INativeTrayMenu CreateSubmenu();

    /// <summary>Stops reporting clicks (the item is being removed).</summary>
    void Detach();
}
