using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Tests.Cameras.Overlay;

/// <summary>
/// The shared texts of the overlay widgets (T-036 rules, moved to the shared formatters of T-081): words in the app
/// language, numbers and times in the region, dates in the app language, long names shortened.
/// </summary>
public sealed class OverlayFormatTests : IDisposable
{
    private readonly CultureScope _culture = new("en-US");

    public void Dispose() => _culture.Dispose();

    [Fact]
    public void TextsInEnglish()
    {
        Assert.Equal("Printing", OverlayFormat.StateName(PrinterState.Printing));
        Assert.Equal("Ready", OverlayFormat.StateName(PrinterState.Idle));
        Assert.Equal("42%", OverlayFormat.Percent(42));
        Assert.Equal("1 h 5 min", OverlayFormat.Duration(65));
        Assert.Equal("1 h 5 min remaining", OverlayFormat.Remaining(65));
        Assert.Equal("Print time: 2 h 10 min", OverlayFormat.PrintTime(130));
        Assert.Equal("Done at 1:05 PM", OverlayFormat.Finish(new(new DateTime(2026, 10, 16, 13, 5, 0), false)));
        Assert.Equal("Done October 17 at 8:00 AM", OverlayFormat.Finish(new(new DateTime(2026, 10, 17, 8, 0, 0), true)));
        Assert.Equal("215 / 215 °C", OverlayFormat.Temperature(new(215, 215)));
        Assert.Equal("25 °C", OverlayFormat.Temperature(new(25, null)));
        Assert.Equal("-4 °C", OverlayFormat.Temperature(new(-4, null)));
    }

    [Fact]
    public void TextsFollowTheLanguageAndNumbersTheCulture()
    {
        using (new CultureScope("cs-CZ"))
        {
            Assert.Equal("Tiskne", OverlayFormat.StateName(PrinterState.Printing));
            Assert.Equal("42 %", OverlayFormat.Percent(42));
            Assert.Equal("Zbývá 1 h 5 min", OverlayFormat.Remaining(65));
            Assert.Equal("Hotovo v 13:05", OverlayFormat.Finish(new(new DateTime(2026, 10, 16, 13, 5, 0), false)));
            Assert.Equal("Hotovo 17. října v 8:00", OverlayFormat.Finish(new(new DateTime(2026, 10, 17, 8, 0, 0), true)));
            Assert.Equal("Doba tisku: 2 h 10 min", OverlayFormat.PrintTime(130));
            Assert.Equal("1 215 / 215 °C", OverlayFormat.Temperature(new(1215, 215)));
        }

        using (new CultureScope("de-DE"))
        {
            Assert.Equal("Fertig um 13:05", OverlayFormat.Finish(new(new DateTime(2026, 10, 16, 13, 5, 0), false)));
            Assert.Equal("Druckzeit: 2 h 10 min", OverlayFormat.PrintTime(130));
        }
    }

    [Fact]
    public void LanguageSwitchChangesTheWords()
    {
        string english = OverlayFormat.Remaining(65);

        using (new UiCultureScope("pl"))
        {
            Assert.StartsWith("Pozostało ", OverlayFormat.Remaining(65), StringComparison.Ordinal);
        }

        Assert.Equal("1 h 5 min remaining", english);
    }

    /// <summary>
    /// User decision 2026-10-20 (T-036 Q3): the words of the date (month name, day–month order) follow the app language,
    /// the time follows the region.
    /// </summary>
    [Theory]
    [InlineData("en-US", "cs", "Hotovo 17. října v 8:00 AM")]
    [InlineData("cs-CZ", "en", "Done October 17 at 8:00")]
    [InlineData("en-US", "de", "Fertig am 17. Oktober um 8:00 AM")]
    [InlineData("de-DE", "cs", "Hotovo 17. října v 08:00")]
    [InlineData("cs-CZ", "de", "Fertig am 17. Oktober um 8:00")]
    [InlineData("de-DE", "en", "Done October 17 at 08:00")]
    public void DateWordsFollowTheAppLanguageAndTheTimeTheRegion(string region, string language, string expected)
    {
        using (new CultureScope(region))
        using (new UiCultureScope(language))
        {
            Assert.Equal(expected, OverlayFormat.Finish(new(new DateTime(2026, 10, 17, 8, 0, 0), true)));
        }
    }

    // ---- Long names ----

    [Fact]
    public void LongFileNameKeepsItsStartAndEnd()
    {
        string shown = OverlayFormat.ShortenFileName("very_long_model_name_with_many_parts_0.4n_0.2mm_PETG_COREONE_12h34m.bgcode");

        Assert.Equal(OverlayFormat.MaxFileNameLength, shown.Length);
        Assert.Equal("very_long_model_name_wi…NE_12h34m.bgcode", shown);
    }

    [Theory]
    [InlineData("benchy.bgcode")]
    [InlineData("exactly_forty_characters_long_name.gcode")]
    public void ShortFileNameIsKept(string name)
    {
        Assert.True(name.Length <= OverlayFormat.MaxFileNameLength);
        Assert.Equal(name, OverlayFormat.ShortenFileName(name));
    }

    [Fact]
    public void ShorteningNeverSplitsASurrogatePair()
    {
        string emoji = "\U0001F600";
        string name = new string('a', 22) + emoji + new string('b', 30) + emoji + new string('c', 15) + ".gcode";

        string shown = OverlayFormat.ShortenFileName(name);

        Assert.True(shown.Length <= OverlayFormat.MaxFileNameLength);
        Assert.DoesNotContain(shown, static c => char.IsSurrogate(c));
        Assert.EndsWith(".gcode", shown, StringComparison.Ordinal);
        Assert.Contains('…', shown);
    }

