using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Settings;

public class AppSettingsTests
{
    [Fact]
    public void DefaultHasFiveSecondPollingAndNoPrinters()
    {
        AppSettings settings = AppSettings.Default;

        Assert.Equal(TimeSpan.FromSeconds(5), settings.PollingInterval);
        Assert.Empty(settings.Printers);
        Assert.True(settings.Notifications.Enabled);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(300.001)]
    public void PollingIntervalOutsideAllowedRangeIsRejected(double seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { PollingInterval = TimeSpan.FromSeconds(seconds) });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(300)]
    public void PollingIntervalInsideAllowedRangeIsAccepted(double seconds)
    {
        AppSettings settings = new() { PollingInterval = TimeSpan.FromSeconds(seconds) };

        Assert.Equal(TimeSpan.FromSeconds(seconds), settings.PollingInterval);
    }

    [Fact]
    public void PrintersWithDuplicateIdsAreRejectedIgnoringCase()
    {
        Assert.Throws<ArgumentException>(() => new AppSettings { Printers = [Printer("mk4"), Printer("MK4")] });
    }

    [Fact]
    public void PrintersNullEntryIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new AppSettings { Printers = [Printer("mk4"), null!] });
    }

    [Fact]
    public void PrintersAreDefensivelyCopied()
    {
        List<PrinterDefinition> source = [Printer("mk4")];
        AppSettings settings = new() { Printers = source };

        source.Add(Printer("xl"));

        Assert.Single(settings.Printers);
    }

    [Fact]
    public void PrintersCannotBeMutatedThroughCast()
    {
        AppSettings settings = new() { Printers = [Printer("mk4")] };

        IList<PrinterDefinition> list = Assert.IsAssignableFrom<IList<PrinterDefinition>>(settings.Printers);
        Assert.Throws<NotSupportedException>(() => list[0] = Printer("xl"));
        Assert.Throws<NotSupportedException>(() => list.Add(Printer("xl")));
    }

    [Fact]
    public void EqualityComparesPrintersByValue()
    {
        AppSettings first = new() { PollingInterval = TimeSpan.FromSeconds(7), Printers = [Printer("mk4"), Printer("xl")] };
        AppSettings second = new() { PollingInterval = TimeSpan.FromSeconds(7), Printers = [Printer("mk4"), Printer("xl")] };

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Printers = [Printer("xl"), Printer("mk4")] });
        Assert.NotEqual(first, second with { Notifications = new NotificationSettings { Error = false } });
        Assert.NotEqual(first, second with { PollingInterval = TimeSpan.FromSeconds(8) });
        Assert.False(first.Equals(null));
    }

    [Fact]
    public void LanguageDefaultsToSystem()
    {
        Assert.Null(AppSettings.Default.Language);
        Assert.Null(new AppSettings().Language);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    [InlineData("sk")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("pl")]
    public void SupportedLanguageIsAccepted(string code)
    {
        AppSettings settings = new() { Language = code };

        Assert.Equal(code, settings.Language);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("DE")]
    [InlineData(" de")]
    [InlineData("de-AT")]
    [InlineData("ja")]
    [InlineData("system")]
    public void UnsupportedOrNonCanonicalLanguageIsRejected(string code)
    {
        Assert.Throws<ArgumentException>(() => new AppSettings { Language = code });
    }

    [Fact]
    public void DebugLoggingIsOffByDefaultAndPartOfEquality()
    {
        AppSettings detailed = new() { DebugLogging = true };

        Assert.False(AppSettings.Default.DebugLogging);
        Assert.NotEqual(new AppSettings(), detailed);
        Assert.Equal(detailed, new AppSettings { DebugLogging = true });
        Assert.Equal(detailed.GetHashCode(), new AppSettings { DebugLogging = true }.GetHashCode());
    }

    [Fact]
    public void PrereleaseChoiceIsUnsetByDefaultAndPartOfEquality()
    {
        Assert.Null(AppSettings.Default.OfferPrereleaseUpdates);
        Assert.NotEqual(new AppSettings(), new AppSettings { OfferPrereleaseUpdates = true });
        Assert.NotEqual(new AppSettings(), new AppSettings { OfferPrereleaseUpdates = false });
        Assert.NotEqual(new AppSettings { OfferPrereleaseUpdates = true }, new AppSettings { OfferPrereleaseUpdates = false });
        Assert.Equal(new AppSettings { OfferPrereleaseUpdates = false }, new AppSettings { OfferPrereleaseUpdates = false });
        Assert.Equal(new AppSettings { OfferPrereleaseUpdates = true }.GetHashCode(), new AppSettings { OfferPrereleaseUpdates = true }.GetHashCode());
    }

    [Fact]
    public void EqualityIncludesLanguage()
    {
        AppSettings german = new() { Language = "de" };

        Assert.NotEqual(new AppSettings(), german);
        Assert.Equal(german, new AppSettings { Language = "de" });
        Assert.Equal(german.GetHashCode(), new AppSettings { Language = "de" }.GetHashCode());
        Assert.NotEqual(german, german with { Language = "pl" });
    }

    [Fact]
    public void SupportedLanguagesAreTheTranslatedOnes()
    {
        Assert.Equal(["en", "cs", "sk", "de", "es", "fr", "it", "pl"], AppLanguages.Supported);
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData(" DE ", "de")]
    [InlineData("Pl", "pl")]
    public void LanguageCodeIsNormalized(string value, string expected)
    {
        Assert.True(AppLanguages.TryNormalize(value, out string? code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("de-AT")]
    [InlineData("deutsch")]
    [InlineData("ja")]
    public void UnknownLanguageCodeIsNotNormalized(string value)
    {
        Assert.False(AppLanguages.TryNormalize(value, out string? code));
        Assert.Null(code);
    }

    private static PrinterDefinition Printer(string id) =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };
}
