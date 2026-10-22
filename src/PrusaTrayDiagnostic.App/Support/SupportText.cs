using System.Globalization;
using System.Text;

namespace PrusaTrayDiagnostic.App.Support;

/// <summary>
/// The text "Copy info to clipboard" puts into the clipboard (T-049): <see cref="SupportInfo"/> in full, then the last log
/// lines. The whole text stays within about <see cref="DefaultMaxBytes"/> (UTF-8): the oldest log lines are left out
/// first, the information about the environment never.
/// </summary>
internal static class SupportText
{
    public const int DefaultMaxBytes = 64 * 1024;

    public static string Compose(string info, IReadOnlyList<string> logLines, int maxBytes = DefaultMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(logLines);

        // Newest lines first until the budget is used up, then back into reading order.
        int budget = maxBytes - Encoding.UTF8.GetByteCount(info) - 128;
        int first = logLines.Count;
        while (first > 0)
        {
            int size = Encoding.UTF8.GetByteCount(logLines[first - 1]) + 1;
            if (size > budget)
            {
                break;
            }

            budget -= size;
            first--;
        }

        StringBuilder text = new(info);
        text.Append('\n');
        if (logLines.Count == 0)
        {
            return text.Append("Last log lines: none\n").ToString();
        }

        text.Append(CultureInfo.InvariantCulture, $"Last log lines ({logLines.Count - first}");
        if (first > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $", {first} older left out to stay within {maxBytes / 1024} KB");
        }

        text.Append("):\n");
        for (int i = first; i < logLines.Count; i++)
        {
            text.Append(logLines[i]).Append('\n');
        }

        return text.ToString();
    }
}
