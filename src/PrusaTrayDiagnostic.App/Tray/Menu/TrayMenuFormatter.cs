using System.Globalization;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Exactly what the menu item of one printer shows, already rounded. Cheap to build and compare, so polls that change
/// nothing visible (timestamp, telemetry, a fraction of a percent, remaining time, file name) are detected before any
/// text is formatted.
/// </summary>
/// <param name="State">Printer state; <see langword="null"/> before the first snapshot ("Connecting…").</param>
/// <param name="ProgressPercent">Whole percent, rounded down; <see langword="null"/> when not shown.</param>
internal readonly record struct PrinterMenuContent(PrinterState? State, int? ProgressPercent)
{
    /// <summary>Same rules as the tooltip: progress only for a running, paused or waiting job of a reachable printer.</summary>
    public static PrinterMenuContent From(PrinterSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return default;
        }

        int? progress = snapshot.Job is { } job
            && snapshot.State.IsReachable()
            && (snapshot.State.HasActiveJob() || snapshot.State == PrinterState.Attention)
                ? ToolTipContent.WholePercent(job.ProgressPercent)
                : null;
        return new(snapshot.State, progress);
    }
}

/// <summary>Texts of the tray menu in the current UI culture.</summary>
internal static class TrayMenuFormatter
{
    /// <summary>
    /// Longer printer names are shortened in the menu ("…" included, T-042), so that one long name does not widen the whole
    /// menu; the state is never shortened. The full name stays in the item's tooltip, the settings and the diagnostics.
    /// </summary>
    public const int MaxNameLength = 32;

    /// <summary>"MK4: Printing – 42%", with the name shortened to <see cref="MaxNameLength"/>.</summary>
    public static string PrinterHeader(string name, PrinterMenuContent content)
    {
        ArgumentNullException.ThrowIfNull(name);
        string state = TrayText.StateName(content.State);
        if (content.ProgressPercent is { } progress)
        {
            state = Format(Strings.ToolTipStateWithProgress, state, Format(Strings.ToolTipProgress, progress));
        }

        return Format(Strings.MenuPrinter, TrayText.Ellipsize(name, MaxNameLength), state);
    }

    /// <summary>Hint for a printer item: its full name when the menu shows it shortened; <see langword="null"/> otherwise.</summary>
    public static string? PrinterToolTip(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length > MaxNameLength ? name : null;
    }

    /// <summary>"Open PrusaLink" / "Open Prusa Connect" (with its access key).</summary>
    public static string OpenWebText(ConnectionType connectionType) =>
        connectionType == ConnectionType.PrusaConnect ? Strings.MenuOpenPrusaConnect : Strings.MenuOpenPrusaLink;

    private static string Format(string format, object? arg0) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}

/// <summary>
/// Access keys in Avalonia menu headers (T-042). "_" marks the access key: Avalonia draws the rendered menu on Windows
/// (<c>AccessText</c>) and strips the marker for the native NSMenu on macOS (<c>AccessText.RemoveAccessKeyMarker</c>);
/// "__" shows a single underscore on both.
/// </summary>
internal static class NativeMenuText
{
    /// <summary>
    /// U+2060 WORD JOINER: invisible, zero width, no line break, and not on any keyboard. An escaped text makes it its
    /// access key, see <see cref="EscapeAccessKeys"/>.
    /// </summary>
    public const char NoAccessKey = (char)0x2060;

    /// <summary>
    /// Plain text (a printer name, a state) shown unchanged and without a usable access key. Doubling "_" is not enough:
    /// Avalonia 12's <c>AccessText</c> shows "__" as "_" but still registers the character after the first "_" as the
    /// access key (here "_" itself, typed with Shift) and underlines it. So the text gets one real marker in front of its
    /// first underscore, on <see cref="NoAccessKey"/>: "MK4_lab" becomes "MK4_" + U+2060 + "__lab", shown as "MK4_lab"
    /// (the joiner is invisible), with an access key nobody can type. Text without "_" is returned as is.
    /// </summary>
    public static string EscapeAccessKeys(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int first = text.IndexOf('_', StringComparison.Ordinal);
        if (first < 0)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, first), "_" + NoAccessKey, text[first..].Replace("_", "__", StringComparison.Ordinal));
    }

    /// <summary>
    /// What the platform menu item gets: a resource text with its access key as is, plain text (printer names, states,
    /// messages) escaped so that it never gets one.
    /// </summary>
    public static string HeaderOf(TrayMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.HasAccessKey ? entry.Header : EscapeAccessKeys(entry.Header);
    }

    /// <summary>
    /// The access key Avalonia's <c>AccessText</c> registers for a menu text: the character after the first "_";
    /// <see langword="null"/> when there is none or it is <see cref="NoAccessKey"/> (an escaped text).
    /// </summary>
    public static char? AccessKeyOf(string menuText)
    {
        ArgumentNullException.ThrowIfNull(menuText);
        int first = menuText.IndexOf('_', StringComparison.Ordinal);
        return first < 0 || first == menuText.Length - 1 || menuText[first + 1] == NoAccessKey ? null : menuText[first + 1];
    }

    /// <summary>
    /// The visible text, like <c>AccessText</c> draws it: the first single "_" (the marker) removed, "__" shown as "_",
    /// without the invisible <see cref="NoAccessKey"/>.
    /// </summary>
    public static string DisplayTextOf(string menuText)
    {
        ArgumentNullException.ThrowIfNull(menuText);
        int marker = MarkerIndex(menuText);
        string text = marker < 0 ? menuText : menuText.Remove(marker, 1);
        return text.Replace("__", "_", StringComparison.Ordinal).Replace(NoAccessKey.ToString(), string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Where <c>AccessText</c> removes the marker: the first "_" not followed by another "_".</summary>
    private static int MarkerIndex(string menuText)
    {
        for (int i = 0; i < menuText.Length - 1; i++)
        {
            if (menuText[i] != '_')
            {
                continue;
            }

            if (menuText[i + 1] != '_')
            {
                return i;
            }

            i++;
        }

        return -1;
    }
}
