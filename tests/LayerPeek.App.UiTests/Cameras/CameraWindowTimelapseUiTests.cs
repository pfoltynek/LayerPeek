using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Localization;
using LayerPeek.App.Resources;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Cameras.Recording;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The timelapse button of the real camera window (T-161, user decisions 2026-11-04; experimental): right before the record
/// button in every mode; hidden like the other window buttons until the pointer is over the window, but while a timelapse
/// runs it is the amber stopwatch with "TL" and stays visible (normal window, mini preview, full screen), dimmed while no
/// frames are taken. A click starts a manual timelapse while idle and stops it while it runs; while it starts or stops, and
/// without the recorder, it is disabled with the reason as its tip. The tip says "experimental". The state follows the
/// service's change events (any thread), independently of the recording. The real timelapse on a screen with a camera:
/// T-125 (Windows) and T-127 (macOS).
/// </summary>
public sealed class CameraWindowTimelapseUiTests(HeadlessSession session)
{
    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Button Timelapse(CameraWindow window) => window.Find<Button>("CameraTimelapseButton");

    private static CameraWindow Open(CameraHarness harness)
    {
        CameraWindow window = harness.Open();
        window.Settle();
        CameraHarness.SkipWindowButtonFade(window);
        return window;
    }

    private static void Settle(CameraWindow window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void MouseAway(CameraWindow window)
    {
        window.MouseMove(new Point(-10, -10));
        Settle(window);
    }

    [Fact]
    public Task WithoutRecordingsTheWindowHasNoTimelapseButton() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow window = Open(harness);

            Assert.False(Timelapse(window).IsVisible);
            Assert.False(ViewModel(window).HasTimelapseButton);
        });

    [Fact]
    public Task AClickStartsAndStopsAndTheIndicatorShowsWithoutThePointer() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            Button timelapse = Timelapse(window);
            TextBlock indicator = window.Find<TextBlock>("CameraTimelapseIndicator");

            MouseAway(window);
            Assert.True(timelapse.IsEffectivelyVisible);
            Assert.Equal(0, timelapse.Opacity);
            Assert.False(indicator.IsVisible);
            // The whole feature is experimental: the control says so (user decision 2026-11-04).
            Assert.Equal(Strings.TimelapseStart, ToolTip.GetTip(timelapse));
            Assert.Equal(Strings.TimelapseStart, AutomationProperties.GetName(timelapse));
            Assert.Contains("experimental", Strings.TimelapseStart, StringComparison.Ordinal);

            window.Click(timelapse);
            Settle(window);
            Assert.Equal(["a"], recordings.TimelapsesStarted);
            Assert.Empty(recordings.Started);

            await Task.Run(() => recordings.SetTimelapse("a", CameraRecordingActivity.Recording));
            Settle(window);
            MouseAway(window);
            Assert.True(ViewModel(window).IsTakingTimelapse);
            Assert.True(indicator.IsEffectivelyVisible);
            Assert.Equal(Strings.CameraTimelapseIndicator, indicator.Text);
            Assert.Equal(1, timelapse.Opacity);
            Assert.Equal(Strings.TimelapseStop, ToolTip.GetTip(timelapse));
            // The REC indicator stays off: the recording is independent.
            Assert.False(window.Find<TextBlock>("CameraRecordingIndicator").IsVisible);

            window.Click(timelapse);
            Settle(window);
            Assert.Equal(["a"], recordings.TimelapsesStopped);
            Assert.Equal(["a"], recordings.TimelapsesStarted);

            await Task.Run(() => recordings.SetTimelapse("a", CameraRecordingActivity.Idle));
            Settle(window);
            MouseAway(window);
            Assert.False(indicator.IsVisible);
            Assert.Equal(0, timelapse.Opacity);
        });

    [Fact]
    public Task WhilePausedTheIndicatorIsDimmedAndTheTipSaysWhy() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            Button timelapse = Timelapse(window);

            recordings.SetTimelapse("a", CameraRecordingActivity.Recording, capturing: false);
            Settle(window);
            MouseAway(window);

            Assert.Contains("paused", timelapse.Classes);
            Assert.True(timelapse.IsEffectivelyEnabled);
            Assert.True(window.Find<TextBlock>("CameraTimelapseIndicator").IsEffectivelyVisible);
            Assert.Equal(Strings.TimelapseStop + Environment.NewLine + Strings.TimelapsePaused, ToolTip.GetTip(timelapse));
            StackPanel content = timelapse.GetVisualDescendants().OfType<StackPanel>().Single(static panel => panel.Classes.Contains("timelapseContent"));
            Assert.Equal(0.6, content.Opacity, 3);

            recordings.SetTimelapse("a", CameraRecordingActivity.Recording);
            Settle(window);
            Assert.DoesNotContain("paused", timelapse.Classes);
            Assert.Equal(1, content.Opacity);
        });

    [Theory]
    [InlineData(CameraRecordingActivity.Starting)]
    [InlineData(CameraRecordingActivity.Stopping)]
    public Task WhileItStartsOrStopsTheButtonIsDisabledWithTheReason(CameraRecordingActivity activity) =>
        session.RunAsync(async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);

            recordings.SetTimelapse("a", activity);
            Settle(window);
            MouseAway(window);

            Button timelapse = Timelapse(window);
            Assert.False(timelapse.IsEffectivelyEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(timelapse));
            Assert.Equal(activity == CameraRecordingActivity.Starting ? Strings.TimelapseStarting : Strings.TimelapseStopping, ToolTip.GetTip(timelapse));
            Assert.True(window.Find<TextBlock>("CameraTimelapseIndicator").IsEffectivelyVisible);
            Assert.Equal(1, timelapse.Opacity);
            ViewModel(window).ToggleTimelapseCommand.Execute(null);
            Assert.Empty(recordings.TimelapsesStarted);
            Assert.Empty(recordings.TimelapsesStopped);
        });

    [Fact]
    public Task WithoutTheRecorderTheButtonIsDisabledWithTheReason() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new() { IsTimelapseAvailable = false };
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);

            Button timelapse = Timelapse(window);
            Assert.False(timelapse.IsEffectivelyEnabled);
            Assert.Equal(Strings.TimelapseUnavailable, ToolTip.GetTip(timelapse));
            Assert.True(window.Find<Button>("CameraRecordButton").IsEffectivelyEnabled);
        });

    [Fact]
    public Task OtherPrintersDoNotChangeTheButton() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            int reads = recordings.StatusReads;

            recordings.SetTimelapse("b", CameraRecordingActivity.Recording);
            Settle(window);

            Assert.False(ViewModel(window).IsTakingTimelapse);
            Assert.Equal(reads, recordings.StatusReads);
        });

    [Fact]
    public Task TheButtonIsInEveryModeRightBeforeTheRecordButton() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetTimelapse("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            CameraViewModel viewModel = ViewModel(window);

            foreach (Action mode in (Action[])[() => { }, () => viewModel.ShowMiniCommand.Execute(null), () => viewModel.ShowFullScreenCommand.Execute(null)])
            {
                mode();
                Settle(window);
                MouseAway(window);
                Control[] shown = [.. window.Find<StackPanel>("CameraWindowButtons").Children.Where(static button => button.IsVisible)];
                Button timelapse = Timelapse(window);
                Assert.True(timelapse.IsEffectivelyVisible, viewModel.Mode.ToString());
                Assert.Equal(1, timelapse.Opacity);
                Assert.True(window.Find<TextBlock>("CameraTimelapseIndicator").IsEffectivelyVisible, viewModel.Mode.ToString());
                Assert.Equal(Array.IndexOf(shown, window.Find<Button>("CameraRecordButton")) - 1, Array.IndexOf(shown, timelapse));
            }
        });

    [Fact]
    public Task OverARunningTimelapseTheStopwatchBecomesTheStopSquare() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetTimelapse("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            Button timelapse = Timelapse(window);
            Avalonia.Controls.Shapes.Path stopwatch = timelapse.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            Rectangle stop = timelapse.GetVisualDescendants().OfType<Rectangle>().Single();

            MouseAway(window);
            Assert.True(stopwatch.IsVisible);
            Assert.False(stop.IsVisible);
            Assert.Equal(Avalonia.Media.Color.Parse("#F5A524"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(stopwatch.Stroke).Color);

            window.MouseMove(Ui.CentreOf(timelapse, window));
            Settle(window);
            Assert.False(stopwatch.IsVisible);
            Assert.True(stop.IsVisible);
        });

    [Fact]
    public Task TheTipFollowsALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            try
            {
                Assert.True(new UiLanguage().Apply("cs"));
                harness.Windows.RefreshTexts();
                Settle(window);
                Assert.Equal("Spustit timelapse (experimentální)", ToolTip.GetTip(Timelapse(window)));
            }
            finally
            {
                Assert.True(new UiLanguage().Apply("en"));
            }
        });

    [Fact]
    public Task SnapshotOfANormalWindowTakingATimelapseAndRecording() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetActivity("a", CameraRecordingActivity.Recording, raise: false);
            recordings.SetTimelapse("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(recordings: recordings);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(960, 540, 0xFF406080);
            CameraHarness.SkipWindowButtonFade(window);
            MouseAway(window);

            // Without the pointer only the two indicators show.
            SnapshotAssert.Matches(window, "camera-timelapse");
        });

    [Fact]
    public Task SnapshotOfTheSmallestMiniPreviewTakingATimelapseWithItsButtons() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetActivity("a", CameraRecordingActivity.Recording, raise: false);
            recordings.SetTimelapse("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), recordings: recordings);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(320, 180, 0xFF406080);
            ViewModel(window).ShowMiniCommand.Execute(null);
            Settle(window);
            CameraHarness.SkipWindowButtonFade(window);
            CameraHarness.ResizeMiniWithGrip(window, window.MinWidth, window.MinHeight);
            Assert.Equal(new Size(240, 168), window.ClientSize);

            MouseAway(window);
            SnapshotAssert.Matches(window, "camera-mini-timelapse");

            // With the pointer over the image all buttons (REC and TL both shown) fit next to each other.
            window.MouseMove(new Point(40, 60), RawInputModifiers.None);
            window.Settle();
            Rect[] bounds = [.. window.Find<StackPanel>("CameraWindowButtons").Children.Where(static b => b.IsVisible).Select(b => b.Bounds)];
            Assert.True(bounds[0].Left >= 0);
            for (int i = 1; i < bounds.Length; i++)
            {
                Assert.True(bounds[i - 1].Right <= bounds[i].Left);
            }

            SnapshotAssert.Matches(window, "camera-mini-timelapse-buttons");
        });
}
