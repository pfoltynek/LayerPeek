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

    [Fact]
    public void ShorteningANameNeverSplitsASurrogatePair()
    {
        string emoji = "\U0001F600";
        string name = new string('a', 7) + emoji + new string('b', 30) + emoji + new string('c', 30);

        string shown = OverlayFormat.ShortenName(name);

        Assert.True(shown.Length <= OverlayFormat.MaxNameLength);
        Assert.Single(shown, static c => c == '…');
        Assert.DoesNotContain(shown, static c => char.IsSurrogate(c));
        Assert.StartsWith(new string('a', 7) + "…", shown, StringComparison.Ordinal);
        Assert.EndsWith(new string('c', 30), shown, StringComparison.Ordinal);
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
