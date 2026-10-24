using System.Globalization;
using System.Text;

using LayerPeek.App.Resources;

namespace LayerPeek.App.Tray;

/// <summary>
/// Builds the tray tooltip in the current UI culture: printer name, state, progress, remaining time and file name.
/// The result never exceeds <see cref="MaxLength"/>.
/// </summary>
internal static class TrayToolTipFormatter
{
    /// <summary>Product name; not localized.</summary>
    public const string AppName = "LayerPeek";

    /// <summary>Windows limit: <c>NOTIFYICONDATAW.szTip</c> holds 128 UTF-16 units including the terminator.</summary>
    public const int MaxLength = 127;

    /// <summary>
    /// Printer names in the multi-printer tooltip are shortened to this length, so that one long name cannot crowd out
    /// the other printers.
    /// </summary>
    public const int MaxSummaryNameLength = 24;

    private const char LineSeparator = '\n';

    public static string Starting() => AppName + LineSeparator + Strings.ToolTipStarting;

    public static string NotConfigured() => AppName + LineSeparator + Strings.ToolTipNoPrinter;

    public static string MonitoringFailed() => AppName + LineSeparator + Strings.ToolTipMonitoringFailed;

    /// <param name="printer">The printer the icon represents.</param>
    /// <param name="otherPrinterCount">Further configured printers, summarized in one line.</param>
    public static string Format(TrayPrinter printer, int otherPrinterCount) =>
        Format(ToolTipContent.From(printer, otherPrinterCount));

    public static string Format(ToolTipContent content)
    {
        string state = TrayText.StateName(content.State);
        if (content.ProgressPercent is { } progress)
        {
            state = Format(Strings.ToolTipStateWithProgress, state, Format(Strings.ToolTipProgress, progress));
        }

        string? remaining = content.RemainingMinutes is { } minutes ? Format(Strings.ToolTipRemaining, FormatMinutes(minutes)) : null;
        string? others = content.OtherPrinterCount > 0 ? Format(Strings.ToolTipOtherPrinters, content.OtherPrinterCount) : null;
        return Compose(content.Name, state, remaining, content.FileName, others);
    }

    /// <summary>
    /// Tooltip for several printers: one line per printer ("Name: State – 42%, 1 h 5 min left"), the most severe first
    /// (configuration order on a tie, see <see cref="TrayStatusRules.Severity"/>). When everything does not fit into
    /// <see cref="MaxLength"/>, remaining times are dropped first; then the least severe printers are summarized as
    /// "Other printers: N". Names are shortened to <see cref="MaxSummaryNameLength"/>. Called only when something visible
    /// changed, never per poll.
    /// </summary>
    public static string FormatSummary(IReadOnlyList<ToolTipContent> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        int count = printers.Count;
        if (count == 0)
        {
            return NotConfigured();
        }

        int[] order = SeverityOrder(printers);
        string[] shortLines = new string[count];
        string[] fullLines = new string[count];
        int shortLength = count - 1;
        int fullLength = count - 1;
        for (int i = 0; i < count; i++)
        {
            ToolTipContent printer = printers[order[i]];
            string line = SummaryLine(printer);
            shortLines[i] = line;
            fullLines[i] = printer.RemainingMinutes is { } minutes ? Format(Strings.ToolTipLineRemaining, line, FormatMinutes(minutes)) : line;
            shortLength += shortLines[i].Length;
            fullLength += fullLines[i].Length;
        }

        if (fullLength <= MaxLength)
        {
            return string.Join(LineSeparator, fullLines);
        }

        if (shortLength <= MaxLength)
        {
            return string.Join(LineSeparator, shortLines);
        }

        return Truncated(shortLines);
    }

    /// <summary>Duration rounded up to whole minutes ("&lt; 1 min", "5 min", "1 h 23 min", "2 d 5 h").</summary>
    public static string FormatDuration(TimeSpan duration) => FormatMinutes(ToolTipContent.CeilingMinutes(duration));

    /// <summary>
    /// <see cref="FormatDuration(TimeSpan)"/> with the units in <paramref name="textCulture"/> instead of the chosen
    /// language, for texts built in one resolved language (toasts). Number formats still follow <c>CurrentCulture</c>.
    /// </summary>
    public static string FormatDurationIn(TimeSpan duration, CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(textCulture);
        return FormatMinutes(ToolTipContent.CeilingMinutes(duration), textCulture);
    }

