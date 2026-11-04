using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// The timelapse options of the real settings window (T-161, user decisions 2026-11-04; experimental): "Take a timelapse of
/// prints" with its "Experimental" badge on the Camera sub-tab of a printer and the time between frames next to the heading
/// of the timelapse section (with its badge) on the Recording tab, in every language; an invalid interval blocks Save, shows
/// its error and gets the focus (T-112). The fit of both views in every language and state is checked by
/// <see cref="SettingsWindowLayoutTests"/>.
/// </summary>
public sealed class SettingsWindowTimelapseUiTests(HeadlessSession session)
{
    private static readonly CameraSettings WorkingCamera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));

    public static TheoryData<string> Languages => [.. AppLanguages.Supported];

    [Theory]
    [MemberData(nameof(Languages))]
    public Task EveryTimelapseOptionHasTheExperimentalBadge(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            try
            {
                SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }));
                ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
                ui.Window.Settle();

                CheckBox prints = ui.Find<CheckBox>("PrinterTimelapsePrints");
                Border printBadge = ui.Find<Border>("PrinterTimelapseExperimental");
                Assert.True(prints.IsEffectivelyVisible && prints.IsEffectivelyEnabled);
                Assert.Equal(Strings.SettingsTimelapsePrints, prints.Content);
                Assert.Equal(Strings.SettingsTimelapsePrintsHint, ToolTip.GetTip(prints));
                AssertBadge(printBadge, ui.Window);
                // Right of its check box, on the same row.
                Rect box = Bounds(prints, ui.Window);
                Rect badge = Bounds(printBadge, ui.Window);
                Assert.True(badge.Left >= box.Right, $"{language}: the badge is not right of the check box.");
                Assert.True(badge.Top >= box.Top && badge.Bottom <= box.Bottom, $"{language}: the badge is not on the row of the check box.");

                ui.Click("SettingsTabRecording");
                ui.Window.Settle();
                AssertBadge(ui.Find<Border>("SettingsTimelapseExperimental"), ui.Window);
                NumericUpDown interval = ui.Find<NumericUpDown>("SettingsTimelapseInterval");
                Assert.True(interval.IsEffectivelyVisible && interval.IsEffectivelyEnabled);
                Assert.Equal(30, interval.Value);
                Assert.Equal(ui.ViewModel.TimelapseIntervalHint, ToolTip.GetTip(interval));
                Assert.Equal(ui.ViewModel.TimelapseIntervalHint, AutomationProperties.GetHelpText(interval));
                Assert.False(ui.Find<TextBlock>("SettingsTimelapseIntervalError").IsVisible);
                Assert.False(ui.Find<TextBlock>("SettingsTimelapseUnavailable").IsVisible);
            }
            finally
            {
                Assert.True(new UiLanguage().Apply("en"));
            }
        });

    [Fact]
    public Task AnInvalidIntervalShowsItsErrorAndGetsTheFocusOnSave() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }));
            ui.Click("SettingsTabRecording");
            ui.Window.Settle();

            ui.ViewModel.TimelapseIntervalSeconds = 2.5m;
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            ui.Window.Settle();

            Assert.Equal(SettingsViewModel.RecordingTab, ui.ViewModel.SelectedTabIndex);
            TextBlock error = ui.Find<TextBlock>("SettingsTimelapseIntervalError");
            Assert.True(error.IsEffectivelyVisible);
            Assert.Equal(ui.ViewModel.TimelapseIntervalError, error.Text);
            NumericUpDown interval = ui.Find<NumericUpDown>("SettingsTimelapseInterval");
            Assert.Contains("invalid", interval.Classes);
            Assert.True(interval.IsKeyboardFocusWithin);
        });

    [Fact]
    public Task WithoutTheRecorderTheOptionsAreDisabledWithTheReason() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(
                scope,
                SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }),
                recordings: new FakeCameraRecordings { IsAvailable = false, IsTimelapseAvailable = false });
            ui.ViewModel.SelectedPrinterTabIndex = SettingsViewModel.CameraPrinterTab;
            ui.Window.Settle();

            CheckBox prints = ui.Find<CheckBox>("PrinterTimelapsePrints");
            Assert.False(prints.IsEffectivelyEnabled);
            Assert.Equal(Strings.TimelapseUnavailable, ToolTip.GetTip(prints));
            Assert.True(ui.Find<Border>("PrinterTimelapseExperimental").IsEffectivelyVisible);

            ui.Click("SettingsTabRecording");
            ui.Window.Settle();
            Assert.False(ui.Find<NumericUpDown>("SettingsTimelapseInterval").IsEffectivelyEnabled);
            Assert.True(ui.Find<TextBlock>("SettingsTimelapseUnavailable").IsEffectivelyVisible);
        });

    private static void AssertBadge(Border badge, Window window)
    {
        Assert.True(badge.IsEffectivelyVisible);
        TextBlock text = Assert.IsType<TextBlock>(badge.Child);
        Assert.Equal(Strings.SettingsExperimental, text.Text);
        Assert.Equal(Strings.SettingsExperimentalHint, ToolTip.GetTip(badge));
        Assert.True(Bounds(badge, window).Right <= window.ClientSize.Width);
    }

    private static Rect Bounds(Control control, Window window) =>
        control.TranslatePoint(default, window) is { } origin ? new Rect(origin, control.Bounds.Size) : throw new InvalidOperationException("Not in the window.");
}
