using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Settings;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.App.Tests.Settings;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Settings;

/// <summary>
/// The timelapse options of the real settings window (T-161, user decisions 2026-11-04; experimental): "Take a timelapse of
/// prints" with its "Experimental" badge on the Camera sub-tab of a printer and the time between frames next to the heading
/// of the timelapse section (with its badge) on the Recording tab, in every language; an invalid interval blocks Save, shows
/// its error and gets the focus (T-112). T-159: what takes the frames (every N seconds / every N-th layer) chooses the number
/// shown next to it. The fit of both views in every language and state is checked by
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
            Assert.False(ui.Find<ComboBox>("SettingsTimelapseTrigger").IsEffectivelyEnabled);
            Assert.Equal(Strings.TimelapseUnavailable, ToolTip.GetTip(ui.Find<ComboBox>("SettingsTimelapseTrigger")));
            Assert.True(ui.Find<TextBlock>("SettingsTimelapseUnavailable").IsEffectivelyVisible);
        });

    /// <summary>
    /// T-159: what takes the frames, in place of the interval's label: every N seconds (the interval next to it) or every N-th
    /// layer (N next to it), both texts fitting the combo box, the estimate explained by the tips, in every language.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public Task TheTriggerChoosesWhichNumberShowsInEveryLanguage(string language) =>
        session.RunAsync(async scope =>
        {
            Assert.True(new UiLanguage().Apply(language));
            try
            {
                SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }));
                ui.Click("SettingsTabRecording");
                ui.Window.Settle();

                ComboBox trigger = ui.Find<ComboBox>("SettingsTimelapseTrigger");
                NumericUpDown interval = ui.Find<NumericUpDown>("SettingsTimelapseInterval");
                NumericUpDown layers = ui.Find<NumericUpDown>("SettingsTimelapseEveryNthLayer");
                Assert.True(trigger.IsEffectivelyVisible && trigger.IsEffectivelyEnabled);
                Assert.Equal(Strings.SettingsTimelapseTrigger, AutomationProperties.GetName(trigger));
                Assert.Equal(Strings.SettingsTimelapseTriggerHint, ToolTip.GetTip(trigger));
                Assert.Equal(Strings.SettingsTimelapseTriggerHint, AutomationProperties.GetHelpText(trigger));
                Assert.Same(ui.ViewModel.TimelapseTriggerOptions[0], trigger.SelectedItem);
                Assert.True(interval.IsEffectivelyVisible);
                Assert.False(layers.IsVisible);
                List<string> problems = [];
                foreach (TimelapseTriggerOption option in ui.ViewModel.TimelapseTriggerOptions)
                {
                    TextBlock probe = new() { Text = option.Text, FontSize = trigger.FontSize, FontFamily = trigger.FontFamily };
                    probe.Measure(Size.Infinity);
                    // The padding and the chevron of the Fluent combo box take about 44 DIP.
                    if (probe.DesiredSize.Width > trigger.Bounds.Width - 44)
                    {
                        problems.Add($"{language}: '{option.Text}' needs {probe.DesiredSize.Width:0.#} DIP in a combo box of {trigger.Bounds.Width:0.#}.");
                    }
                }

                Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
                // On the row of the heading, left of the number.
                Rect box = Bounds(trigger, ui.Window);
                Rect number = Bounds(interval, ui.Window);
                Assert.True(box.Right <= number.Left && Math.Abs(box.Center.Y - number.Center.Y) < 2, $"{language}: the trigger is not left of the interval.");

                trigger.SelectedItem = ui.ViewModel.TimelapseTriggerOptions[1];
                ui.Window.Settle();
                Assert.True(ui.ViewModel.IsTimelapseByLayer);
                Assert.False(interval.IsVisible);
                Assert.True(layers.IsEffectivelyVisible && layers.IsEffectivelyEnabled);
                Assert.Equal(1, layers.Value);
                Assert.Equal(Strings.SettingsTimelapseEveryNthLayer, AutomationProperties.GetName(layers));
                Assert.Equal(ui.ViewModel.TimelapseEveryNthLayerHint, ToolTip.GetTip(layers));
                Assert.Equal(ui.ViewModel.TimelapseEveryNthLayerHint, AutomationProperties.GetHelpText(layers));
                Assert.Equal(number, Bounds(layers, ui.Window));
                AssertBadge(ui.Find<Border>("SettingsTimelapseExperimental"), ui.Window);
            }
            finally
            {
                Assert.True(new UiLanguage().Apply("en"));
            }
        });

    [Fact]
    public Task TheLayerTriggerIsSavedAndLoaded() =>
        session.RunAsync(static async scope =>
        {
            FakeEditorStore store = SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera });
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, store);
            ui.Click("SettingsTabRecording");
            ui.Window.Settle();

            ui.Find<ComboBox>("SettingsTimelapseTrigger").SelectedItem = ui.ViewModel.TimelapseTriggerOptions[1];
            ui.Window.Settle();
            ui.Find<NumericUpDown>("SettingsTimelapseEveryNthLayer").Value = 3;
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();

            LayerPeek.Core.Cameras.Recording.CameraRecordingSettings saved = Assert.Single(store.Saves).Updated.Recording;
            Assert.Equal(LayerPeek.Core.Cameras.Recording.CameraTimelapseTriggerKind.Layer, saved.TimelapseTrigger);
            Assert.Equal(3, saved.TimelapseEveryNthLayer);
            ui.Window.Close();

            // Opened again with what was saved.
            FakeEditorStore reopened = new()
            {
                Data = store.Data with { Settings = store.Saves[0].Updated },
            };
            SettingsWindowHarness again = await SettingsWindowHarness.OpenAsync(scope, reopened);
            again.Click("SettingsTabRecording");
            again.Window.Settle();
            Assert.Same(again.ViewModel.TimelapseTriggerOptions[1], again.Find<ComboBox>("SettingsTimelapseTrigger").SelectedItem);
            Assert.True(again.Find<NumericUpDown>("SettingsTimelapseEveryNthLayer").IsEffectivelyVisible);
            Assert.Equal(3, again.Find<NumericUpDown>("SettingsTimelapseEveryNthLayer").Value);
            Assert.False(again.Find<NumericUpDown>("SettingsTimelapseInterval").IsVisible);
        });

    [Fact]
    public Task AnInvalidLayerStepShowsItsErrorAndGetsTheFocusOnSave() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha") with { Camera = WorkingCamera }));
            ui.Click("SettingsTabRecording");
            ui.Window.Settle();
            ui.ViewModel.SelectedTimelapseTrigger = ui.ViewModel.TimelapseTriggerOptions[1];

            ui.ViewModel.TimelapseEveryNthLayer = 0;
            ui.ViewModel.SelectedTabIndex = SettingsViewModel.GeneralTab;
            await ui.ViewModel.SaveCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            ui.Window.Settle();

            Assert.Equal(SettingsViewModel.RecordingTab, ui.ViewModel.SelectedTabIndex);
            TextBlock error = ui.Find<TextBlock>("SettingsTimelapseEveryNthLayerError");
            Assert.True(error.IsEffectivelyVisible);
            Assert.Equal(ui.ViewModel.TimelapseEveryNthLayerError, error.Text);
            Assert.False(ui.Find<TextBlock>("SettingsTimelapseIntervalError").IsVisible);
            NumericUpDown layers = ui.Find<NumericUpDown>("SettingsTimelapseEveryNthLayer");
            Assert.Contains("invalid", layers.Classes);
            Assert.True(layers.IsKeyboardFocusWithin);
        });

    private static void AssertBadge(Border badge, Window window)
    {
        Assert.True(badge.IsEffectivelyVisible);
        TextBlock text = Assert.IsType<TextBlock>(badge.Child);
        Assert.Equal(Strings.SettingsExperimental, text.Text);
        Assert.Equal(Strings.SettingsExperimentalHint, ToolTip.GetTip(badge));
        // The tip is mouse-only (the badge takes no focus): screen readers get its meaning as the help text.
        Assert.Equal(Strings.SettingsExperimentalHint, AutomationProperties.GetHelpText(badge));
        Assert.True(Bounds(badge, window).Right <= window.ClientSize.Width);
    }

    private static Rect Bounds(Control control, Window window) =>
        control.TranslatePoint(default, window) is { } origin ? new Rect(origin, control.Bounds.Size) : throw new InvalidOperationException("Not in the window.");
}
