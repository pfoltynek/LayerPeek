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
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The record button of the real camera window (T-146, user decisions 2026-11-03): right before the pin in every mode; hidden
/// like the other window buttons until the pointer is over the window, but while anything records it is the red dot with
/// "REC" and stays visible (normal window, mini preview, full screen). A click starts a manual recording while idle and stops
/// the recording while it shows REC; while the recording starts or stops, and in a build without the recorder, it is
/// disabled with the reason as its tip. The state follows the service's change events, which come on any thread. The real
/// recording on a screen with a camera: T-125 (Windows) and T-127 (macOS).
/// </summary>
public sealed class CameraWindowRecordingUiTests(HeadlessSession session)
{
    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Button Record(CameraWindow window) => window.Find<Button>("CameraRecordButton");

    private static CameraWindow Open(CameraHarness harness, CameraWindowStartMode mode = CameraWindowStartMode.Normal)
    {
        CameraWindow window = harness.Open();
        window.Settle();
        CameraHarness.SkipWindowButtonFade(window);
        if (mode == CameraWindowStartMode.Mini)
        {
            ViewModel(window).ShowMiniCommand.Execute(null);
            window.Settle();
        }

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
    public Task WithoutRecordingsTheWindowHasNoRecordButton() =>
        session.RunAsync(static async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow window = Open(harness);

            Assert.False(Record(window).IsVisible);
            Assert.False(ViewModel(window).HasRecordingButton);
        });

    [Fact]
    public Task AClickStartsAndStopsAndRecShowsWithoutThePointer() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            Button record = Record(window);
            TextBlock rec = window.Find<TextBlock>("CameraRecordingIndicator");

            // Idle: hidden like the other buttons until the pointer comes; the red dot, no "REC".
            MouseAway(window);
            Assert.True(record.IsEffectivelyVisible);
            Assert.Equal(0, record.Opacity);
            Assert.False(rec.IsVisible);
            Assert.Equal(Strings.RecordingStart, ToolTip.GetTip(record));
            Assert.Equal(Strings.RecordingStart, AutomationProperties.GetName(record));

            window.Click(record);
            Settle(window);
            Assert.Equal(["a"], recordings.Started);

            // The service reports the recording (from another thread, like the real one).
            await Task.Run(() => recordings.SetActivity("a", CameraRecordingActivity.Recording));
            Settle(window);
            MouseAway(window);
            Assert.True(ViewModel(window).IsRecording);
            Assert.True(rec.IsEffectivelyVisible);
            Assert.Equal(Strings.CameraRecordingIndicator, rec.Text);
            Assert.Equal(1, record.Opacity);
            Assert.Equal(Strings.RecordingStop, ToolTip.GetTip(record));

            window.Click(record);
            Settle(window);
            Assert.Equal(["a"], recordings.Stopped);
            Assert.Equal(["a"], recordings.Started);

