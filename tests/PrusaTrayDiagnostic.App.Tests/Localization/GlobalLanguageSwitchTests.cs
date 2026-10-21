using System.ComponentModel;
using System.Globalization;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Resources;

namespace PrusaTrayDiagnostic.App.Tests.Localization;

/// <summary>
/// Tests that switch the process-wide language of the resources (<see cref="Strings.Culture"/>). They run alone, after
/// all parallel tests, because every other test reads the texts; the system language is restored after each test.
/// </summary>
[CollectionDefinition(nameof(GlobalLanguageGroup), DisableParallelization = true)]
public sealed class GlobalLanguageGroup;

[Collection(nameof(GlobalLanguageGroup))]
public sealed class GlobalLanguageSwitchTests : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public GlobalLanguageSwitchTests()
    {
        // A known system language, so that "follow the system" is predictable.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void Dispose()
    {
        // Never leave another language behind for the tests that run next.
        Strings.Culture = null;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }

    [Theory]
    [InlineData("cs", "U_končit")]
    [InlineData("sk", "U_končiť")]
    [InlineData("de", "_Beenden")]
    [InlineData("es", "_Salir")]
    [InlineData("fr", "_Quitter")]
    [InlineData("it", "_Esci")]
    [InlineData("pl", "_Zakończ")]
    [InlineData("en", "_Quit")]
    public async Task ApplySwitchesEveryTextIndependentlyOfTheSystem(string code, string quit)
    {
        UiLanguage language = new();

        language.Apply(code);

        Assert.Equal(quit, Strings.MenuQuit);
        Assert.Equal(quit, LocalizedStrings.Instance[nameof(Strings.MenuQuit)]);
        // Also on other threads (tooltips are formatted on monitor threads).
        Assert.Equal(quit, await Task.Run(static () => Strings.MenuQuit, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ApplyNeverChangesNumberAndDateFormats()
    {
        UiLanguage language = new();

        language.Apply("de");

        Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
        Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("1.5", 1.5.ToString(CultureInfo.CurrentCulture));
        Assert.Equal("_Beenden", Strings.MenuQuit);
    }

    [Fact]
    public void SystemLanguageFollowsTheUiCultureOfTheSystem()
    {
        UiLanguage language = new();
        language.Apply("pl");

        language.Apply(null);

        Assert.Null(Strings.Culture);
        Assert.Equal("_Quit", Strings.MenuQuit);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
        Assert.Equal("_Beenden", Strings.MenuQuit);
    }

    [Fact]
    public void ApplyRefreshesBoundXamlTexts()
    {
        UiLanguage language = new();
        List<string?> changes = [];
        PropertyChangedEventHandler handler = (_, e) => changes.Add(e.PropertyName);
        LocalizedStrings.Instance.PropertyChanged += handler;
        try
        {
            language.Apply("fr");
        }
        finally
        {
            LocalizedStrings.Instance.PropertyChanged -= handler;
        }

        // Avalonia's indexer bindings listen for "Item" (and "Item[]" in older versions).
        Assert.Contains("Item", changes);
        Assert.Contains("Item[]", changes);
    }
}
