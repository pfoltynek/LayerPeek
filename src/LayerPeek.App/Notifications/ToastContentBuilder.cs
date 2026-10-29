using System.Globalization;
using System.Text;

using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Cooldown;
using LayerPeek.Core.Events;
using LayerPeek.Core.Notifications;

namespace LayerPeek.App.Notifications;

/// <summary>
/// Builds the toast XML (Windows <c>ToastGeneric</c> schema) of a <see cref="PrintNotification"/> with all texts in one
/// given language (a language switch while building cannot mix languages): printer name as the title, the event as the
/// body and the job file name as a third line (for "cooled down", T-080: the temperatures instead).
/// Printer and file names come from the user and the printer: they are stripped of characters XML cannot carry,
/// shortened and escaped, so they can never add markup (actions, protocol launches...).
/// </summary>
internal static class ToastContentBuilder
{
    /// <summary>Longest text line; toasts show about three lines anyway and the whole payload is limited (5 kB).</summary>
    public const int MaxTextLength = 200;

    /// <param name="textCulture">Language of the texts (the app passes <c>UiLanguage.CurrentTextCulture()</c>); numbers follow <c>CurrentCulture</c>.</param>
    public static string Build(PrintNotification notification, CultureInfo textCulture)
    {
        NotificationLines lines = Lines(notification, textCulture);

        StringBuilder xml = new(512);
        xml.Append("<toast");
        // An id too long to encode leaves the toast without a click target (the service logs it); it is still shown.
        if (ToastLaunchArgument.TryForPrinter(notification.Printer.Id, out string? launch))
        {
            xml.Append(" launch=\"");
            AppendEscaped(xml, launch);
            xml.Append('"');
        }

        // Foreground activation: the click is delivered to the running app (ToastNotification.Activated).
        xml.Append(" activationType=\"foreground\"><visual><binding template=\"ToastGeneric\">");
        AppendText(xml, lines.Title);
        AppendText(xml, lines.Body);
        if (lines.Detail is { } detail)
        {
            AppendText(xml, detail);
        }

        xml.Append("</binding></visual>");
        if (!notification.PlaySound)
        {
            xml.Append("<audio silent=\"true\"/>");
        }

        // Without an <audio> element Windows plays the default notification sound.
        xml.Append("</toast>");
        return xml.ToString();
    }

    /// <summary>
    /// The toast of the test notification (T-101) in <paramref name="textCulture"/>: the same shape as a printer toast (title
    /// and one line, foreground activation), with the launch argument <see cref="ToastLaunchArgument.Settings"/>.
    /// </summary>
    public static string BuildTest(CultureInfo textCulture, bool playSound)
    {
        ArgumentNullException.ThrowIfNull(textCulture);
        StringBuilder xml = new(384);
        xml.Append("<toast launch=\"");
        AppendEscaped(xml, ToastLaunchArgument.Settings);
        xml.Append("\" activationType=\"foreground\"><visual><binding template=\"ToastGeneric\">");
        AppendText(xml, TestNotification.Title(textCulture));
        AppendText(xml, TestNotification.Body(textCulture));
        xml.Append("</binding></visual>");
        if (!playSound)
        {
            xml.Append("<audio silent=\"true\"/>");
        }

        xml.Append("</toast>");
        return xml.ToString();
    }

    /// <summary>
    /// The texts of a notification in <paramref name="textCulture"/>, the same on every OS (Windows toast, macOS
    /// notification, T-020): the printer name (its id when the name is empty) as the title, the event as the body and the
    /// job file name, or for "cooled down" (T-080) the temperatures, as the detail. Printer and file names are stripped of
    /// control characters and characters XML cannot carry, and shortened to <see cref="MaxTextLength"/>; not escaped.
    /// </summary>
    internal static NotificationLines Lines(PrintNotification notification, CultureInfo textCulture)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(textCulture);

        string title = Clean(notification.Printer.Name);
        if (title.Length == 0)
        {
            title = Clean(notification.Printer.Id);
        }

        if (notification is { Kind: PrintEventKind.CooledDown, Cooldown: { } cooled })
        {
            // T-080: how the print ended and that the printer cooled down, then the temperatures instead of the file name.
            return new NotificationLines(
                title,
                CooledDownText(cooled.Outcome, textCulture),
                Format(Text(nameof(Strings.NotificationCooledDownDetail), textCulture), WholeDegrees(cooled.NozzleCelsius), cooled.ThresholdCelsius));
        }

