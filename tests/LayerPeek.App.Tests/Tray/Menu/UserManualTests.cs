using System.Globalization;
using System.Text;

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

    /// <summary>
    /// T-182: the "More information" link opens the chapter "Securing access to PrusaLink" (T-183) in the manual of the same
    /// language. Its anchor is the slug GitHub makes of a heading of that manual, so a renamed or renumbered heading fails
    /// here; the address is ASCII (the anchor percent-encoded).
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void TheSecurityChapterIsAHeadingOfTheManualOfItsLanguage(string language)
    {
        Uri uri = Assert.IsType<Uri>(UserManual.SecurityChapterUriFor(CultureInfo.GetCultureInfo(language)));

        string prefix = $"https://github.com/pfoltynek/LayerPeek/blob/main/manuals/{language}/manual.md#";
        Assert.StartsWith(prefix, uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.True(uri.AbsoluteUri.All(char.IsAscii), uri.AbsoluteUri);
        string anchor = Uri.UnescapeDataString(uri.AbsoluteUri[prefix.Length..]);
        string[] slugs = [.. File.ReadLines(Path.Combine(RepositoryRoot(), "manuals", language, "manual.md"))
            .Where(static line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(static line => GitHubSlug(line[3..]))];
        Assert.Contains(anchor, slugs);
        Assert.StartsWith("14-", anchor, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("14. Securing access to PrusaLink", "14-securing-access-to-prusalink")]
    [InlineData("14. Accès sécurisé à PrusaLink", "14-accès-sécurisé-à-prusalink")]
    [InlineData("8. Recording, dashcam and timelapse", "8-recording-dashcam-and-timelapse")]
    [InlineData("A_b (c) – d", "a_b-c--d")]
    public void TheSlugFollowsTheRulesOfGitHub(string heading, string slug) => Assert.Equal(slug, GitHubSlug(heading));

    /// <summary>
    /// GitHub's heading anchor: lower case, every character that is not a letter, digit, mark, space, hyphen or underscore
    /// removed, each space turned into a hyphen (letters outside ASCII stay).
    /// </summary>
    private static string GitHubSlug(string heading)
    {
        StringBuilder slug = new(heading.Length);
        foreach (char c in heading.Trim().ToLowerInvariant())
        {
            if (c == ' ')
            {
                slug.Append('-');
            }
            else if (c is '-' or '_' || char.IsLetterOrDigit(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                slug.Append(c);
            }
        }

        return slug.ToString();
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LayerPeek.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void TheSecurityChapterOfAnUntranslatedLanguageIsTheEnglishOne() =>
        Assert.Equal(UserManual.SecurityChapterUriFor(CultureInfo.GetCultureInfo("en")), UserManual.SecurityChapterUriFor(CultureInfo.GetCultureInfo("ja")));

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
