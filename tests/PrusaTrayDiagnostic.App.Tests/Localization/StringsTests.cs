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
            _ = string.Format(CultureInfo.InvariantCulture, text, "a", "b", "c", "d");
        }
    }

    public static TheoryData<string> AllCultures => new(["", .. TranslatedCultures]);

    /// <summary>
    /// A resolution ("1920 × 1080 px") never wraps (decision 2026-10-19, T-034): non-breaking spaces around "×" and before
    /// "px" in every text and language.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCultures))]
    public void ResolutionsUseNonBreakingSpaces(string culture)
    {
        CultureInfo info = culture.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);
        Dictionary<string, string> texts = Load(info);
        foreach ((string key, string text) in texts)
        {
            Assert.False(text.Contains(" ×", StringComparison.Ordinal) || text.Contains("× ", StringComparison.Ordinal), $"'{key}' in '{culture}' has a breaking space next to '×'.");
            Assert.False(ResolutionWithBreakingSpace().IsMatch(text), $"'{key}' in '{culture}' has a breaking space before 'px'.");
        }

        string success = string.Format(info, texts["SettingsCameraTestSuccess"], 1920, 1080);
        Assert.Contains("1920\u00A0×\u00A01080\u00A0px", success, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(\}|\d) px\b")]
    private static partial Regex ResolutionWithBreakingSpace();

    /// <summary>
    /// A number and its unit ("42 %", "215 °C", "215 / 215 °C") never break apart: where a language puts a space before
    /// "%" or "°C", it is a non-breaking one (T-036 review).
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCultures))]
    public void UnitsUseNonBreakingSpaces(string culture)
    {
        CultureInfo info = culture.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(culture);
        Dictionary<string, string> texts = Load(info);
        foreach ((string key, string text) in texts)
        {
            Assert.False(UnitWithBreakingSpace().IsMatch(text), $"'{key}' in '{culture}' has a breaking space before '%' or '°C'.");
        }

        Assert.DoesNotContain(' ', string.Format(info, texts["DiagnosticsTemperatureValue"], 215));
        Assert.EndsWith("215 °C", string.Format(info, texts["CameraOverlayTemperatureWithTarget"], 210, 215), StringComparison.Ordinal);
        Assert.DoesNotContain(' ', string.Format(info, texts["ToolTipProgress"], 42));
    }

    [GeneratedRegex(@"(\}|\d) (%|°C)")]
    private static partial Regex UnitWithBreakingSpace();

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
            _ = string.Format(CultureInfo.InvariantCulture, text, "a", "b", "c", "d");
        }
    }

    [Fact]
    public void StronglyTypedAccessFollowsUiCulture()
    {
        using (new UiCultureScope("cs-CZ"))
        {
            Assert.Equal("U_končit", Strings.MenuQuit);
        }

        using (new UiCultureScope("en-GB"))
        {
            Assert.Equal("_Quit", Strings.MenuQuit);
        }

        using (new UiCultureScope("ja-JP"))
        {
            Assert.Equal("_Quit", Strings.MenuQuit);
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

        Assert.NotEqual("_Quit", expected);
        Assert.Equal(expected, Strings.ResourceManager.GetString(nameof(Strings.MenuQuit), CultureInfo.GetCultureInfo(regional)));
    }

    [Fact]
    public void LocalizedStringsIndexerReturnsTheResource()
    {
        using (new UiCultureScope("cs-CZ"))
        {
            Assert.Equal("U_končit", LocalizedStrings.Instance[nameof(Strings.MenuQuit)]);
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
