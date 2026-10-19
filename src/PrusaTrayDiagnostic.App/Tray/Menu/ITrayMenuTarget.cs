using System.Collections.ObjectModel;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// The platform menu the presenter writes to (Avalonia <c>NativeMenu</c> in the app, a fake in tests).
/// Layout: printer items first (each with its submenu when there are several printers), then a separator, then the
/// fixed items. UI thread only.
/// </summary>
internal interface ITrayMenuTarget
{
    /// <summary>The user clicked an item; the argument is the entry the item currently shows.</summary>
    event EventHandler<TrayMenuEntry>? ItemClicked;

    /// <summary>Sets the fixed part (settings, about, quit...) and removes the printer items; again after a language switch.</summary>
    void SetFixedItems(IReadOnlyList<TrayMenuEntry> items);

    void InsertPrinterItem(int index, TrayMenuEntry entry);

    /// <summary>Changes the existing item in place (text, enabled state, click target).</summary>
    void UpdatePrinterItem(int index, TrayMenuEntry entry);

    void RemovePrinterItem(int index);

    void ClearPrinterItems();
}

/// <summary>What the tray menu shows; collection changes are raised on the UI thread.</summary>
internal interface ITrayMenuSource
{
    IReadOnlyList<TrayMenuEntry> FixedItems { get; }

    /// <summary><see cref="FixedItems"/> was replaced (language switch); raised on the UI thread.</summary>
    event EventHandler? FixedItemsChanged;

    /// <summary>
    /// One entry per configured printer (a submenu header when there are several printers), or a single disabled status
    /// message ("Starting…", "No printer configured"...). A single printer with an enabled camera is followed by its
    /// "Camera…" entry (with several printers it is in the printer's submenu).
    /// </summary>
    ReadOnlyObservableCollection<TrayMenuEntry> PrinterItems { get; }

    /// <summary>Opens the web interface of a printer entry; does nothing for an entry without an address.</summary>
    void OpenWeb(TrayMenuEntry entry);
}

/// <summary>Application-level actions of the tray menu, implemented by the Avalonia application.</summary>
internal interface ITrayAppCommands
{
    void ShowAbout();

    /// <summary>
    /// Opens the diagnostics window, or activates it when it is already open; shows <paramref name="printerId"/> when set
    /// (a printer submenu), otherwise keeps the current or default printer.
    /// </summary>
    void ShowDiagnostics(string? printerId);

    /// <summary>Opens the settings window, or activates it when it is already open.</summary>
    void ShowSettings();

    /// <summary>
    /// Opens the camera window of <paramref name="printerId"/>, or activates it when it is already open (one window per
    /// printer; windows of different printers may be open at the same time).
    /// </summary>
    void ShowCamera(string printerId);

    void Quit();
}
