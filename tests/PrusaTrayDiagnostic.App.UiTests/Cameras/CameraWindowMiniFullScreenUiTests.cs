using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.UiTests.Cameras;

/// <summary>The "Full screen" button of the mini preview, next to "back to the normal window" (T-040).</summary>
public sealed class CameraWindowMiniFullScreenUiTests(HeadlessSession session)
{
    private static readonly CameraWindowDefaults MiniFirst = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? throw new InvalidOperationException("Not in the window."), control.Bounds.Size);

    /// <summary>The mini preview at its minimum size (160 × 90).</summary>
    private static CameraWindow OpenSmallestMini(CameraHarness harness)
    {
        CameraWindow window = harness.Open();
        window.Settle();
        Assert.True(ViewModel(window).IsMini);
        window.Width = window.MinWidth;
        window.Height = window.MinHeight;
        window.Settle();
        Assert.Equal(new Size(160, 90), window.ClientSize);
        return window;
    }

    [Fact]
    public Task MiniPreviewShowsFullScreenNextToBackToTheNormalWindow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = OpenSmallestMini(harness);

            Button fullScreen = window.Find<Button>("CameraFullScreenButton");
            Button normal = window.Find<Button>("CameraNormalButton");
            Assert.True(fullScreen.IsEffectivelyVisible);
            Assert.True(normal.IsEffectivelyVisible);
            Assert.False(window.Find<Button>("CameraMiniButton").IsVisible);
            Assert.Equal("Full screen (F11)", ToolTip.GetTip(fullScreen));
            Assert.Equal("Full screen (F11)", AutomationProperties.GetName(fullScreen));

            // Side by side in the top right corner, not covering each other, inside the window.
            Rect full = BoundsIn(fullScreen, window);
            Rect back = BoundsIn(normal, window);
            Assert.False(full.Intersects(back));
            Assert.True(full.Right <= back.Left);
            Assert.Equal(full.Top, back.Top);
            Assert.True(full.Left >= 0 && full.Top >= 0);
            Assert.True(back.Right <= window.ClientSize.Width && back.Bottom <= window.ClientSize.Height);
            Assert.Equal(window.ClientSize.Width - 6, back.Right, 0.5);
            // The overlay keeps 6 + 30 + 4 + 30 + 4 = 74 DIP free on the right of a top-left overlay: both buttons fit in it.
            Assert.True(window.ClientSize.Width - full.Left <= 74);
            Assert.True(back.Bottom <= 40);
        });

    [Fact]
    public Task FullScreenButtonOfTheMiniPreviewEntersFullScreenAndEscReturnsToTheSameRect() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = OpenSmallestMini(harness);
            window.Position = new PixelPoint(1500, 800);
            window.Settle();
            PixelPoint position = window.Position;
            Size size = window.ClientSize;

            Ui.Click(window, Ui.CentreOf(window.Find<Button>("CameraFullScreenButton"), window));
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.True(ViewModel(window).IsFullScreen);
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.True(window.ShowInTaskbar);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.Settle();

            Assert.True(ViewModel(window).IsMini);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.False(window.ShowInTaskbar);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
        });

    [Fact]
    public Task EveryModeShowsTheButtonsOfTheOtherTwoInTheSameOrder() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);

            AssertButtons(window, "CameraFullScreenButton", "CameraMiniButton");

            viewModel.ShowFullScreenCommand.Execute(null);
            window.Settle();
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            AssertButtons(window, "CameraNormalButton", "CameraMiniButton");
            Assert.Equal("Back to the normal window (double-click)", ToolTip.GetTip(window.Find<Button>("CameraNormalButton")));

            viewModel.ShowMiniCommand.Execute(null);
            window.Settle();
            AssertButtons(window, "CameraFullScreenButton", "CameraNormalButton");

            // Full screen entered from the mini preview: the double-click leads back to the mini preview, not to the normal window.
            viewModel.ShowFullScreenCommand.Execute(null);
            window.Settle();
            AssertButtons(window, "CameraNormalButton", "CameraMiniButton");
            Button normal = window.Find<Button>("CameraNormalButton");
            Assert.Equal("Normal window", ToolTip.GetTip(normal));
            Assert.Equal("Normal window", AutomationProperties.GetName(normal));
        });

    [Fact]
    public Task NormalButtonInFullScreenFromTheMiniPreviewOpensTheNormalWindow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            CameraWindow window = harness.Open();
            window.Settle();
            // On the rotated monitor right of the primary one (x 2560 … 4000).
            window.Position = new PixelPoint(3000, 1500);
            window.Settle();
            Ui.Click(window, Ui.CentreOf(window.Find<Button>("CameraFullScreenButton"), window));
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            Ui.Click(window, Ui.CentreOf(window.Find<Button>("CameraNormalButton"), window));
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.True(ViewModel(window).IsNormal);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.True(window.ShowInTaskbar);
            Assert.False(window.Topmost);
            // The normal window by the defaults (never shown before): 960 × 540, on the monitor of the full screen (T-035 Q5).
            Assert.Equal(new Size(960, 540), window.ClientSize);
            Assert.InRange(window.Position.X, 2560, 4000 - 960);
            AssertButtons(window, "CameraFullScreenButton", "CameraMiniButton");
        });

    [Fact]
    public Task MiniButtonInFullScreenFromTheNormalWindowOpensTheMiniPreviewWhereItWas() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors());
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);
            viewModel.ShowMiniCommand.Execute(null);
            window.Settle();
            window.Position = new PixelPoint(1500, 800);
            window.Settle();
            PixelPoint miniPosition = window.Position;
            Size miniSize = window.ClientSize;
            viewModel.ShowNormalCommand.Execute(null);
            window.Settle();
            Ui.Click(window, Ui.CentreOf(window.Find<Button>("CameraFullScreenButton"), window));
            Dispatcher.UIThread.RunJobs();
            window.Settle();
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            Ui.Click(window, Ui.CentreOf(window.Find<Button>("CameraMiniButton"), window));
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.True(viewModel.IsMini);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.False(window.ShowInTaskbar);
            Assert.True(window.Topmost);
            Assert.Equal(miniPosition, window.Position);
            Assert.Equal(miniSize, window.ClientSize);
        });

    /// <summary>Exactly <paramref name="left"/> and <paramref name="right"/> are shown, side by side in the top right corner.</summary>
    private static void AssertButtons(CameraWindow window, string left, string right)
    {
        string[] shown = [.. window.Find<StackPanel>("CameraWindowButtons").Children
            .Where(static button => button.IsVisible)
            .Select(static button => AutomationProperties.GetAutomationId(button) ?? "")];
        Assert.Equal([left, right], shown);
        Rect first = BoundsIn(window.Find<Button>(left), window);
        Rect second = BoundsIn(window.Find<Button>(right), window);
        Assert.True(first.Right <= second.Left);
        Assert.Equal(window.ClientSize.Width - 6, second.Right, 0.5);
    }

    [Fact]
    public Task SnapshotOfTheSmallestMiniPreviewWithItsButtons() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(screens: FakeScreenCatalog.ThreeMonitors(), window: MiniFirst);
            (CameraWindow window, _) = await harness.OpenStreamingAsync(320, 180, 0xFF406080);
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            window.Settle();

            // The buttons appear while the pointer is over the window; the fade-in is skipped (it runs on real time).
            StackPanel buttons = window.Find<StackPanel>("CameraWindowButtons");
            Assert.Equal(0, buttons.Opacity);
            buttons.Transitions = null;
            window.MouseMove(new Point(40, 60), RawInputModifiers.None);
            window.Settle();

            Assert.Equal(1, buttons.Opacity);
            SnapshotAssert.Matches(window, "camera-mini-buttons");
        });
}
