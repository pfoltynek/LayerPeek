using Avalonia.Automation;
using Avalonia.Controls;

using LayerPeek.App.Cameras;
using LayerPeek.App.Localization;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>A language switch (T-029) while camera windows are open (T-014): XAML texts and view model texts follow.</summary>
public sealed class CameraWindowLanguageTests(HeadlessSession session)
{
    private static Button WindowButton(CameraWindow window, string toolTip) =>
        Ui.Find<Button>(window, button => ToolTip.GetTip(button) as string == toolTip);

    [Fact]
    public Task SwitchTranslatesTitleStatusButtonsAndOverlayCardsOfEveryOpenWindow() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new(overlay: new([new("test-a", Core.Cameras.Overlay.OverlayAnchor.TopLeft)]));
            CameraWindow alpha = harness.Open("a");
            CameraWindow beta = harness.Open("b");
            await harness.Factory.WaitForFeedAsync(2);
            harness.Monitor.Raise(Snapshot(PrinterState.Printing, Job(42.7, TimeSpan.FromMinutes(65)), printerId: "a"));
            await Ui.UntilAsync(() => Ui.ShowsText(alpha, "Printing") && Ui.ShowsText(alpha, "42%"), "print state shown");
            Assert.True(Ui.ShowsText(alpha, "Connecting to the camera…"));

            // What the app does: UiLanguage switches the resources (XAML bound through LocalizedStrings follows), then
            // App.OnLanguageChanged asks the open camera windows to format their view model texts again.
            Assert.True(new UiLanguage().Apply("de"));
            harness.Windows.RefreshTexts();
            alpha.Settle();
            beta.Settle();

            Assert.Equal("Alpha – Kamera", alpha.Title);
            Assert.Equal("Beta – Kamera", beta.Title);
            Assert.True(Ui.ShowsText(alpha, "Verbindung zur Kamera wird hergestellt…"));
            Assert.True(Ui.ShowsText(beta, "Verbindung zur Kamera wird hergestellt…"));
            Assert.True(Ui.ShowsText(alpha, "Druckt"));
            // Bound in XAML (LocalizedStrings) and chosen by the view model (MiniToolTip).
            Button mini = WindowButton(alpha, "Kleine Vorschau, immer im Vordergrund");
            Assert.Equal("Kleine Vorschau, immer im Vordergrund", AutomationProperties.GetName(mini));
            Assert.NotNull(WindowButton(alpha, "Vollbild (F11)"));
            Assert.Equal(
                "Kamerabild. Doppelklick schaltet das Vollbild um; F11 Vollbild, Esc zurück.",
                AutomationProperties.GetName(Ui.Find<Control>(alpha, static control => AutomationProperties.GetName(control)?.StartsWith("Kamerabild", StringComparison.Ordinal) == true)));

            // The windows and their streams are unchanged: no second connection.
            Assert.Equal(2, harness.Factory.Feeds.Count);
            Assert.True(alpha.IsVisible && beta.IsVisible);
        });

    [Fact]
    public Task SwitchBackToTheSystemLanguageRestoresEnglish() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow window = harness.Open("a");
            await harness.Factory.WaitForFeedAsync(1);
            UiLanguage language = new();
            language.Apply("pl");
            harness.Windows.RefreshTexts();
            window.Settle();
            Assert.Equal("Alpha – kamera", window.Title);

            language.Apply(null);
            harness.Windows.RefreshTexts();
            window.Settle();

            Assert.Equal("Alpha – Camera", window.Title);
            Assert.True(Ui.ShowsText(window, "Connecting to the camera…"));
            Assert.NotNull(WindowButton(window, "Mini preview, always on top"));
        });
}
