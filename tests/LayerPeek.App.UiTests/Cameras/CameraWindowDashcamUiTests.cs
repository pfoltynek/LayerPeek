using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

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
/// "Save the last N min" in the context menu of the real camera window (T-148): only for a printer with a dashcam, enabled
/// while it holds the last minutes, otherwise disabled with the reason as its tip; it follows the service's change events
/// (any thread) and the language. The real dashcam with a camera: T-125 (Windows) and T-127 (macOS).
/// </summary>
public sealed class CameraWindowDashcamUiTests(HeadlessSession session)
{
    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static CameraWindow Open(CameraHarness harness)
    {
        CameraWindow window = harness.Open();
        window.Width = 960;
        window.Height = 540;
        window.Settle();
        CameraHarness.SkipWindowButtonFade(window);
        return window;
    }

    private static MenuItem Item(CameraWindow window) =>
        window.ContextMenu!.Items.OfType<MenuItem>().Single(static item => AutomationProperties.GetAutomationId(item) == "CameraSaveDashcamMenuItem");

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void RightClick(Window window)
    {
        window.Settle();
        Point point = new(100, 100);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Settle(window);
    }

    [Fact]
    public Task WithoutADashcamTheMenuHasNoSaveItem() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);

            RightClick(window);

            Assert.True(window.ContextMenu!.IsOpen);
            Assert.False(Item(window).IsVisible);
            Assert.False(ViewModel(window).HasDashcam);
            window.ContextMenu.Close();
        });

    [Fact]
    public Task WhileItBuffersTheItemSavesAndOtherwiseItIsDisabledWithTheReason() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetDashcam("a", CameraDashcamActivity.Waiting, minutes: 3, raise: false);
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            MenuItem item = Item(window);

            RightClick(window);
            Assert.True(item.IsVisible);
            Assert.False(item.IsEffectivelyEnabled);
            Assert.Equal("_Save the last 3 min", item.Header);
            Assert.Equal(Strings.DashcamNotPrinting, ToolTip.GetTip(item));
            window.ContextMenu!.Close();
            Settle(window);

            // The print starts (from another thread, like the service reports it).
            await Task.Run(() => recordings.SetDashcam("a", CameraDashcamActivity.Buffering, minutes: 3));
            Settle(window);
            RightClick(window);
            Assert.True(item.IsEffectivelyEnabled);
            Assert.Null(ToolTip.GetTip(item));
            window.ContextMenu.Close();
            Settle(window);

            ViewModel(window).SaveDashcamCommand.Execute(null);
            Assert.Equal(["a"], recordings.DashcamSaves);

            recordings.SetDashcam("a", CameraDashcamActivity.Saving, minutes: 3);
            Settle(window);
            Assert.False(item.IsEffectivelyEnabled);
            Assert.Equal(Strings.DashcamSaving, ToolTip.GetTip(item));
            ViewModel(window).SaveDashcamCommand.Execute(null);
            Assert.Equal(["a"], recordings.DashcamSaves);
        });

    [Fact]
    public Task WhileTheWholePrintIsRecordedTheReasonSaysSo() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);

            recordings.SetActivity("a", CameraRecordingActivity.Recording);
            recordings.SetDashcam("a", CameraDashcamActivity.Waiting);
            Settle(window);

            Assert.True(ViewModel(window).HasDashcam);
            Assert.Equal(Strings.DashcamWholePrintRecorded, ViewModel(window).SaveDashcamToolTip);
            Assert.False(ViewModel(window).SaveDashcamCommand.CanExecute(null));
        });

    [Fact]
    public Task TheItemFollowsALanguageSwitch() =>
        session.RunAsync(static async scope =>
        {
            FakeCameraRecordings recordings = new();
            recordings.SetDashcam("a", CameraDashcamActivity.Buffering, raise: false);
            await using CameraHarness harness = new(recordings: recordings);
            CameraWindow window = Open(harness);
            try
            {
                Assert.True(new UiLanguage().Apply("cs"));
                harness.Windows.RefreshTexts();
                Settle(window);
                Assert.Equal("Uložit posle_dních 2 min", ViewModel(window).SaveDashcamText);
            }
            finally
            {
                Assert.True(new UiLanguage().Apply("en"));
            }
        });
}
