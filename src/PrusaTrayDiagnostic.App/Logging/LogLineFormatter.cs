using System.Buffers;
using System.Globalization;
using System.Text;

using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Logging;

/// <summary>
/// One record of the log file: <c>2026-10-21 14:03:12.345 +02:00 INF [pid 12345] Category: message</c>, then the whole
/// exception (type, message, inner exceptions, stack trace) on indented lines. Every line break inside the message or
/// the exception starts an indented line, so each record, and only a record, starts at the beginning of a line with its
/// time stamp. Lines end with <c>\n</c>.
/// </summary>
internal static class LogLineFormatter
{
    /// <summary>Starts every continuation line of a record.</summary>
    public const string Indent = "    ";

    /// <summary>CR, LF and the Unicode line breaks some viewers honour: none of them may start a line that looks like a record.</summary>
    private static readonly SearchValues<char> LineBreaks = SearchValues.Create("\r\n\u0085\u2028\u2029");

    public static string Format(DateTimeOffset time, LogLevel level, int processId, string category, string message, Exception? exception)
    {
        StringBuilder record = new(capacity: 64 + category.Length + message.Length);
        record.Append(CultureInfo.InvariantCulture, $"{time:yyyy-MM-dd HH:mm:ss.fff zzz} {LevelName(level)} [pid {processId}] {category}: ");
        AppendIndented(record, message);
        if (exception is not null)
        {
            record.Append('\n').Append(Indent);
            AppendIndented(record, exception.ToString());
        }

        return record.Append('\n').ToString();
    }

    /// <summary>Three letters, so that the columns after it line up.</summary>
    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    /// <summary>Appends <paramref name="text"/> without trailing white space, every line break (CRLF counts once) as <c>\n</c> + <see cref="Indent"/>.</summary>
    private static void AppendIndented(StringBuilder record, string text)
    {
        ReadOnlySpan<char> rest = text.AsSpan().TrimEnd();
        int lineBreak;
        while ((lineBreak = rest.IndexOfAny(LineBreaks)) >= 0)
        {
            record.Append(rest[..lineBreak]).Append('\n').Append(Indent);
            int next = rest[lineBreak] == '\r' && lineBreak + 1 < rest.Length && rest[lineBreak + 1] == '\n' ? lineBreak + 2 : lineBreak + 1;
            rest = rest[next..];
        }

        record.Append(rest);
    }
}
