using System.Collections.ObjectModel;

namespace LayerPeek.App.Tray.Menu;

/// <summary>
/// The platform menu the presenter writes to (Avalonia <c>NativeMenu</c> in the app, a fake in tests).
/// Layout: printer items first (each with its submenu, T-042), then a separator, then the fixed items. UI thread only.
/// </summary>
internal interface ITrayMenuTarget
{
    /// <summary>The user clicked an item; the argument is the entry the item currently shows.</summary>
    event EventHandler<TrayMenuEntry>? ItemClicked;

    /// <summary>
    /// Sets the fixed part (diagnostics, settings, about, quit...) and removes the printer items; again after a language
    /// switch only.
    /// </summary>
    void SetFixedItems(IReadOnlyList<TrayMenuEntry> items);

    /// <summary>
    /// Changes one fixed item in place (e.g. "Diagnostics…" enabled or disabled), without touching the rest of the menu.
    /// <paramref name="index"/> is its index in the list given to <see cref="SetFixedItems"/> (separators included); it
    /// must be a plain item and <paramref name="entry"/> must not be a separator.
    /// </summary>
    void UpdateFixedItem(int index, TrayMenuEntry entry);

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

    /// <summary>
    /// <see cref="FixedItems"/> was replaced as a whole (language switch only): the menu is built again. Raised on the UI
    /// thread after <see cref="PrinterItems"/> is up to date.
    /// </summary>
    event EventHandler? FixedItemsChanged;

    /// <summary>
    /// One entry of <see cref="FixedItems"/> changed (its index; "Diagnostics…" enabled or disabled, never on a poll), the
    /// others are the same: the platform item is updated in place (user decision 2026-10-21). Raised on the UI thread after
    /// <see cref="PrinterItems"/> is up to date.
    /// </summary>
    event EventHandler<int>? FixedItemChanged;

    /// <summary>
    /// One submenu header per configured printer (its items: web interface and camera; T-042), or a disabled
    /// status message ("Starting…", "Monitoring stopped…"); without printers "No printer configured" followed by
    /// "Add printer…".
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
    /// (an instance command), otherwise keeps the current or default printer (the main menu item, 2026-10-21).
    /// </summary>
    void ShowDiagnostics(string? printerId);

    /// <summary>Opens the settings window, or activates it when it is already open.</summary>
    void ShowSettings();

    /// <summary>
    /// "Add printer…" (T-042, shown while no printer is configured): opens the settings window (or activates it) on the
    /// Printers tab and adds a new printer to it.
    /// </summary>
    void AddPrinter();

    /// <summary>
    /// The camera item ("Show camera" / "Hide camera", T-040, 2026-10-22): opens the camera window of <paramref name="printerId"/>, or closes it when it is
    /// open (one window per printer; windows of different printers may be open at the same time).
    /// </summary>
    void ToggleCamera(string printerId);

    /// <summary>
    /// "Show camera" while the camera window of <paramref name="printerId"/> is open (macOS, T-102): the app and the window
    /// come to the front, a minimized window is restored; a window that closed meanwhile opens.
    /// </summary>
    void BringCameraToFront(string printerId);

    void Quit();
}
