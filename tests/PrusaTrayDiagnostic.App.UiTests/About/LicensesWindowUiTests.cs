using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;

using PrusaTrayDiagnostic.App.Licenses;
using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Tests.Tray;
using PrusaTrayDiagnostic.App.Tests.Tray.Menu;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;

namespace PrusaTrayDiagnostic.App.UiTests.About;

/// <summary>Third-party licences opened from the real About window (T-014).</summary>
public sealed class LicensesWindowUiTests(HeadlessSession session)
{
    private static void Click(Window window, Control control)
    {
        Ui.Click(window, Ui.CentreOf(control, window));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task AboutOpensOneLicencesWindowWithFFmpegAndExpandableTexts() =>
        session.RunAsync(async scope =>
        {
            FakeUrlLauncher launcher = new();
            AboutWindow about = scope.Track(new AboutWindow { DataContext = new AboutViewModel(launcher, typeof(AboutViewModel).Assembly) });
            about.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Ui.ShowsText(about, "License: Apache License 2.0"));

            HyperlinkButton open = Ui.Find<HyperlinkButton>(about, static button => button.Content as string == "Third-party licences…");
            Click(about, open);
            Click(about, open);

            LicensesWindow licences = Assert.IsType<LicensesWindow>(Assert.Single(about.OwnedWindows));
            LicensesViewModel viewModel = (LicensesViewModel)licences.DataContext!;
            Assert.True(Ui.ShowsText(licences, "FFmpeg (video decoding of the camera)"));
            Assert.True(Ui.ShowsText(licences, "Licence: LGPL-3.0-or-later"));
            // The FFmpeg version is read in the background (the native libraries are next to this test as well).
            await Ui.UntilAsync(() => viewModel.FFmpegFullText is not null, "FFmpeg information loaded");
            Assert.NotEqual("Detecting the FFmpeg libraries…", viewModel.FFmpegVersionText);

            Expander avalonia = Ui.Find<Expander>(licences, static expander => expander.DataContext is LicenseItemViewModel { Name: "Avalonia" });
            Assert.True(Ui.ShowsText(licences, "Version 12.1.4"));
            Click(licences, avalonia.GetVisualDescendants().OfType<ToggleButton>().First());

            TextBox text = Ui.Find<TextBox>(avalonia, static box => box.IsEffectivelyVisible);
            Assert.Contains("Permission is hereby granted", text.Text, StringComparison.Ordinal);
            Click(licences, Ui.Find<HyperlinkButton>(avalonia, static button => button.Content as string == "https://github.com/AvaloniaUI/Avalonia"));
            Assert.Equal([new Uri("https://github.com/AvaloniaUI/Avalonia")], launcher.Opened);

            about.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(licences.IsVisible);
        });

    [Fact]
    public Task LanguageSwitchTranslatesAboutAndItsOpenLicencesWindow() =>
        session.RunAsync(static async scope =>
        {
            FakeUrlLauncher launcher = new();
            AboutViewModel aboutViewModel = new(launcher, typeof(AboutViewModel).Assembly);
            AboutWindow about = scope.Track(new AboutWindow { DataContext = aboutViewModel });
            // Opened like the app does (App.ShowAbout): the slot asks the view model to refresh after a switch.
            AuxiliaryWindowSlot slot = new();
            slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(about), null));
            about.Settle();
            Click(about, Ui.Find<HyperlinkButton>(about, static button => button.Content as string == "Third-party licences…"));
            LicensesWindow licences = Assert.IsType<LicensesWindow>(Assert.Single(about.OwnedWindows));
            LicensesViewModel viewModel = (LicensesViewModel)licences.DataContext!;
            await Ui.UntilAsync(() => viewModel.FFmpegFullText is not null, "FFmpeg information loaded");

            Assert.True(new UiLanguage().Apply("fr"));
            slot.RefreshTexts();
            about.Settle();
            licences.Settle();

            Assert.NotNull(Ui.Find<HyperlinkButton>(about, static button => button.Content as string == "Licences tierces…"));
            Assert.Equal("Licences tierces", licences.Title);
            Assert.True(Ui.ShowsText(licences, "FFmpeg (décodage vidéo de la caméra)"));
            Assert.True(Ui.ShowsText(licences, "Licence\u00A0: LGPL-3.0-or-later"));
            Assert.True(Ui.ShowsText(licences, "Version 12.1.4"));
            Assert.True(Ui.ShowsText(licences, "Licence\u00A0: MIT"));
            Assert.True(Ui.ShowsText(licences, "Tmds.DBus.Protocol (Linux uniquement)"));
            Assert.StartsWith("FFmpeg est distribué sous licence GNU LGPL", viewModel.FFmpegNote, StringComparison.Ordinal);
            Assert.True(licences.IsVisible);
        });
}