            await Task.Run(() => recordings.SetActivity("a", CameraRecordingActivity.Idle));
            Settle(window);
            MouseAway(window);
            Assert.False(rec.IsVisible);
            Assert.Equal(0, record.Opacity);
        });

    [Fact]
    public Task OtherPrintersDoNotChangeTheButton() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            int reads = recordings.StatusReads;

            recordings.SetActivity("b", CameraRecordingActivity.Recording);
            Settle(window);

            Assert.False(ViewModel(window).IsRecording);
            // A change of another printer reads nothing (the window filters by its printer before posting).
            Assert.Equal(reads, recordings.StatusReads);
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

            recordings.SetActivity("a", activity);
            Settle(window);
            MouseAway(window);

            Button record = Record(window);
            Assert.False(record.IsEffectivelyEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(record));
            Assert.Equal(activity == CameraRecordingActivity.Starting ? Strings.RecordingStarting : Strings.RecordingStopping, ToolTip.GetTip(record));
            // Still the REC indicator: something records.
            Assert.True(window.Find<TextBlock>("CameraRecordingIndicator").IsEffectivelyVisible);
            Assert.Equal(1, record.Opacity);
            ViewModel(window).ToggleRecordingCommand.Execute(null);
            Assert.Empty(recordings.Started);
            Assert.Empty(recordings.Stopped);
        });

    [Fact]
    public Task WithoutTheRecorderTheButtonIsDisabledWithTheReason() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new() { IsAvailable = false };
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);

            Button record = Record(window);
            Assert.False(record.IsEffectivelyEnabled);
            Assert.Equal(Strings.RecordingUnavailable, ToolTip.GetTip(record));
        });

    [Fact]
    public Task TheButtonIsInEveryModeRightBeforeThePin() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetActivity("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            CameraViewModel viewModel = ViewModel(window);

            foreach (Action mode in (Action[])[() => { }, () => viewModel.ShowMiniCommand.Execute(null), () => viewModel.ShowFullScreenCommand.Execute(null)])
            {
                mode();
                Settle(window);
                MouseAway(window);
                Control[] shown = [.. window.Find<StackPanel>("CameraWindowButtons").Children.Where(static button => button.IsVisible)];
                Button record = Record(window);
                Assert.True(record.IsEffectivelyVisible, viewModel.Mode.ToString());
                Assert.Equal(1, record.Opacity);
                int index = Array.IndexOf(shown, record);
                // Before the pin (the last button), or the last one in full screen (no pin there).
                Assert.Equal(viewModel.IsFullScreen ? shown.Length - 1 : shown.Length - 2, index);
            }
        });

    [Fact]
    public Task OverARecordingButtonTheDotBecomesTheStopSquare() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetActivity("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            Button record = Record(window);
            Ellipse dot = record.GetVisualDescendants().OfType<Ellipse>().Single();
            Rectangle stop = record.GetVisualDescendants().OfType<Rectangle>().Single();

            MouseAway(window);
            Assert.True(dot.IsVisible);
            Assert.False(stop.IsVisible);

            window.MouseMove(Ui.CentreOf(record, window));
            Settle(window);
            Assert.False(dot.IsVisible);
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
                Assert.Equal("Nahrávat", ToolTip.GetTip(Record(window)));
            }
            finally
            {
                Assert.True(new UiLanguage().Apply("en"));
            }
        });

    [Fact]
    public Task ClosingTheWindowUnsubscribes() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            Assert.Equal(1, recordings.ChangedSubscribers);

            window.Close();
            Settle(window);

            Assert.Equal(0, recordings.ChangedSubscribers);
        });

    [Fact]
    public Task SnapshotOfARecordingNormalWindow() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetActivity("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(recordings: recordings);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(960, 540, 0xFF406080);
            CameraHarness.SkipWindowButtonFade(window);
            MouseAway(window);

            // Without the pointer only the REC indicator shows.
            SnapshotAssert.Matches(window, "camera-recording");
        });

    [Fact]
    public Task SnapshotOfTheSmallestRecordingMiniPreviewWithItsButtons() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetActivity("a", CameraRecordingActivity.Recording, raise: false);
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), recordings: recordings);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(320, 180, 0xFF406080);
            ViewModel(window).ShowMiniCommand.Execute(null);
            Settle(window);
            CameraHarness.SkipWindowButtonFade(window);
            CameraHarness.ResizeMiniWithGrip(window, window.MinWidth, window.MinHeight);
            Assert.Equal(new Size(240, 168), window.ClientSize);

            MouseAway(window);
            SnapshotAssert.Matches(window, "camera-mini-recording");

            // With the pointer over the image all buttons fit next to each other in the smallest mini preview.
            window.MouseMove(new Point(40, 60), RawInputModifiers.None);
            window.Settle();
            Rect[] bounds = [.. window.Find<StackPanel>("CameraWindowButtons").Children.Where(static b => b.IsVisible).Select(b => b.Bounds)];
            Assert.True(bounds[0].Left >= 0);
            for (int i = 1; i < bounds.Length; i++)
            {
                Assert.True(bounds[i - 1].Right <= bounds[i].Left);
            }

            SnapshotAssert.Matches(window, "camera-mini-recording-buttons");
        });
}
