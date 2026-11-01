using System.Globalization;

using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// Access keys of the context menu of the camera window (T-117, decision Q-D1 a) in every supported language: "Edit layout"
/// and "Always on top" have exactly one each, a letter or digit, and they differ; the tip of the layout button keeps its
/// text without one.
/// </summary>
public sealed class CameraContextMenuAccessKeyTests
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

        char layout = AccessKeyOf(Strings.CameraLayoutEditMenu, language);
        char topmost = AccessKeyOf(Strings.CameraTopmostMenu, language);

        Assert.NotEqual(char.ToUpper(layout, info), char.ToUpper(topmost, info));
        // The menu text is the button tip with the access key.
        Assert.Equal(Strings.CameraLayoutEdit, Strings.CameraLayoutEditMenu.Replace("_", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain('_', Strings.CameraLayoutEdit);
        Assert.DoesNotContain('_', Strings.CameraTopmostOn);
        Assert.DoesNotContain('_', Strings.CameraTopmostOff);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void PinTextsAreTranslatedAndNameTheState(string language)
    {
        using CultureScope culture = new(language);

        Assert.NotEqual(Strings.CameraTopmostOn, Strings.CameraTopmostOff);
        // Both start with the name of the option, the menu item is that name with its access key.
        string name = Strings.CameraTopmostMenu.Replace("_", string.Empty, StringComparison.Ordinal);
        Assert.StartsWith(name, Strings.CameraTopmostOn, StringComparison.Ordinal);
        Assert.StartsWith(name, Strings.CameraTopmostOff, StringComparison.Ordinal);
        if (language != "en")
        {
            string translated = Strings.CameraTopmostOn;
            using CultureScope english = new("en");
            Assert.NotEqual(Strings.CameraTopmostOn, translated);
        }
    }

    private static char AccessKeyOf(string header, string language)
    {
        int index = header.IndexOf('_', StringComparison.Ordinal);
        Assert.True(index >= 0 && index < header.Length - 1, $"{language}: '{header}' has no access key.");
        Assert.Equal(1, header.Count(static c => c == '_'));
        char key = header[index + 1];
        Assert.True(char.IsLetterOrDigit(key), $"{language}: '{header}' has the access key '{key}'.");
        return key;
    }
}
