using System.Globalization;
using System.Text;

using Avalonia.Controls;

using LayerPeek.App.UiTests.Infrastructure;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// Information only (never fails, T-223): the widths of the main tab headers of the settings window in English with
/// Inter, the app's default font on macOS. The check in the running app (T-127, item 1k) compares them with the
/// Accessibility frames of the same tabs; equal widths mean the app lays text out like these tests. Shown with
/// <c>--output detailed</c> or in the TRX report (standard output of the test).
/// </summary>
public sealed class SettingsWindowTabWidthsUiTests(HeadlessSession session)
{
    private static readonly string[] MainTabs =
        ["SettingsTabPrinters", "SettingsTabWidgets", "SettingsTabGeneral", "SettingsTabStartupUpdates", "SettingsTabRecording", "SettingsTabCamera"];

    [Fact]
    public Task MainTabWidthsForTheCheckInTheRunningApp()
    {
        ITestOutputHelper output = TestContext.Current.TestOutputHelper!;
        return session.RunAsync(async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")));
            StringBuilder report = new("Main tab headers of the settings window (English, Inter, DIP = points):");
            report.AppendLine();
            foreach (string id in MainTabs)
            {
                TabItem tab = ui.Window.Find<TabItem>(id);
                Assert.True(tab.Bounds.Width > 0, id);
                report.AppendLine(CultureInfo.InvariantCulture, $"  {id}: {tab.Bounds.Width:0.00} × {tab.Bounds.Height:0.00}");
            }

            output.WriteLine(report.ToString());
        });
    }
}
