using System.Globalization;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Localization;
using LayerPeek.App.Tray.Menu;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Tray.Menu;

/// <summary>"User manual" (T-172): the manual on <c>main</c> in the language of the texts, English for any other.</summary>
public sealed class UserManualTests
{
    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    [Theory]
    [MemberData(nameof(Languages))]
    public void EverySupportedLanguageHasItsManual(string language)
    {
        Uri uri = UserManual.UriFor(CultureInfo.GetCultureInfo(language));

        Assert.Equal($"https://github.com/pfoltynek/LayerPeek/blob/main/manuals/{language}/manual.md", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("de-AT", "de")]
    [InlineData("cs-CZ", "cs")]
    [InlineData("en-GB", "en")]
    [InlineData("pt-BR", "en")]
    [InlineData("ja", "en")]
    [InlineData("", "en")]
    public void RegionalVariantsUseTheirLanguageAndOthersEnglish(string culture, string language)
    {
        Uri uri = UserManual.UriFor(CultureInfo.GetCultureInfo(culture));

        Assert.EndsWith($"/manuals/{language}/manual.md", uri.AbsoluteUri, StringComparison.Ordinal);
    }
}

/// <summary>The manual follows the applied language of the texts (<see cref="Strings.Culture"/>, process-wide).</summary>
[Collection(nameof(GlobalLanguageGroup))]
public sealed class UserManualLanguageTests : IDisposable
{
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        Strings.Culture = null;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    [Theory]
    [InlineData("de-AT", null, "de")]
    [InlineData("ja-JP", null, "en")]
    [InlineData("de-AT", "pl", "pl")]
    [InlineData("ja-JP", "sk", "sk")]
    [InlineData("cs-CZ", "en", "en")]
    public void TheManualIsInTheLanguageOfTheTexts(string system, string? chosen, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(system);
        new UiLanguage().Apply(chosen);

        Uri uri = UserManual.UriFor(UiLanguage.CurrentTextCulture());

        Assert.EndsWith($"/manuals/{expected}/manual.md", uri.AbsoluteUri, StringComparison.Ordinal);
    }
}
