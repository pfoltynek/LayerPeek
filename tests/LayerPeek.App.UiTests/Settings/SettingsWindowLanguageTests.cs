using System.Globalization;

using Avalonia.Controls;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.UiTests.Infrastructure;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>A language switch (T-029) while the settings window is open: XAML texts and the view model's texts follow.</summary>
public sealed class SettingsWindowLanguageTests(HeadlessSession session)
{
    [Theory]
    [InlineData("cs")]
    [InlineData("de")]
    public Task SwitchTranslatesLabelsErrorsAndHintsOfTheOpenWindow(string code) =>
        session.RunAsync(async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith());
            ui.Click("SettingsAddPrinter");
            ui.Window.TypeOver(ui.NameBox, "Alpha");
            await ui.SaveAsync();
            Assert.Equal(Strings.SettingsErrorAddressRequired, ui.ShownText("PrinterAddressError"));
            CultureInfo target = CultureInfo.GetCultureInfo(code);
            Assert.NotEqual(Text(nameof(Strings.SettingsSave), target), Strings.SettingsSave);

            // What the app does: UiLanguage switches the resources (XAML bound through LocalizedStrings follows), then
            // App.OnLanguageChanged asks the open window to format its view model texts again.
            UiLanguage language = new();
            Assert.True(language.Apply(code));
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            Assert.Equal(Text(nameof(Strings.SettingsTitle), target), ui.Window.Title);
            Assert.Equal(Text(nameof(Strings.SettingsSave), target), ui.Find<Button>("SettingsSave").Content);
            Assert.Equal(Text(nameof(Strings.SettingsCancel), target), ui.Find<Button>("SettingsCancel").Content);
            Assert.Equal(Text(nameof(Strings.SettingsUserName), target), ui.ShownText("PrinterUserNameLabel"));
            Assert.Equal(Text(nameof(Strings.SettingsUserNameHint), target), ui.ShownText("PrinterUserNameHint"));
            Assert.Equal(Text(nameof(Strings.SettingsErrorAddressRequired), target), ui.ShownText("PrinterAddressError"));
            Assert.Equal(Text(nameof(Strings.SettingsFixErrors), target), ui.ShownText("SettingsStatus"));

            // The edits and the open window survive the switch.
            Assert.True(ui.IsOpen);
            Assert.Equal("Alpha", ui.NameBox.Text);
        });

    [Fact]
    public Task SwitchBackToTheSystemLanguageRestoresEnglish() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            UiLanguage language = new();
            language.Apply("pl");
            ui.Slot.RefreshTexts();
            ui.Window.Settle();
            Assert.NotEqual("Save", ui.Find<Button>("SettingsSave").Content);

            language.Apply(null);
            ui.Slot.RefreshTexts();
            ui.Window.Settle();

            Assert.Equal("Save", ui.Find<Button>("SettingsSave").Content);
            Assert.Equal("PrusaLink user name from the printer settings. The default is “maker”.", ui.ShownText("PrinterUserNameHint"));
        });

    private static string Text(string key, CultureInfo culture) =>
        Strings.ResourceManager.GetString(key, culture) ?? throw new InvalidOperationException($"Missing text '{key}'.");
}
