using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tests.Localization;

public sealed partial class StringsTests
{
    /// <summary>Every translation (not English: it is the neutral resources).</summary>
    private static readonly string[] TranslatedCultures = ["cs", "sk", "de", "es", "fr", "it", "pl"];

    public static TheoryData<string> Translations => new(TranslatedCultures);

    [Theory]
    [MemberData(nameof(Translations))]
    public void TranslationHasExactlyTheNeutralKeysAndPlaceholders(string culture)
    {
        Dictionary<string, string> neutral = Load(CultureInfo.InvariantCulture);
        Dictionary<string, string> translated = Load(CultureInfo.GetCultureInfo(culture));

        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), translated.Keys.Order(StringComparer.Ordinal));
        foreach ((string key, string text) in translated)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"'{key}' is empty in {culture}.");
            Assert.True(
                Placeholders(neutral[key]).SequenceEqual(Placeholders(text)),
                $"'{key}' in {culture} has placeholders [{string.Join(", ", Placeholders(text))}] instead of [{string.Join(", ", Placeholders(neutral[key]))}].");
            // A malformed format string (e.g. a stray brace) would only throw when the text is shown.
            _ = string.Format(CultureInfo.InvariantCulture, text, "a", "b", "c");
        }
    }

    [Fact]
    public void EverySupportedLanguageExceptEnglishHasATranslation()
    {
        Assert.Equal(
            AppLanguages.Supported.Where(static code => code != "en").Order(StringComparer.Ordinal),
            TranslatedCultures.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NeutralFormatStringsAreValid()
    {
        foreach ((string key, string text) in Load(CultureInfo.InvariantCulture))
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"'{key}' is empty.");
            _ = string.Format(CultureInfo.InvariantCulture, text, "a", "b", "c");
        }
    }

    [Fact]
    public void StronglyTypedAccessFollowsUiCulture()
    {
        using (new UiCultureScope("cs-CZ"))
        {
            Assert.Equal("Ukončit", Strings.MenuQuit);
        }

        using (new UiCultureScope("en-GB"))
        {
            Assert.Equal("Quit", Strings.MenuQuit);
        }

        using (new UiCultureScope("ja-JP"))
        {
            Assert.Equal("Quit", Strings.MenuQuit);
        }
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("fr-CA", "fr")]
    [InlineData("es-MX", "es")]
    [InlineData("it-CH", "it")]
    [InlineData("pl-PL", "pl")]
    [InlineData("sk-SK", "sk")]
    public void RegionalVariantUsesItsLanguage(string regional, string language)
    {
        string expected = Strings.ResourceManager.GetString(nameof(Strings.MenuQuit), CultureInfo.GetCultureInfo(language))!;

        Assert.NotEqual("Quit", expected);
        Assert.Equal(expected, Strings.ResourceManager.GetString(nameof(Strings.MenuQuit), CultureInfo.GetCultureInfo(regional)));
    }

    [Fact]
    public void LocalizedStringsIndexerReturnsTheResource()
    {
        using (new UiCultureScope("cs-CZ"))
        {
            Assert.Equal("Ukončit", LocalizedStrings.Instance[nameof(Strings.MenuQuit)]);
        }

        // A typo must be visible instead of an empty label.
        Assert.Equal("NoSuchKey", LocalizedStrings.Instance["NoSuchKey"]);
    }

    private static Dictionary<string, string> Load(CultureInfo culture)
    {
        ResourceSet? set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);
        Dictionary<string, string> strings = new(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in set)
        {
            strings.Add((string)entry.Key, Assert.IsType<string>(entry.Value));
        }

        return strings;
    }

    private static string[] Placeholders(string text) =>
        [.. PlaceholderPattern().Matches(text).Select(match => match.Value).Distinct().Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\d+(?:[,:][^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}
