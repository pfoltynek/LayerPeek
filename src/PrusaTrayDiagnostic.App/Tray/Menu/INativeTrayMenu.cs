namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>Minimal view of a platform menu (Avalonia <c>NativeMenu</c> in the app, a fake in tests). UI thread only.</summary>
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

    /// <summary>Stops reporting clicks (the item is being removed).</summary>
    void Detach();
}
