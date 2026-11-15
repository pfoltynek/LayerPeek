using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Windows;

/// <summary>
/// Texts of the context menu of text fields (T-242) in every supported language: each item has exactly one access key, a
/// letter, and the items of one menu differ in it, with the Windows and with the macOS Paste; every language but English
/// has its own words.
/// </summary>
public sealed class TextBoxContextMenuTextTests
{
    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    [Fact]
    public void EveryAppLanguageIsChecked()
    {
        Assert.Equal(8, AppLanguages.Supported.Count);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void MenuItemsHaveUniqueAccessKeys(string language)
    {
        using CultureScope culture = new(language);
        CultureInfo info = CultureInfo.GetCultureInfo(language);

        foreach (string paste in new[] { Strings.TextBoxPaste, Strings.TextBoxPasteMac })
        {
            char[] keys = [.. new[] { Strings.TextBoxCut, Strings.TextBoxCopy, paste, Strings.TextBoxSelectAll }
                .Select(header => char.ToUpper(AccessKeyOf(header, language), info))];
            Assert.Equal(keys.Length, keys.Distinct().Count());
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void MenuItemsAreTranslated(string language)
    {
        if (language == "en")
        {
            return;
        }

        string[] translated;
        using (new CultureScope(language))
        {
            translated = [Strings.TextBoxCut, Strings.TextBoxCopy, Strings.TextBoxPaste, Strings.TextBoxSelectAll];
        }

        using CultureScope english = new("en");
        Assert.All(
            translated.Zip([Strings.TextBoxCut, Strings.TextBoxCopy, Strings.TextBoxPaste, Strings.TextBoxSelectAll]),
            pair => Assert.NotEqual(pair.Second, pair.First));
    }

    [Theory]
    [InlineData("de", "Einfügen", "Einsetzen")]
    [InlineData("sk", "Prilepiť", "Vložiť")]
    [InlineData("cs", "Vložit", "Vložit")]
    public void PasteFollowsTheWordingOfEachPlatform(string language, string windows, string mac)
    {
        using CultureScope culture = new(language);

        Assert.Equal(windows, Strings.TextBoxPaste.Replace("_", string.Empty, StringComparison.Ordinal));
        Assert.Equal(mac, Strings.TextBoxPasteMac.Replace("_", string.Empty, StringComparison.Ordinal));
    }

    private static char AccessKeyOf(string header, string language)
    {
        int index = header.IndexOf('_', StringComparison.Ordinal);
        Assert.True(index >= 0 && index < header.Length - 1, $"{language}: '{header}' has no access key.");
        Assert.Equal(1, header.Count(static c => c == '_'));
        char key = header[index + 1];
        Assert.True(char.IsLetter(key), $"{language}: '{header}' has the access key '{key}'.");
        return key;
    }
}
