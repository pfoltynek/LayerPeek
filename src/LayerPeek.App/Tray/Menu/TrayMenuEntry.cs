using System.Collections;

using LayerPeek.App.Printers;

namespace LayerPeek.App.Tray.Menu;

/// <summary>What clicking a tray menu item does.</summary>
public enum TrayMenuCommand
{
    /// <summary>Informational item, separator or the header of a submenu (every printer item, T-042).</summary>
    None,

    /// <summary>Opens <see cref="TrayMenuEntry.WebUri"/> (PrusaLink / Prusa Connect) in the browser.</summary>
    OpenWeb,

    /// <summary>Settings window (T-015).</summary>
    Settings,

    /// <summary>
    /// No printer is configured (T-042): opens the settings window on the Printers tab and adds a new printer there.
    /// </summary>
    AddPrinter,

    /// <summary>
    /// Diagnostics window (T-011): a single main menu item without a printer (user decision 2026-10-21, the window has its
    /// own printer combo); an open window keeps its printer. Enabled only while the menu shows printers. Preselects
    /// <see cref="TrayMenuEntry.PrinterId"/> when an entry sets it.
    /// </summary>
    Diagnostics,

    /// <summary>
    /// Camera window of <see cref="TrayMenuEntry.PrinterId"/> (T-014): opens it, or closes it when it is open (T-040; a
    /// plain item "Show camera" / "Hide camera", 2026-10-22).
    /// </summary>
    Camera,

    /// <summary>
    /// "Show camera" next to "Hide camera" while the camera window of <see cref="TrayMenuEntry.PrinterId"/> is open (macOS
    /// only, T-102): brings the app and the window to the front (restored when minimized); opens it when it closed meanwhile.
    /// </summary>
    CameraToFront,
    About,
    Quit,
}

/// <summary>
/// One tray menu item as the user sees it. Immutable with value equality (including <see cref="Submenu"/>), so that an
/// update reaches the platform menu only when something visible (or the click target) changed.
/// </summary>
/// <param name="Header">
/// Display text. Plain text (printer names, states, messages) unless <see cref="HasAccessKey"/>: the platform adapter
/// escapes it so that an "_" in a printer name never becomes an access key.
/// </param>
/// <param name="ToolTip">Hint shown on hover where the platform supports it.</param>
/// <param name="WebUri">Address opened by <see cref="TrayMenuCommand.OpenWeb"/>; already validated as http(s).</param>
/// <param name="PrinterId">The printer the item belongs to (printer items and their submenu items).</param>
internal sealed record TrayMenuEntry(
    TrayMenuCommand Command,
    string Header,
    bool IsEnabled,
    string? ToolTip = null,
    Uri? WebUri = null,
    string? PrinterId = null)
{
    public static TrayMenuEntry Separator { get; } = new(TrayMenuCommand.None, string.Empty, IsEnabled: false) { IsSeparator = true };

    public bool IsSeparator { get; private init; }

    /// <summary>
    /// <see cref="Header"/> is a menu text with an access-key marker ("_Settings…", T-042): fixed items and printer submenu
    /// items, from the resources. Shown as is; <see langword="false"/> = plain text, escaped by the adapter.
    /// </summary>
    public bool HasAccessKey { get; init; }

    /// <summary>
    /// Items of the submenu this item opens (every printer has one, T-042); <see langword="null"/> for a plain item. An item
    /// with a submenu is a header only: clicking it does nothing.
    /// </summary>
    public TrayMenuEntryList? Submenu { get; init; }

    /// <summary>
    /// The printer's picture shown left of the text (printer items on Windows, T-039); <see langword="null"/> = no icon.
    /// </summary>
    public PrinterPicture? Icon { get; init; }
}

/// <summary>Immutable list of menu entries with value equality, so that <see cref="TrayMenuEntry"/> keeps value semantics.</summary>
internal sealed class TrayMenuEntryList : IReadOnlyList<TrayMenuEntry>, IEquatable<TrayMenuEntryList>
{
    private readonly TrayMenuEntry[] _items;

    public TrayMenuEntryList(IReadOnlyList<TrayMenuEntry> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = new TrayMenuEntry[items.Count];
        for (int i = 0; i < _items.Length; i++)
        {
            _items[i] = items[i] ?? throw new ArgumentException("Menu entries must not be null.", nameof(items));
        }
    }

    public int Count => _items.Length;

    public TrayMenuEntry this[int index] => _items[index];

    public bool Equals(TrayMenuEntryList? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || other._items.Length != _items.Length)
        {
            return false;
        }

        for (int i = 0; i < _items.Length; i++)
        {
            if (!_items[i].Equals(other._items[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as TrayMenuEntryList);

    public override int GetHashCode()
    {
        HashCode hash = default;
        foreach (TrayMenuEntry item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public IEnumerator<TrayMenuEntry> GetEnumerator() => ((IEnumerable<TrayMenuEntry>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Which auxiliary windows exist; their items are shown disabled until the window is implemented ("Add printer…" follows
/// the settings window).
/// </summary>
/// <param name="DiagnosticsAvailable">
/// The diagnostics window exists. The main menu item "Diagnostics…" is enabled only when it does and the menu shows
/// printers (2026-10-21).
/// </param>
/// <param name="PrinterIcons">
/// Printer items show the printer's picture (T-039): on Windows, where Avalonia draws the menu itself. macOS (native
/// NSMenu) is left out until T-018 verifies it (user decision 2026-10-19).
/// </param>
/// <param name="CameraToFront">
/// A printer with an open camera window also has "Show camera" (<see cref="TrayMenuCommand.CameraToFront"/>, T-102): on
/// macOS, where its windows have no taskbar button of their own and fall behind other apps.
/// </param>
internal sealed record TrayMenuOptions(bool SettingsAvailable, bool DiagnosticsAvailable, bool PrinterIcons = false, bool CameraToFront = false)
{
    /// <summary>Both auxiliary windows exist: settings (T-015) and diagnostics (T-011); no printer icons.</summary>
    public static TrayMenuOptions Default { get; } = new(SettingsAvailable: true, DiagnosticsAvailable: true);

    /// <summary><see cref="Default"/> with printer icons where the platform shows them (Windows) and "Show camera" for open camera windows on macOS.</summary>
    public static TrayMenuOptions ForCurrentPlatform { get; } = Default with { PrinterIcons = OperatingSystem.IsWindows(), CameraToFront = OperatingSystem.IsMacOS() };
}
