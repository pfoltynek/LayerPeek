using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

using PrusaTrayDiagnostic.App.Resources;

namespace PrusaTrayDiagnostic.App.Tests.Localization;

public sealed partial class StringsTests
{
    [Fact]
    public void CzechTranslatesEveryNeutralString()
    {
        Dictionary<string, string> neutral = Load(CultureInfo.InvariantCulture);
        Dictionary<string, string> czech = Load(CultureInfo.GetCultureInfo("cs"));

        Assert.NotEmpty(neutral);
        Assert.Equal(neutral.Keys.Order(StringComparer.Ordinal), czech.Keys.Order(StringComparer.Ordinal));
        foreach ((string key, string text) in czech)
        {
            Assert.False(string.IsNullOrWhiteSpace(text), $"'{key}' is empty in Czech.");
            Assert.Equal(Placeholders(neutral[key]), Placeholders(text));
        }
    }

    [Fact]
    public void StronglyTypedAccessFollowsUiCulture()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("cs-CZ");
            Assert.Equal("Ukončit", Strings.MenuQuit);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            Assert.Equal("Quit", Strings.MenuQuit);

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
            Assert.Equal("Quit", Strings.MenuQuit);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
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
