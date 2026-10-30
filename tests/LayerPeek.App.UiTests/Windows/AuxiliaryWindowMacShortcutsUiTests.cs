using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tests.Tray.Menu;
using LayerPeek.App.UiTests.Diagnostics;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;

namespace LayerPeek.App.UiTests.Windows;

/// <summary>
/// ⌘W and ⌘. in the About, third-party licences and diagnostics windows (T-100, user decision 2026-10-29): both close the
/// window like its Close button (⌘. is Esc on macOS). Forced through <c>UsesMacShortcuts</c> on every OS; the Windows variant
/// ignores them.
/// </summary>
public sealed class AuxiliaryWindowMacShortcutsUiTests(HeadlessSession session)
{
    public static TheoryData<string> Keys => ["⌘W", "⌘."];

    /// <summary>Presses ⌘W or ⌘. (released only when the window stays: a closed headless window takes no more input).</summary>
    private static void Press(Window window, string shortcut, bool closes)
    {
        (Key key, PhysicalKey physical, string symbol) = shortcut == "⌘W" ? (Key.W, PhysicalKey.W, "w") : (Key.OemPeriod, PhysicalKey.Period, ".");
        window.KeyPress(key, RawInputModifiers.Meta, physical, symbol);
        if (!closes)
        {
            window.KeyRelease(key, RawInputModifiers.Meta, physical, symbol);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static AboutWindow ShowAbout(UiTestScope scope, bool mac)
    {
        AboutWindow about = scope.Track(new AboutWindow { DataContext = new AboutViewModel(new FakeUrlLauncher(), typeof(AboutViewModel).Assembly), UsesMacShortcuts = mac });
        about.Show();
        about.Settle();
        return about;
    }

    [Fact]
    public Task TheShortcutsOfMacOSApplyOnlyOnMacOS() =>
        session.RunAsync(static async scope =>
        {
            bool mac = OperatingSystem.IsMacOS();
            Assert.Equal(mac, scope.Track(new AboutWindow()).UsesMacShortcuts);
            Assert.Equal(mac, scope.Track(new LicensesWindow()).UsesMacShortcuts);
            Assert.Equal(mac, scope.Track(new DiagnosticsWindow()).UsesMacShortcuts);
            Assert.Equal(mac, scope.Track(new SettingsWindow()).UsesMacShortcuts);
            await using Cameras.CameraHarness harness = new();
            CameraWindow camera = harness.Open();
            Assert.Equal(mac, ((LayerPeek.App.Cameras.CameraViewModel)camera.DataContext!).UsesMacShortcuts);
        });

    [Theory]
    [MemberData(nameof(Keys))]
    public Task AboutClosesWithTheShortcut(string shortcut) =>
        session.RunAsync(scope =>
        {
            AboutWindow about = ShowAbout(scope, mac: true);
            Press(about, shortcut, closes: true);
            Assert.False(about.IsVisible);
            return Task.CompletedTask;
        });

    [Theory]
    [MemberData(nameof(Keys))]
    public Task LicencesCloseWithTheShortcutAndTheAboutWindowStays(string shortcut) =>
        session.RunAsync(scope =>
        {
            AboutWindow about = ShowAbout(scope, mac: true);
            HyperlinkButton open = Ui.Find<HyperlinkButton>(about, static button => button.Content as string == "Third-party licences…");
            Ui.Click(about, Ui.CentreOf(open, about));
            Dispatcher.UIThread.RunJobs();
            LicensesWindow licences = Assert.IsType<LicensesWindow>(Assert.Single(about.OwnedWindows));
            licences.UsesMacShortcuts = true;
            licences.Settle();

            Press(licences, shortcut, closes: true);

            Assert.False(licences.IsVisible);
            Assert.True(about.IsVisible);
            return Task.CompletedTask;
        });

    [Theory]
    [MemberData(nameof(Keys))]
    public Task DiagnosticsCloseWithTheShortcut(string shortcut) =>
        session.RunAsync(scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [TestPrinters.Definition("a", "Alpha")]);
            ui.Window.UsesMacShortcuts = true;

            Press(ui.Window, shortcut, closes: true);

            Assert.False(ui.Window.IsVisible);
            Assert.False(ui.Slot.IsOpen);
            return Task.CompletedTask;
        });

    [Fact]
    public Task DiagnosticsCommandPeriodClosesAnOpenDropDownFirstLikeEsc() =>
        session.RunAsync(scope =>
        {
            // User decision 2026-10-30: like the settings window, ⌘. with an open drop-down closes only the drop-down.
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [TestPrinters.Definition("a", "Alpha"), TestPrinters.Definition("b", "Beta")]);
            ui.Window.UsesMacShortcuts = true;
            ComboBox selector = ui.Find<ComboBox>("DiagnosticsPrinterSelector");
            Assert.True(selector.IsVisible);
            Assert.True(selector.Focus());
            selector.IsDropDownOpen = true;
            ui.Window.Settle();
            Assert.True(selector.IsDropDownOpen);

            Press(ui.Window, "⌘.", closes: false);
            Assert.False(selector.IsDropDownOpen);
            Assert.True(ui.Window.IsVisible);

            // Without an open drop-down ⌘. closes the window again; ⌘W closes it also with an open drop-down.
            Press(ui.Window, "⌘.", closes: true);
            Assert.False(ui.Window.IsVisible);

            DiagnosticsWindowHarness again = DiagnosticsWindowHarness.Open(scope, [TestPrinters.Definition("a", "Alpha"), TestPrinters.Definition("b", "Beta")]);
            again.Window.UsesMacShortcuts = true;
            ComboBox selectorAgain = again.Find<ComboBox>("DiagnosticsPrinterSelector");
            Assert.True(selectorAgain.Focus());
            selectorAgain.IsDropDownOpen = true;
            again.Window.Settle();
            Press(again.Window, "⌘W", closes: true);
            Assert.False(again.Window.IsVisible);
            return Task.CompletedTask;
        });

    [Theory]
    [MemberData(nameof(Keys))]
    public Task WindowsVariantKeepsTheWindowsOpen(string shortcut) =>
        session.RunAsync(scope =>
        {
            AboutWindow about = ShowAbout(scope, mac: false);
            Press(about, shortcut, closes: false);
            Assert.True(about.IsVisible);

            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, [TestPrinters.Definition("a", "Alpha")]);
            ui.Window.UsesMacShortcuts = false;
            Press(ui.Window, shortcut, closes: false);
            Assert.True(ui.Window.IsVisible);

            // Esc still closes them (IsCancel), on every OS.
            about.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(about.IsVisible);
            return Task.CompletedTask;
        });
}
