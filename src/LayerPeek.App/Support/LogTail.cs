using System.Text;

using LayerPeek.App.Logging;

namespace LayerPeek.App.Support;

/// <summary>
/// The last lines of the log (T-049): from the current file, and when it is short after a rotation, from the files before
/// it (including one that a rotation is just moving). Only the end of each file is read (<see cref="MaxBytesPerFile"/>):
/// the support text is limited to about 64 KB anyway. Files are opened with full sharing, because the app keeps writing
/// them. A rotation at the very moment of reading may repeat or miss a few lines; nothing more.
/// </summary>
internal static class LogTail
{
    public const int DefaultLineCount = 200;

    /// <summary>The end of a file that is read at most; a file longer than that is the last one read.</summary>
    internal const int MaxBytesPerFile = 64 * 1024;

    /// <summary>At most <paramref name="lineCount"/> lines, oldest first; none when there is no log. Never throws for I/O.</summary>
    public static IReadOnlyList<string> Read(string? directory, int lineCount = DefaultLineCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineCount);
        List<string> lines = [];
        if (directory is null)
        {
            return lines;
        }

        foreach (string path in NewestFirst(directory))
        {
            if (lines.Count >= lineCount)
            {
                break;
            }

            if (!TryReadEnd(path, out string text, out bool startsWithLine, out bool wholeFile))
            {
                continue;
            }

            string[] fileLines = text.Split('\n');
            // The text usually ends with a line break; a part cut off at the start of the read range is not a whole line.
            int first = startsWithLine ? 0 : 1;
            int last = fileLines.Length - (fileLines[^1].Length == 0 ? 1 : 0);
            int take = Math.Min(lineCount - lines.Count, Math.Max(0, last - first));
            lines.InsertRange(0, fileLines[(last - take)..last]);
            if (!wholeFile)
            {
                // Older lines than the read range would leave a gap; they would not fit into the text anyway.
                break;
            }
        }

        return lines;
    }

    /// <summary>The current file, the one a rotation is moving, then the older files.</summary>
    private static IEnumerable<string> NewestFirst(string directory)
    {
        yield return LogFile.PathOf(directory, 0);
        yield return Path.Combine(directory, LogFile.RotatingFileName);
        for (int index = 1; index < LogFile.DefaultFileCount; index++)
        {
            yield return LogFile.PathOf(directory, index);
        }
    }

    /// <summary>
    /// The last <see cref="MaxBytesPerFile"/> bytes of <paramref name="path"/> as text; <paramref name="startsWithLine"/> when
    /// they start with a whole line (the whole file, or a range right after a line break), <paramref name="wholeFile"/> when
    /// nothing before them was left out.
    /// </summary>
    private static bool TryReadEnd(string path, out string text, out bool startsWithLine, out bool wholeFile)
    {
        text = string.Empty;
        startsWithLine = true;
        wholeFile = true;
        try
        {
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // One byte before the range tells whether it starts a line.
            long length = file.Length;
            long start = Math.Max(0, length - MaxBytesPerFile - 1);
            file.Position = start;
            byte[] buffer = new byte[length - start];
            file.ReadExactly(buffer);
            int skip = 0;
            if (start > 0)
            {
                startsWithLine = buffer[0] == (byte)'\n';
                wholeFile = false;
                skip = 1;
            }

            // A character cut at the start is in the dropped partial line; Windows line ends would leave a CR.
            text = Encoding.UTF8.GetString(buffer, skip, buffer.Length - skip).Replace("\r", string.Empty, StringComparison.Ordinal);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing (not rotated yet), deleted meanwhile or not readable: left out.
            return false;
        }
    }
}
