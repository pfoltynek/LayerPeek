using System.Globalization;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tray;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Cameras.Overlay;

/// <summary>
/// Texts of the overlay widgets, shared by all of them (rules of T-036): words in the app language (<see cref="Strings"/>),
/// numbers and times in the region (<c>CurrentCulture</c>). Call on the UI thread when the shown content changed or the
/// language switched, never per poll.
/// </summary>
public static class OverlayFormat
{
    /// <summary>Longest printer name shown; longer ones are shortened in the middle (<see cref="ShortenName"/>).</summary>
    public const int MaxNameLength = 40;

    /// <summary>
    /// UTF-16 code units kept at the start of a shortened printer name, rounded up to the end of the character that contains
    /// the last one: the same start that the middle width trimming of the cards keeps (<c>TextTrimming.PrefixCharacterEllipsis</c>
    /// in Avalonia 12 splits after 8 code units and rounds up to the end of the glyph cluster). A name that is also too wide
    /// for its card therefore loses this "…" first and shows a single one, never "start…middle…". Glyph clusters equal
    /// grapheme clusters except for font ligatures across the cut, which a text function cannot know.
    /// </summary>
    public const int NameHeadLength = 8;

    /// <summary>Longest file name shown; longer ones keep their start and end (extension) around "…".</summary>
    public const int MaxFileNameLength = 40;

    /// <summary>Characters kept at the end of a shortened file name (e.g. "_PLA_2h10m.bgcode").</summary>
    public const int FileNameTailLength = 16;

    private const string Ellipsis = "…";

    /// <summary>"Printing", "Ready", … (the tray's state names).</summary>
    public static string StateName(PrinterState? state) => TrayText.StateName(state);

    /// <summary>"42%" / "42 %" (by language).</summary>
    public static string Percent(int percent) => Format(Strings.ToolTipProgress, percent);

    /// <summary>"1 h 5 min"; clamped so that an absurd estimate cannot overflow <see cref="TimeSpan"/>.</summary>
    public static string Duration(long minutes) =>
        TrayToolTipFormatter.FormatDuration(TimeSpan.FromTicks(minutes < TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMinute ? Math.Max(minutes, 0) * TimeSpan.TicksPerMinute : TimeSpan.MaxValue.Ticks));

    /// <summary>"1 h 5 min remaining".</summary>
    public static string Remaining(long minutes) => Format(Strings.ToolTipRemaining, Duration(minutes));

    /// <summary>"Print time: 2 h 10 min".</summary>
    public static string PrintTime(long minutes) => Format(Strings.CameraOverlayPrintTime, Duration(minutes));

    /// <summary>"Done at 1:05 PM", or with the date on another day: "Done October 17 at 8:00 AM".</summary>
    public static string Finish(OverlayFinishTime finish)
    {
        string time = finish.Local.ToString("t", CultureInfo.CurrentCulture);
        return finish.IsOnAnotherDay
            ? Format(Strings.CameraOverlayFinishDateTime, MonthDay(finish.Local), time)
            : Format(Strings.CameraOverlayFinishTime, time);
    }

    /// <summary>"215 / 215 °C" while heating, "25 °C" with the heater off.</summary>
    public static string Temperature(OverlayTemperature temperature)
    {
        string actual = temperature.Actual.ToString("N0", CultureInfo.CurrentCulture);
        return temperature.Target is { } target
            ? Format(Strings.CameraOverlayTemperatureWithTarget, actual, target.ToString("N0", CultureInfo.CurrentCulture))
            : Format(Strings.DiagnosticsTemperatureValue, actual);
    }

