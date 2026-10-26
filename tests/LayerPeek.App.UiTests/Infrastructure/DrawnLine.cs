using System.Globalization;

using Avalonia.Controls;

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>
/// What a <see cref="TextBlock"/> really draws on a line after trimming (its text runs, including the "…"), and checks
/// that a shortened line is made of whole user-perceived characters of the original text.
/// </summary>
public static class DrawnLine
{
    /// <summary>The text <paramref name="block"/> draws on its line <paramref name="line"/> (after shortening).</summary>
    public static string Of(TextBlock block, int line = 0)
    {
        ArgumentNullException.ThrowIfNull(block);
        return string.Concat(block.TextLayout.TextLines[line].TextRuns.Select(static run => run.Text.ToString()));
    }

    /// <summary>
    /// The drawn line as pieces of <paramref name="block"/>'s text in LOGICAL order (the runs of a line are kept in visual
    /// order, so right-to-left text comes reversed), plus the texts of the runs that are not part of it (the "…").
    /// </summary>
    public static (List<Range> Pieces, List<string> Others) Logical(TextBlock block, int line = 0)
    {
        ArgumentNullException.ThrowIfNull(block);
        string text = block.Text ?? string.Empty;
        List<Range> pieces = [];
        List<string> others = [];
        foreach (Avalonia.Media.TextFormatting.TextRun run in block.TextLayout.TextLines[line].TextRuns)
        {
            if (run.Length == 0)
            {
                continue;
            }

            if (System.Runtime.InteropServices.MemoryMarshal.TryGetString(run.Text, out string? runText, out int start, out int length) && ReferenceEquals(runText, text))
            {
                pieces.Add(new Range(start, start + length));
            }
            else
            {
                others.Add(run.Text.ToString());
            }
        }

        pieces.Sort(static (a, b) => a.Start.Value.CompareTo(b.Start.Value));
        return (pieces, others);
    }

    /// <summary>
    /// Checks a line shortened in the middle in logical order: exactly one "…" that is not part of the text, a start from
    /// the text's start and an end up to the text's end (each contiguous, no overlap), both cut between whole characters.
    /// Returns the kept start and end; the failure message names <paramref name="context"/>.
    /// </summary>
    public static (string Start, string End) AssertMiddleShortened(TextBlock block, string context)
    {
        ArgumentNullException.ThrowIfNull(block);
        string text = block.Text ?? string.Empty;
        (List<Range> pieces, List<string> others) = Logical(block);
        string drawn = Of(block);
        Xunit.Assert.True(others.Count == 1 && others[0] == "…", $"{context}: '{drawn}' does not have exactly one '…' run ({others.Count})");
        int startEnd = 0;
        int index = 0;
        while (index < pieces.Count && pieces[index].Start.Value == startEnd)
        {
            startEnd = pieces[index].End.Value;
            index++;
        }

        int endStart = index < pieces.Count ? pieces[index].Start.Value : text.Length;
        int expected = endStart;
        for (; index < pieces.Count; index++)
        {
            Xunit.Assert.True(pieces[index].Start.Value == expected, $"{context}: '{drawn}' has a gap or an overlap at {pieces[index]} in its end");
            expected = pieces[index].End.Value;
        }

        Xunit.Assert.True(expected == text.Length, $"{context}: '{drawn}' does not end with the text's end ({expected} of {text.Length})");
        Xunit.Assert.True(startEnd < endStart, $"{context}: '{drawn}' keeps everything ({startEnd}, {endStart})");
        string[] characters = TextElements(text);
        string start = text[..startEnd];
        string end = text[endStart..];
        Xunit.Assert.True(IsWholeCharacterPrefix(characters, start), $"{context}: '{drawn}': the start is not whole characters");
        Xunit.Assert.True(IsWholeCharacterSuffix(characters, end), $"{context}: '{drawn}': the end is not whole characters");
        return (start, end);
    }

    /// <summary>The grapheme clusters of <paramref name="text"/>.</summary>
    public static string[] TextElements(string text)
    {
        List<string> elements = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        return [.. elements];
    }

    /// <summary>Whether <paramref name="head"/> is the first whole characters of the text made of <paramref name="characters"/>.</summary>
    public static bool IsWholeCharacterPrefix(string[] characters, string head)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(head);
        int length = 0;
        foreach (string character in characters)
        {
            if (length == head.Length)
            {
                break;
            }

            length += character.Length;
        }

        return length == head.Length && string.Concat(characters).StartsWith(head, StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="tail"/> is the last whole characters of the text made of <paramref name="characters"/>.</summary>
    public static bool IsWholeCharacterSuffix(string[] characters, string tail)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(tail);
        int length = 0;
        for (int i = characters.Length - 1; i >= 0 && length < tail.Length; i--)
        {
            length += characters[i].Length;
        }

        return length == tail.Length && string.Concat(characters).EndsWith(tail, StringComparison.Ordinal);
    }
}
