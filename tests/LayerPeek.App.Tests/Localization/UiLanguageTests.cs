using System.Globalization;

using LayerPeek.App.Localization;
using LayerPeek.App.Tray;
using LayerPeek.App.Tests.Tray;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Localization;

/// <summary>Language switching logic with a recording switch instead of the global resources (see <see cref="GlobalLanguageSwitchTests"/>).</summary>
public sealed class UiLanguageTests
{
    private readonly List<CultureInfo?> _applied = [];
    private int _changed;

    [Fact]
    public void StartsWithTheSystemLanguage()
    {
        UiLanguage language = Create();

        Assert.Null(language.Code);
        Assert.False(language.Apply(null));
        Assert.Empty(_applied);
        Assert.Equal(0, _changed);
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
    public void SupportedLanguageSwitchesTheTextsAndRaisesChanged(string code)
    {
        UiLanguage language = Create();

        Assert.True(language.Apply(code));

        Assert.Equal(code, language.Code);
        Assert.Equal([CultureInfo.GetCultureInfo(code)], _applied);
        Assert.Equal(1, _changed);
    }

    [Fact]
    public void SameLanguageAgainDoesNothing()
    {
        UiLanguage language = Create();
        language.Apply("de");

        Assert.False(language.Apply("de"));

        Assert.Single(_applied);
        Assert.Equal(1, _changed);
    }

    [Fact]
    public void BackToSystemClearsTheExplicitCulture()
    {
        UiLanguage language = Create();
        language.Apply("pl");

        Assert.True(language.Apply(null));

        Assert.Null(language.Code);
        Assert.Equal([CultureInfo.GetCultureInfo("pl"), null], _applied);
        Assert.Equal(2, _changed);
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("de-AT")]
    [InlineData("")]
    public void UnknownCodeMeansSystemLanguage(string code)
    {
        UiLanguage language = Create();
        language.Apply("fr");

        language.Apply(code);

        Assert.Null(language.Code);
        Assert.Null(_applied[^1]);
    }

    [Fact]
    public void ChangedIsRaisedAfterTheSwitch()
    {
        CultureInfo? seen = null;
        UiLanguage language = new(culture => _applied.Add(culture));
        language.Changed += (_, _) => seen = _applied[^1];

        language.Apply("it");

        Assert.Equal(CultureInfo.GetCultureInfo("it"), seen);
    }

    [Fact]
    public void ObserverAppliesTheLanguageOfEverySettingsOnTheUiThread()
    {
        ManualDispatcher dispatcher = new();
        UiLanguage language = Create();
        UiLanguageObserver observer = new(language, dispatcher);

        observer.ApplySettings(new AppSettings { Language = "de" });
        Assert.Null(language.Code);
        dispatcher.RunAll();
        Assert.Equal("de", language.Code);

        // Only the language changed: monitoring keeps running and observers get UpdateSettings.
        ((IMonitoringObserver)observer).UpdateSettings(new AppSettings { Language = "sk" });
        dispatcher.RunAll();
        Assert.Equal("sk", language.Code);

        observer.ApplySettings(AppSettings.Default);
        dispatcher.RunAll();
        Assert.Null(language.Code);
    }

    [Fact]
    public void ObserverAppliesOnlyTheLatestOfQuickSuccessiveSettings()
    {
        ManualDispatcher dispatcher = new();
        UiLanguage language = Create();
        UiLanguageObserver observer = new(language, dispatcher);

        observer.ApplySettings(new AppSettings { Language = "de" });
        observer.UpdateSettings(new AppSettings { Language = "fr" });
        observer.ApplySettings(new AppSettings { Language = "es" });

        Assert.Equal(1, dispatcher.Pending);
        dispatcher.RunAll();
        Assert.Equal("es", language.Code);
        Assert.Equal([CultureInfo.GetCultureInfo("es")], _applied);
    }

    [Fact]
    public void ObserverIgnoresAMonitoringFailure()
    {
        ManualDispatcher dispatcher = new();
        UiLanguageObserver observer = new(Create(), dispatcher);

        observer.ReportMonitoringFailed();

        Assert.Equal(0, dispatcher.PostCount);
    }

    private UiLanguage Create()
    {
        UiLanguage language = new(culture => _applied.Add(culture));
        language.Changed += (_, _) => _changed++;
        return language;
    }
}