    /// <param name="textCulture"><see langword="null"/> = like the generated <see cref="Strings"/> properties.</param>
    private static string FormatMinutes(long minutes, CultureInfo? textCulture = null)
    {
        const long MinutesPerDay = 24 * 60;
        CultureInfo? culture = textCulture ?? Strings.Culture;
        return minutes switch
        {
            < 1 => Text(nameof(Strings.DurationLessThanMinute), culture),
            < 60 => Format(Text(nameof(Strings.DurationMinutes), culture), minutes),
            < MinutesPerDay => Format(Text(nameof(Strings.DurationHoursMinutes), culture), minutes / 60, minutes % 60),
            _ => Format(Text(nameof(Strings.DurationDaysHours), culture), minutes / MinutesPerDay, minutes % MinutesPerDay / 60),
        };
    }

    /// <summary>A resource text in <paramref name="culture"/> (<see langword="null"/> = the thread's UI culture), exactly as the generated properties read it.</summary>
    private static string Text(string name, CultureInfo? culture) => Strings.ResourceManager.GetString(name, culture) ?? name;

    private static string Compose(string name, string state, string? remaining, string? fileName, string? others)
    {
        int fixedLength = name.Length + 1 + state.Length
            + (remaining is null ? 0 : remaining.Length + 1)
            + (others is null ? 0 : others.Length + 1);

        // The file name is the least important and potentially longest part: shorten it first.
        if (fileName is not null)
        {
            int available = MaxLength - fixedLength - 1;
            fileName = available >= 2 ? TrayText.Ellipsize(fileName, available) : null;
        }

        StringBuilder text = new(fixedLength + (fileName?.Length + 1 ?? 0));
        text.Append(name).Append(LineSeparator).Append(state);
        AppendLine(text, remaining);
        AppendLine(text, fileName);
        AppendLine(text, others);

        // Only a very long printer name can still exceed the limit.
        return text.Length > MaxLength ? TrayText.Ellipsize(text.ToString(), MaxLength) : text.ToString();
    }

    /// <summary>"Name: State – 42%" with the name shortened to <see cref="MaxSummaryNameLength"/>.</summary>
    private static string SummaryLine(ToolTipContent printer)
    {
        string state = TrayText.StateName(printer.State);
        if (printer.ProgressPercent is { } progress)
        {
            state = Format(Strings.ToolTipStateWithProgress, state, Format(Strings.ToolTipProgress, progress));
        }

        return Format(Strings.MenuPrinter, TrayText.Ellipsize(printer.Name, MaxSummaryNameLength), state);
    }

    /// <summary>Indexes of <paramref name="printers"/>, most severe first; stable, so configuration order decides a tie.</summary>
    private static int[] SeverityOrder(IReadOnlyList<ToolTipContent> printers)
    {
        int[] order = new int[printers.Count];
        int[] severity = new int[printers.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
            severity[i] = TrayStatusRules.Severity(printers[i].Status);
        }

        Array.Sort(order, (a, b) => severity[a] != severity[b] ? severity[b].CompareTo(severity[a]) : a.CompareTo(b));
        return order;
    }

    /// <summary>As many lines as fit (most severe first), then "Other printers: N" for the rest.</summary>
    private static string Truncated(string[] lines)
    {
        StringBuilder text = new(MaxLength);
        int shown = 0;
        for (; shown < lines.Length; shown++)
        {
            int rest = lines.Length - shown - 1;
            int footerLength = rest > 0 ? 1 + OtherPrinters(rest).Length : 0;
            int lineLength = (shown > 0 ? 1 : 0) + lines[shown].Length;
            if (text.Length + lineLength + footerLength > MaxLength)
            {
                break;
            }

            if (shown > 0)
            {
                text.Append(LineSeparator);
            }

            text.Append(lines[shown]);
        }

        if (shown == 0)
        {
            // Not even the most severe printer fits next to the count: shorten its line (practically unreachable,
            // names are already shortened).
            int available = MaxLength - 1 - OtherPrinters(lines.Length - 1).Length;
            text.Append(TrayText.Ellipsize(lines[0], available));
            shown = 1;
        }

        if (shown < lines.Length)
        {
            text.Append(LineSeparator).Append(OtherPrinters(lines.Length - shown));
        }

        return text.ToString();
    }

    private static string OtherPrinters(int count) => Format(Strings.ToolTipOtherPrinters, count);

    private static void AppendLine(StringBuilder text, string? line)
    {
        if (line is not null)
        {
            text.Append(LineSeparator).Append(line);
        }
    }

    private static string Format(string format, object? arg0) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}