    [Fact]
    public void ShorteningAFileNameKeepsSurrogatePairsWhole()
    {
        // Exact outputs: a pair across the cut of the start is left out with the start, a pair across the cut of the end
        // is left out with the end (shorter than the limit rather than a broken character).
        string emoji = "\U0001F600";

        Assert.Equal(
            new string('a', 22) + "…" + new string('c', 10) + ".gcode",
            OverlayFormat.ShortenFileName(new string('a', 22) + emoji + new string('b', 30) + new string('c', 10) + ".gcode"));
        Assert.Equal(
            new string('a', 23) + "…" + new string('c', 9) + ".gcode",
            OverlayFormat.ShortenFileName(new string('a', 40) + emoji + new string('c', 9) + ".gcode"));
    }

    [Fact]
    public void ShorteningAFileNameWhoseFirstCharacterIsLongerThanItsStartCutsThatCharacter()
    {
        // The first character (a letter with 30 combining marks, 31 code units) is longer than the kept start (23): moving
        // the cut down to the character boundary would leave no start at all, so the character is cut at 23 and the end
        // keeps its 16 characters.
        string first = "a" + string.Concat(Enumerable.Repeat("́", 30));
        string fileName = first + new string('b', 30) + new string('c', 10) + ".gcode";

        string shown = OverlayFormat.ShortenFileName(fileName);

        Assert.Equal(first[..23] + "…" + new string('c', 10) + ".gcode", shown);
    }

    [Fact]
    public void LongPrinterNameIsShortenedInTheMiddle()
    {
        // Start + "…" + end (T-076 1.4); the start is as long as the prefix that the width trimming of the card keeps, so a
        // name that is also too wide still shows a single "…" (the trimming removes this one first).
        string name = "Prusa CORE One in the workshop upstairs – second from the window";

        string shown = OverlayFormat.ShortenName(name);

        Assert.Equal(OverlayFormat.MaxNameLength, shown.Length);
        Assert.Equal("Prusa CO…stairs – second from the window", shown);
        Assert.Equal(OverlayFormat.NameHeadLength, shown.IndexOf('…', StringComparison.Ordinal));
        Assert.Equal("MK4", OverlayFormat.ShortenName("MK4"));
        Assert.Equal(new string('n', 40), OverlayFormat.ShortenName(new string('n', 40)));
    }

    [Theory]
    [InlineData("aaaaaaaa")] // the cut after 8 characters is between two characters
    [InlineData("aaaaaaa\U0001F600")] // a surrogate pair across the cut
    [InlineData("aaaaaaaé")] // "é" as a letter and a combining accent across the cut
    [InlineData("aaaaaa\U0001F469‍\U0001F467")] // an emoji sequence (woman + ZWJ + girl) across the cut
    public void ShortenedNameKeepsTheWholeCharacterAtTheCutOfItsStart(string head)
    {
        // The width trimming of the card keeps the first 8 UTF-16 code units rounded up to the end of the character
        // (glyph cluster) that contains the 8th. The start of the shortened name must end exactly there, otherwise the
        // trimming keeps a different start and both "…" are drawn ("aaaaaaa…" + "…" + end).
        string name = head + new string('b', 40) + new string('c', 40);

        string shown = OverlayFormat.ShortenName(name);

        Assert.Equal(head + "…" + new string('c', OverlayFormat.MaxNameLength - head.Length - 1), shown);
    }

    [Fact]
    public void ShortenedNameDoesNotStartItsEndInsideACharacter()
    {
        // The cut before the end lands on the combining accent of "é": the end starts after the whole "é", never with a lone
        // accent that would sit on the "…".
        string name = new string('a', 20) + "é" + new string('c', 30);

        string shown = OverlayFormat.ShortenName(name);

        Assert.Equal(new string('a', OverlayFormat.NameHeadLength) + "…" + new string('c', 30), shown);
    }

    [Fact]
    public void ShorteningANameWithOneHugeCharacterStaysWithinTheLimit()
    {
        // A letter with 60 combining marks is one character longer than the whole limit: it is cut, but never inside a
        // surrogate pair.
        string name = "a" + string.Concat(Enumerable.Repeat("́", 60)) + "\U0001F600" + new string('c', 30);

        string shown = OverlayFormat.ShortenName(name);

        Assert.True(shown.Length <= OverlayFormat.MaxNameLength);
        Assert.Single(shown, static c => c == '…');
        Assert.StartsWith("a", shown, StringComparison.Ordinal);
        Assert.EndsWith(new string('c', 30), shown, StringComparison.Ordinal);
        Assert.False(char.IsLowSurrogate(shown[shown.IndexOf('…', StringComparison.Ordinal) + 1]));
    }

    [Fact]
    public void ShortenedNamesAreReusedWhileTheRawNameIsUnchanged()
    {
        // Every poll brings a new string instance with the same text: the shortened one is not built again.
        OverlayTextCache cache = new();
        string first = cache.FileName(new string('f', 60) + ".bgcode");

        Assert.Same(first, cache.FileName(new string('f', 60) + ".bgcode"));
        string other = cache.FileName(new string('g', 60) + ".bgcode");
        Assert.NotEqual(first, other);
        Assert.Equal(OverlayFormat.ShortenFileName(new string('g', 60) + ".bgcode"), other);

        string name = cache.Name(new string('N', 100));
        Assert.Same(name, cache.Name(new string('N', 100)));
        Assert.Same(other, cache.FileName(new string('g', 60) + ".bgcode"));
    }
}