        string? fileName = string.IsNullOrWhiteSpace(notification.FileName) ? null : Clean(notification.FileName);
        return new NotificationLines(
            title,
            EventText(notification.Kind, notification.Remaining, textCulture),
            !string.IsNullOrEmpty(fileName) && notification.Kind != PrintEventKind.CooledDown ? fileName : null);
    }

    /// <summary>The body line describing the event.</summary>
    internal static string EventText(PrintEventKind kind, TimeSpan? remaining, CultureInfo culture) => kind switch
    {
        PrintEventKind.PrintStarted => Text(nameof(Strings.NotificationPrintStarted), culture),
        PrintEventKind.PrintFinished => Text(nameof(Strings.NotificationPrintFinished), culture),
        PrintEventKind.PrintPaused => Text(nameof(Strings.NotificationPrintPaused), culture),
        PrintEventKind.PrintResumed => Text(nameof(Strings.NotificationPrintResumed), culture),
        PrintEventKind.PrintStopped => Text(nameof(Strings.NotificationPrintStopped), culture),
        PrintEventKind.Error => Text(nameof(Strings.NotificationError), culture),
        PrintEventKind.AttentionRequired => Text(nameof(Strings.NotificationAttention), culture),
        PrintEventKind.CredentialsRejected => Text(nameof(Strings.NotificationCredentialsRejected), culture),
        PrintEventKind.PrinterOffline => Text(nameof(Strings.NotificationOffline), culture),
        PrintEventKind.PrinterOnline => Text(nameof(Strings.NotificationOnline), culture),
        PrintEventKind.RemainingTime when remaining is { } time =>
            Format(Text(nameof(Strings.NotificationRemainingTime), culture), TrayToolTipFormatter.FormatDurationIn(time, culture)),
        PrintEventKind.RemainingTime => Text(nameof(Strings.NotificationAlmostFinished), culture),
        PrintEventKind.CooledDown => Text(nameof(Strings.NotificationCooledDown), culture),
        _ => Text(nameof(Strings.NotificationStatusChanged), culture),
    };

    /// <summary>The body line of the cooled-down toast: "Print finished, printer cooled down" / "Print stopped, …".</summary>
    private static string CooledDownText(CooldownPrintOutcome outcome, CultureInfo culture) => outcome switch
    {
        CooldownPrintOutcome.Finished => Text(nameof(Strings.NotificationCooledDownFinished), culture),
        CooldownPrintOutcome.Stopped => Text(nameof(Strings.NotificationCooledDownStopped), culture),
        _ => Text(nameof(Strings.NotificationCooledDown), culture),
    };

    /// <summary>
    /// The nozzle reading in whole degrees, like the threshold; it is at or below the threshold, so rounding never shows it
    /// above. Defensive bounds only: the cool-down monitor sends plausible finite readings.
    /// </summary>
    private static int WholeDegrees(double celsius) =>
        double.IsFinite(celsius) ? (int)Math.Clamp(Math.Round(celsius, MidpointRounding.AwayFromZero), -1000, 1000) : 0;

    /// <summary>A resource text in exactly <paramref name="culture"/>, regardless of the chosen language (<see cref="Strings.Culture"/>).</summary>
    private static string Text(string name, CultureInfo culture) => Strings.ResourceManager.GetString(name, culture) ?? name;

    private static string Format(string format, object? arg0) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);

    private static void AppendText(StringBuilder xml, string text)
    {
        xml.Append("<text>");
        AppendEscaped(xml, text);
        xml.Append("</text>");
    }

    /// <summary>Removes characters XML 1.0 cannot contain (control characters, lone surrogates...) and shortens.</summary>
    private static string Clean(string text)
    {
        StringBuilder? cleaned = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            int length = IsXmlChar(c) ? 1
                : i + 1 < text.Length && char.IsSurrogatePair(c, text[i + 1]) ? 2
                : 0;
            if (length == 0)
            {
                // First invalid character: copy what was valid so far.
                cleaned ??= new StringBuilder(text, 0, i, text.Length);
                continue;
            }

            cleaned?.Append(text, i, length);
            i += length - 1;
        }

        string result = (cleaned?.ToString() ?? text).Trim();
        return TrayText.Ellipsize(result, MaxTextLength);
    }

    /// <summary>XML 1.0 Char production for a single UTF-16 unit (surrogate pairs are checked by the caller).</summary>
    private static bool IsXmlChar(char c) =>
        c is '\t' or '\n' or '\r'
        || (c >= ' ' && c <= '퟿')
        || (c >= '' && c <= '�');

    private static void AppendEscaped(StringBuilder xml, string text)
    {
        foreach (char c in text)
        {
            _ = c switch
            {
                '&' => xml.Append("&amp;"),
                '<' => xml.Append("&lt;"),
                '>' => xml.Append("&gt;"),
                '"' => xml.Append("&quot;"),
                '\'' => xml.Append("&apos;"),
                _ => xml.Append(c),
            };
        }
    }
}

/// <summary>The texts of one notification (<see cref="ToastContentBuilder.Lines"/>).</summary>
/// <param name="Detail">The job file name or the temperatures; <see langword="null"/>: none.</param>
internal sealed record NotificationLines(string Title, string Body, string? Detail);
