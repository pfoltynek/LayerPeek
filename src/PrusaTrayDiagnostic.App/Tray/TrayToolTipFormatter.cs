using System.Globalization;
using System.Text;

using PrusaTrayDiagnostic.App.Resources;

namespace PrusaTrayDiagnostic.App.Tray;

/// <summary>
/// Builds the tray tooltip in the current UI culture: printer name, state, progress, remaining time and file name.
/// The result never exceeds <see cref="MaxLength"/>.
/// </summary>
internal static class TrayToolTipFormatter
{
    /// <summary>Product name; not localized.</summary>
    public const string AppName = "Prusa Tray Diagnostic";

    /// <summary>Windows limit: <c>NOTIFYICONDATAW.szTip</c> holds 128 UTF-16 units including the terminator.</summary>
    public const int MaxLength = 127;

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

    /// <summary>Duration rounded up to whole minutes ("&lt; 1 min", "5 min", "1 h 23 min", "2 d 5 h").</summary>
    public static string FormatDuration(TimeSpan duration) => FormatMinutes(ToolTipContent.CeilingMinutes(duration));

    private static string FormatMinutes(long minutes)
    {
        const long MinutesPerDay = 24 * 60;
        return minutes switch
        {
            < 1 => Strings.DurationLessThanMinute,
            < 60 => Format(Strings.DurationMinutes, minutes),
            < MinutesPerDay => Format(Strings.DurationHoursMinutes, minutes / 60, minutes % 60),
            _ => Format(Strings.DurationDaysHours, minutes / MinutesPerDay, minutes % MinutesPerDay / 60),
        };
    }

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
