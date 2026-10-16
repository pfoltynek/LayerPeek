using System.Globalization;
using System.Text;

using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Notifications;

namespace PrusaTrayDiagnostic.App.Notifications;

/// <summary>
/// Builds the toast XML (Windows <c>ToastGeneric</c> schema) of a <see cref="PrintNotification"/> in the current UI
/// culture: printer name as the title, the event as the body and the job file name as a third line.
/// Printer and file names come from the user and the printer: they are stripped of characters XML cannot carry,
/// shortened and escaped, so they can never add markup (actions, protocol launches...).
/// </summary>
internal static class ToastContentBuilder
{
    /// <summary>Longest text line; toasts show about three lines anyway and the whole payload is limited (5 kB).</summary>
    public const int MaxTextLength = 200;

    public static string Build(PrintNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        string title = Clean(notification.Printer.Name);
        if (title.Length == 0)
        {
            title = Clean(notification.Printer.Id);
        }

        string? fileName = string.IsNullOrWhiteSpace(notification.FileName) ? null : Clean(notification.FileName);

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
        AppendText(xml, title);
        AppendText(xml, EventText(notification.Kind, notification.Remaining));
        if (!string.IsNullOrEmpty(fileName))
        {
            AppendText(xml, fileName);
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

    /// <summary>The body line describing the event.</summary>
    internal static string EventText(PrintEventKind kind, TimeSpan? remaining) => kind switch
    {
        PrintEventKind.PrintStarted => Strings.NotificationPrintStarted,
        PrintEventKind.PrintFinished => Strings.NotificationPrintFinished,
        PrintEventKind.PrintPaused => Strings.NotificationPrintPaused,
        PrintEventKind.PrintResumed => Strings.NotificationPrintResumed,
        PrintEventKind.PrintStopped => Strings.NotificationPrintStopped,
        PrintEventKind.Error => Strings.NotificationError,
        PrintEventKind.AttentionRequired => Strings.NotificationAttention,
        PrintEventKind.CredentialsRejected => Strings.NotificationCredentialsRejected,
        PrintEventKind.PrinterOffline => Strings.NotificationOffline,
        PrintEventKind.PrinterOnline => Strings.NotificationOnline,
        PrintEventKind.RemainingTime when remaining is { } time =>
            Format(Strings.NotificationRemainingTime, TrayToolTipFormatter.FormatDuration(time)),
        PrintEventKind.RemainingTime => Strings.NotificationAlmostFinished,
        _ => Strings.NotificationStatusChanged,
    };

    private static string Format(string format, object? arg0) =>
        string.Format(CultureInfo.CurrentCulture, format, arg0);

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
