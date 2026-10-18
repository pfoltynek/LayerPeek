using System.Collections.ObjectModel;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// The platform menu the presenter writes to (Avalonia <c>NativeMenu</c> in the app, a fake in tests).
/// Layout: printer items first, then a separator, then the fixed items. UI thread only.
/// </summary>
internal interface ITrayMenuTarget
{
    /// <summary>The user clicked an item; the argument is the entry the item currently shows.</summary>
    event EventHandler<TrayMenuEntry>? ItemClicked;

    /// <summary>Sets the fixed part (settings, about, quit...). Called once.</summary>
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

    /// <summary>One entry per configured printer, or a single disabled status message ("Starting…", "No printer configured"...).</summary>
    ReadOnlyObservableCollection<TrayMenuEntry> PrinterItems { get; }

    /// <summary>Opens the web interface of a printer entry; does nothing for an entry without an address.</summary>
    void OpenWeb(TrayMenuEntry entry);
}

/// <summary>Application-level actions of the tray menu, implemented by the Avalonia application.</summary>
internal interface ITrayAppCommands
{
    void ShowAbout();

    /// <summary>Opens the diagnostics window, or activates it when it is already open.</summary>
    void ShowDiagnostics();

    void Quit();
}
