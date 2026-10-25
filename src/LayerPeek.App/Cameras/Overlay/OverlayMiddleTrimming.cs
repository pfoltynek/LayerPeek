using System.Runtime.InteropServices;

using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// Shortens a line that does not fit <b>in the middle</b> (T-076 1.4, the T-036 rule "start + … + end" applied to the
/// width): the first <see cref="PrefixLength"/> characters, "…" and as much of the end as fits. Names keep both their start
/// and their end ("Foldable book h…holder v2"), where versions and variants usually are. A prefix wider than the line is cut
/// with the "…" at the end.
/// </summary>
/// <remarks>
/// Built on Avalonia's <see cref="TextLeadingPrefixCharacterEllipsis"/> (12.1.4), which keeps whole glyph clusters on both
/// sides (the prefix counts UTF-16 code units and rounds up to the end of the cluster). Its end is wrong, though, when the
/// line has several runs (emoji or CJK drawn in a fallback font): after a run that fits only partly it keeps collecting
/// pieces of the runs before it, so it draws text that is not the end of the name ("…e 打印 v2" for "… cube 打印打印 v2").
/// <see cref="MiddleEllipsis"/> keeps only the part of that end that really continues to the end of the line.
/// </remarks>
public sealed class OverlayMiddleTrimming : TextTrimming
{
    /// <summary>Characters kept at the start: about half of the narrowest title line (M, 212 DIP at 14 semi-bold).</summary>
    public const int DefaultPrefixLength = 14;

    private const string Ellipsis = "…";

    public OverlayMiddleTrimming(int prefixLength = DefaultPrefixLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
        PrefixLength = prefixLength;
    }

    /// <summary>The shared instance for XAML (<c>{x:Static ov:OverlayMiddleTrimming.Middle}</c>).</summary>
    public static OverlayMiddleTrimming Middle { get; } = new();

    public int PrefixLength { get; }

    public override TextCollapsingProperties CreateCollapsingProperties(TextCollapsingCreateInfo createInfo) =>
        new MiddleEllipsis(new TextLeadingPrefixCharacterEllipsis(Ellipsis, PrefixLength, createInfo.Width, createInfo.TextRunProperties, createInfo.FlowDirection));

    public override string ToString() => nameof(OverlayMiddleTrimming);

    /// <summary>
    /// The runs of a collapsed line (prefix, "…", end) with only the part of the end that is contiguous with the end of the
    /// line: from the last run backwards, each run must end where the next one starts. The rest of the end is left out
    /// (the line shows less of the end, never text that is not there). Runs are located in the text by their memory, so a
    /// line whose text is not one string (inlines) is returned unchanged.
    /// </summary>
    private static TextRun[] KeepContiguousEnd(TextLine line, TextRun[] collapsed)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(collapsed);
        string? text = null;
        int lineEnd = -1;
        foreach (TextRun run in line.TextRuns)
        {
            if (run.Length == 0 || !MemoryMarshal.TryGetString(run.Text, out string? runText, out int start, out int length))
            {
                continue;
            }

            if (text is null)
            {
                text = runText;
            }
            else if (!ReferenceEquals(text, runText))
            {
                return collapsed;
            }

            lineEnd = Math.Max(lineEnd, start + length);
        }

        if (text is null)
        {
            return collapsed;
        }

        // The "…" is the first run that is not a piece of the line's text; the end follows it.
        int symbol = Array.FindIndex(collapsed, run => !IsPieceOf(run, text, out _, out _));
        if (symbol < 0)
        {
            return collapsed;
        }

        int expectedEnd = lineEnd;
        int first = collapsed.Length;
        while (first > symbol + 1 && IsPieceOf(collapsed[first - 1], text, out int start, out int length) && start + length == expectedEnd)
        {
            expectedEnd = start;
            first--;
        }

        if (first == symbol + 1)
        {
            return collapsed;
        }

        TextRun[] kept = new TextRun[symbol + 1 + collapsed.Length - first];
        Array.Copy(collapsed, kept, symbol + 1);
        Array.Copy(collapsed, first, kept, symbol + 1, collapsed.Length - first);
        return kept;
    }

    private static bool IsPieceOf(TextRun run, string text, out int start, out int length)
    {
        if (MemoryMarshal.TryGetString(run.Text, out string? runText, out start, out length) && ReferenceEquals(runText, text))
        {
            return true;
        }

        start = length = 0;
        return false;
    }

    /// <summary><see cref="TextLeadingPrefixCharacterEllipsis"/> whose end is cut back to the part contiguous with the line's end.</summary>
    private sealed class MiddleEllipsis(TextLeadingPrefixCharacterEllipsis inner) : TextCollapsingProperties
    {
        public override double Width => inner.Width;

        public override TextRun Symbol => inner.Symbol;

        public override FlowDirection FlowDirection => inner.FlowDirection;

        public override TextRun[]? Collapse(TextLine textLine) =>
            inner.Collapse(textLine) is { } collapsed ? KeepContiguousEnd(textLine, collapsed) : null;
    }
}