    /// <summary>
    /// A printer name of at most <see cref="MaxNameLength"/> characters: longer ones keep the first
    /// <see cref="NameHeadLength"/> characters and the end around "…" (T-076 1.4 and 2.1).
    /// </summary>
    public static string ShortenName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return ShortenMiddle(name, MaxNameLength, NameHeadLength, headUpToCharacterEnd: true);
    }

    /// <summary>
    /// A file name of at most <see cref="MaxFileNameLength"/> characters: longer ones keep the start and the last
    /// <see cref="FileNameTailLength"/> characters (slicers put material, time and the extension there) around "…".
    /// </summary>
    public static string ShortenFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return ShortenMiddle(fileName, MaxFileNameLength, MaxFileNameLength - FileNameTailLength - Ellipsis.Length);
    }

    /// <summary>
    /// At most <paramref name="maxLength"/> UTF-16 code units: about the first <paramref name="head"/> and the end around "…".
    /// Both cuts keep whole user-perceived characters (grapheme clusters: surrogate pairs, letters with combining accents,
    /// emoji sequences). A cut inside a character moves the end of the start down (the end keeps its length) or up with
    /// <paramref name="headUpToCharacterEnd"/> (the end gets shorter), and the start of the end up.
    /// </summary>
    private static string ShortenMiddle(string text, int maxLength, int head, bool headUpToCharacterEnd = false)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        (int before, int after) = CharacterAround(text, head);
        int headEnd = headUpToCharacterEnd ? after : before;
        if (headEnd == 0 || headEnd > maxLength - Ellipsis.Length)
        {
            // One character longer than the whole limit (e.g. a letter with dozens of combining marks): cut it, only never
            // inside a surrogate pair.
            headEnd = char.IsHighSurrogate(text[head - 1]) ? head - 1 : head;
        }

        (_, int tailStart) = CharacterAround(text, text.Length - (maxLength - Math.Max(head, headEnd) - Ellipsis.Length));
        return string.Concat(text.AsSpan(0, headEnd), Ellipsis, text.AsSpan(tailStart));
    }

    /// <summary>
    /// The start and end of the character (grapheme cluster) that contains the code unit before <paramref name="index"/>
    /// and the one at it; both are <paramref name="index"/> when a character boundary is there.
    /// </summary>
    private static (int Before, int After) CharacterAround(string text, int index)
    {
        int start = 0;
        while (start < index)
        {
            int end = start + StringInfo.GetNextTextElementLength(text.AsSpan(start));
            if (end > index)
            {
                return (start, end);
            }

            start = end;
        }

        return (index, index);
    }

    /// <summary>
    /// "17. října" / "October 17" (user decision 2026-10-20, T-036 Q3): the words of a date are part of the sentence, so the
    /// month name <b>and</b> the day–month order come from the app language (<see cref="UiLanguage.CurrentTextCulture"/>);
    /// only the time next to it follows the region (<c>CurrentCulture</c>). Mixing a region pattern with month names of
    /// another language would give ungrammatical text (en-US "MMMM d" with Czech genitive names: "října 17").
    /// </summary>
    private static string MonthDay(DateTime date)
    {
        CultureInfo language = UiLanguage.CurrentTextCulture();
        // The neutral language ("cs") has no calendar data of its own on every platform; its default region does.
        CultureInfo culture = language.IsNeutralCulture ? CultureInfo.CreateSpecificCulture(language.Name) : language;
        return date.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
    }

    private static string Format(string format, object? arg0) => string.Format(CultureInfo.CurrentCulture, format, arg0);

    private static string Format(string format, object? arg0, object? arg1) => string.Format(CultureInfo.CurrentCulture, format, arg0, arg1);
}

/// <summary>
/// The last shortened printer name and file name with the raw text they came from (T-036): a poll brings a new string
/// instance with the same text, which then reuses the shortened one instead of allocating it again. One per widget; not
/// thread-safe (widgets use it while computing their content, under the lock of the overlay).
/// </summary>
public sealed class OverlayTextCache
{
    private (string Raw, string Short)? _name;
    private (string Raw, string Short)? _fileName;

    public string Name(string raw) => Get(ref _name, raw, OverlayFormat.ShortenName);

    public string FileName(string raw) => Get(ref _fileName, raw, OverlayFormat.ShortenFileName);

    private static string Get(ref (string Raw, string Short)? entry, string raw, Func<string, string> shorten)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (entry is { } last && string.Equals(last.Raw, raw, StringComparison.Ordinal))
        {
            return last.Short;
        }

        string shortened = shorten(raw);
        entry = (raw, shortened);
        return shortened;
    }
}
