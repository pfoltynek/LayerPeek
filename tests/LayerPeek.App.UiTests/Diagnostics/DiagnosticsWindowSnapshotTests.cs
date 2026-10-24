using Avalonia.Styling;

using LayerPeek.App.Localization;
using LayerPeek.App.UiTests.Infrastructure;

namespace LayerPeek.App.UiTests.Diagnostics;

/// <summary>
/// What the diagnostics window looks like (T-041; Skia rendering compared with the PNGs in <c>Snapshots/</c>). The window
/// keeps its XAML size (780 × 508), the system language is English unless a test switches it, time is fixed. Update the
/// references with <c>LAYERPEEK_UPDATE_SNAPSHOTS=1</c> (see the task notes of T-031).
/// </summary>
public sealed class DiagnosticsWindowSnapshotTests(HeadlessSession session)
{
    [Fact]
    public Task PrintingWithLoadedInformation() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowLayoutTests.OpenPrinting(scope);

            SnapshotAssert.Matches(ui.Window, "diagnostics-printing");
            return Task.CompletedTask;
        });

    [Fact]
    public Task IdleInTheDarkTheme() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowLayoutTests.OpenIdle(scope);
            ui.Window.RequestedThemeVariant = ThemeVariant.Dark;

            SnapshotAssert.Matches(ui.Window, "diagnostics-idle-dark");
            return Task.CompletedTask;
        });

    [Fact]
    public Task OfflineWithDetailInFrench() =>
        session.RunAsync(static scope =>
        {
            Assert.True(new UiLanguage().Apply("fr"));
            DiagnosticsWindowHarness ui = DiagnosticsWindowLayoutTests.OpenOffline(scope);

            SnapshotAssert.Matches(ui.Window, "diagnostics-offline-fr");
            return Task.CompletedTask;
        });

    [Fact]
    public Task WorstStateInItalian() =>
        session.RunAsync(static scope =>
        {
            Assert.True(new UiLanguage().Apply("it"));
            DiagnosticsWindowHarness ui = DiagnosticsWindowLayoutTests.OpenWorst(scope, loadedInfo: true);

            SnapshotAssert.Matches(ui.Window, "diagnostics-worst-it");
            return Task.CompletedTask;
        });

    [Fact]
    public Task NoPrinter() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = DiagnosticsWindowHarness.Open(scope, []);

            SnapshotAssert.Matches(ui.Window, "diagnostics-no-printer");
            return Task.CompletedTask;
        });
}
