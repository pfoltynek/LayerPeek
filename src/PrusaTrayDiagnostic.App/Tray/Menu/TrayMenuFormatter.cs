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
    /// <summary>Longer items are shortened (only a very long printer name gets there).</summary>
    public const int MaxHeaderLength = 100;

    /// <summary>"MK4: Printing – 42%".</summary>
    public static string PrinterHeader(string name, PrinterMenuContent content)
    {
        ArgumentNullException.ThrowIfNull(name);
        string state = TrayText.StateName(content.State);
        if (content.ProgressPercent is { } progress)
        {
            state = Format(Strings.ToolTipStateWithProgress, state, Format(Strings.ToolTipProgress, progress));
        }

        string header = Format(Strings.MenuPrinter, name, state);
        if (header.Length <= MaxHeaderLength)
        {
            return header;
        }

        // Shorten the name and keep the state readable.
        int nameLength = Math.Max(MaxHeaderLength - (header.Length - name.Length), TrayText.Ellipsis.Length + 1);
        header = Format(Strings.MenuPrinter, TrayText.Ellipsize(name, nameLength), state);
        return TrayText.Ellipsize(header, MaxHeaderLength);
    }

    /// <summary>Hint for a printer item: what clicking it opens, or why it cannot be opened.</summary>
    public static string PrinterToolTip(ConnectionType connectionType, bool canOpenWeb) =>
        !canOpenWeb ? Strings.MenuInvalidAddress
        : connectionType == ConnectionType.PrusaConnect ? Strings.MenuOpenPrusaConnect
        : Strings.MenuOpenPrusaLink;

    private static string Format(string format, object? arg0) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}

/// <summary>
/// Avalonia menu headers treat "_" as an access-key marker (rendered menu on Windows, stripped on macOS);
/// "__" shows a single underscore on both.
/// </summary>
internal static class NativeMenuText
{
    public static string EscapeAccessKeys(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Contains('_', StringComparison.Ordinal) ? text.Replace("_", "__", StringComparison.Ordinal) : text;
    }
}
