using System.Collections;

using PrusaTrayDiagnostic.App.Printers;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>What clicking a tray menu item does.</summary>
public enum TrayMenuCommand
{
    /// <summary>Informational item, separator or the header of a submenu.</summary>
    None,

    /// <summary>Opens <see cref="TrayMenuEntry.WebUri"/> (PrusaLink / Prusa Connect) in the browser.</summary>
    OpenWeb,

    /// <summary>Settings window (T-015).</summary>
    Settings,

    /// <summary>Diagnostics window (T-011), preselected on <see cref="TrayMenuEntry.PrinterId"/> when set.</summary>
    Diagnostics,

    /// <summary>Camera window of <see cref="TrayMenuEntry.PrinterId"/> (T-014).</summary>
    Camera,
    About,
    Quit,
}

/// <summary>
/// One tray menu item as the user sees it. Immutable with value equality (including <see cref="Submenu"/>), so that an
/// update reaches the platform menu only when something visible (or the click target) changed.
/// </summary>
/// <param name="Header">Display text, not escaped (the platform adapter handles access-key markers).</param>
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
    /// Items of the submenu this item opens (several printers: one submenu per printer); <see langword="null"/> for a
    /// plain item. An item with a submenu is a header only: clicking it does nothing.
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

/// <summary>Which auxiliary windows exist; their items are shown disabled until the window is implemented.</summary>
/// <param name="PrinterIcons">
/// Printer items show the printer's picture (T-039): on Windows, where Avalonia draws the menu itself. macOS (native
/// NSMenu) is left out until T-018 verifies it (user decision 2026-10-19).
/// </param>
internal sealed record TrayMenuOptions(bool SettingsAvailable, bool DiagnosticsAvailable, bool PrinterIcons = false)
{
    /// <summary>Both auxiliary windows exist: settings (T-015) and diagnostics (T-011); no printer icons.</summary>
    public static TrayMenuOptions Default { get; } = new(SettingsAvailable: true, DiagnosticsAvailable: true);

    /// <summary><see cref="Default"/> with printer icons where the platform shows them (Windows).</summary>
    public static TrayMenuOptions ForCurrentPlatform { get; } = Default with { PrinterIcons = OperatingSystem.IsWindows() };
}
