using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// T-182 (user decisions 2026-11-11): an unencrypted address that may be outside the local network gets a warning in place
/// of the hint under the printer address and under the camera address, with an amber outline of the field, the warning as
/// its help text for screen readers and the "More information" link to the security chapter of the manual. The warning
/// follows the typing, goes away when the address is corrected and never blocks saving.
/// </summary>
public sealed class SettingsWindowUntrustedNetworkUiTests(HeadlessSession session)
{
    private const string Warning = "warning";

    private static readonly Color LightWarningColor = Color.Parse("#8A4B00");
    private static readonly Color DarkWarningColor = Color.Parse("#FFC56B");
    private static readonly Uri LocalStream = new("rtsp://192.168.0.227/live");

    [Fact]
    public Task ThePrinterAddressWarningFollowsTheTypingAndDoesNotBlockSaving() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop"));
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            TextBox address = ui.AddressBox;
            AssertNoWarning(ui, address, "PrinterAddressWarning", "PrinterAddressHint", Strings.SettingsAddressHint);

            ui.Window.TypeOver(address, "http://printer.example.com");
            AssertWarning(ui, address, "PrinterAddressWarning", "PrinterAddressHint", Strings.SettingsAddressUntrustedNetwork, LightWarningColor);
            Assert.True(ui.Window.IsShown<HyperlinkButton>("PrinterAddressSecurityHelp"));

            // https, a local name and an error each take the warning away (an error takes the place of the hint as before).
            ui.Window.TypeOver(address, "https://printer.example.com");
            AssertNoWarning(ui, address, "PrinterAddressWarning", "PrinterAddressHint", Strings.SettingsAddressHint);
            ui.Window.TypeOver(address, "http://8.8.8.8");
            Assert.True(ui.ViewModel.SelectedPrinter!.HasAddressWarning);
            ui.Window.TypeOver(address, "ftp://8.8.8.8");
            Assert.Null(ui.ShownText("PrinterAddressWarning"));
            Assert.Equal(Strings.SettingsErrorAddressInvalid, ui.ShownText("PrinterAddressError"));
            Assert.DoesNotContain(Warning, address.Classes);
            ui.Window.TypeOver(address, "prusa-mk4.local");
            AssertNoWarning(ui, address, "PrinterAddressWarning", "PrinterAddressHint", Strings.SettingsAddressHint);

            // Informative only (Q2): Save keeps an address with the warning (and closes the window).
            ui.Window.TypeOver(address, "http://printer.example.com");
            Assert.True(ui.ViewModel.SelectedPrinter.HasAddressWarning);
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Assert.Equal("http://printer.example.com", Assert.Single(Assert.Single(store.Saves).Updated.Printers).Address);
        });

    [Fact]
    public Task TheCameraAddressWarningFollowsTheTypingAndDoesNotBlockSaving() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop") with { Camera = new CameraSettings(enabled: true, LocalStream) });
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ui.Click("PrinterTabCamera");
            ui.Window.Settle();
            TextBox url = ui.Find<TextBox>("PrinterCameraUrl");
            AssertNoWarning(ui, url, "PrinterCameraUrlWarning", "PrinterCameraUrlHint", Strings.SettingsCameraUrlHint);

            ui.Window.TypeOver(url, "rtsp://203.0.113.10/live");
            AssertWarning(ui, url, "PrinterCameraUrlWarning", "PrinterCameraUrlHint", Strings.SettingsCameraUrlUntrustedNetwork, LightWarningColor);
            Assert.True(ui.Window.IsShown<HyperlinkButton>("PrinterCameraSecurityHelp"));

            ui.Window.TypeOver(url, LocalStream.OriginalString);
            AssertNoWarning(ui, url, "PrinterCameraUrlWarning", "PrinterCameraUrlHint", Strings.SettingsCameraUrlHint);

            ui.Window.TypeOver(url, "rtsp://203.0.113.10/live");
            Assert.True(ui.ViewModel.SelectedPrinter!.HasCameraUrlWarning);
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Assert.Equal(new Uri("rtsp://203.0.113.10/live"), Assert.Single(Assert.Single(store.Saves).Updated.Printers).Camera.StreamUri);
        });

    [Fact]
    public Task TheLinkOpensTheSecurityChapter() =>
        session.RunAsync(static async scope =>
        {
            FakeUrlLauncher launcher = new();
            Uri chapter = new("https://example.invalid/manuals/en/manual.md#prusalink-access-security");
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop", "http://203.0.113.10") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://203.0.113.10/live")) }),
                urlLauncher: launcher,
                securityHelp: chapter);
            ui.Window.Settle();

            ui.Click("PrinterAddressSecurityHelp");
            ui.Click("PrinterTabCamera");
            ui.Window.Settle();
            ui.Click("PrinterCameraSecurityHelp");

            Assert.Equal([chapter, chapter], launcher.Opened);
            HyperlinkButton link = ui.Find<HyperlinkButton>("PrinterCameraSecurityHelp");
            Assert.Equal(Strings.SettingsSecurityMoreInfo, link.Content);
            Assert.Equal(Strings.SettingsSecurityMoreInfoHint, AutomationProperties.GetHelpText(link));
        });

    [Fact]
    public Task TheWarningUsesTheWarningColourOfTheDarkTheme() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop", "http://203.0.113.10")));
            AssertWarning(ui, ui.AddressBox, "PrinterAddressWarning", "PrinterAddressHint", Strings.SettingsAddressUntrustedNetwork, LightWarningColor);
            ui.Window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            ui.Window.Settle();

            AssertWarning(ui, ui.AddressBox, "PrinterAddressWarning", "PrinterAddressHint", Strings.SettingsAddressUntrustedNetwork, DarkWarningColor);
        });

    [Fact]
    public Task TheWarningFollowsTheLanguage() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "MK4S workshop", "http://203.0.113.10")));
            string english = ui.ShownText("PrinterAddressWarning")!;

            Assert.True(new UiLanguage().Apply("cs"));
            ui.ViewModel.RefreshTexts();
            ui.Window.Settle();

            Assert.NotEqual(english, ui.ShownText("PrinterAddressWarning"));
            Assert.Equal(Strings.SettingsAddressUntrustedNetwork, ui.ShownText("PrinterAddressWarning"));
            Assert.Equal(Strings.SettingsAddressUntrustedNetwork, AutomationProperties.GetHelpText(ui.AddressBox));
        });

    private static void AssertWarning(SettingsWindowHarness ui, TextBox field, string warningId, string hintId, string text, Color color)
    {
        ui.Window.Settle();
        Assert.Equal(text, ui.ShownText(warningId));
        Assert.Null(ui.ShownText(hintId));
        Assert.Contains(Warning, field.Classes);
        Assert.DoesNotContain("invalid", field.Classes);
        Assert.Equal(text, AutomationProperties.GetHelpText(field));
        Assert.Equal(color, ColorOf(ui.Find<TextBlock>(warningId).Foreground));
        Border outline = field.GetVisualDescendants().OfType<Border>().First(static border => border.Name == "PART_BorderElement");
        Assert.Equal(color, ColorOf(outline.BorderBrush));
        Assert.Equal(2, outline.BorderThickness.Left);
    }

    private static void AssertNoWarning(SettingsWindowHarness ui, TextBox field, string warningId, string hintId, string hint)
    {
        ui.Window.Settle();
        Assert.Null(ui.ShownText(warningId));
        Assert.Equal(hint, ui.ShownText(hintId));
        Assert.DoesNotContain(Warning, field.Classes);
        Assert.Equal(hint, AutomationProperties.GetHelpText(field));
    }

    private static Color ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : default;
}
