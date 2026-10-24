using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Localization;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// "Always on top" of the real camera window (T-037): <see cref="Window.Topmost"/> per mode from the printer's options
/// (normal window, mini preview; full screen never), also after switching modes and after saving changed settings while
/// the window is open (without moving or resizing it). The real z-order above other applications is a manual check.
/// </summary>
public sealed class CameraWindowTopmostUiTests(HeadlessSession session)
{
    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void Save(CameraHarness harness, CameraWindowDefaults defaults)
    {
        // What saving the settings does for an open window: the monitoring session applies the new printer definitions.
        ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
        {
            Printers = [CameraHarness.CameraPrinter("a", "Alpha", defaults), CameraHarness.CameraPrinter("b", "Beta")],
        });
        Dispatcher.UIThread.RunJobs();
    }

    public static TheoryData<bool, bool> Options => new() { { false, false }, { false, true }, { true, false }, { true, true } };

    [Theory]
    [MemberData(nameof(Options))]
    public Task TopmostFollowsTheOptionInEveryModeAndBackAndForth(bool normal, bool mini) =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal, TopmostNormal = normal, TopmostMini = mini };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);

            Assert.True(viewModel.IsNormal);
            Assert.Equal(normal, window.Topmost);

            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.True(viewModel.IsMini);
            Assert.Equal(mini, window.Topmost);

            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.Equal(WindowState.FullScreen, window.WindowState);
            Assert.False(window.Topmost);

            // Esc leaves full screen to the mode it was entered from (user decision 2026-10-20): the mini preview.
            Run(window, () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(viewModel.IsMini);
            Assert.Equal(mini, window.Topmost);

            Run(window, () => viewModel.ShowNormalCommand.Execute(null));
            Assert.True(viewModel.IsNormal);
            Assert.Equal(normal, window.Topmost);

            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.True(viewModel.IsFullScreen);
            Assert.False(window.Topmost);

            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.True(viewModel.IsNormal);
            Assert.Equal(normal, window.Topmost);
        });

    [Theory]
    [InlineData(CameraWindowStartMode.Normal, false, true, false)]
    [InlineData(CameraWindowStartMode.Normal, true, false, true)]
    [InlineData(CameraWindowStartMode.Mini, false, true, true)]
    [InlineData(CameraWindowStartMode.Mini, true, false, false)]
    [InlineData(CameraWindowStartMode.FullScreen, true, true, false)]
    public Task WindowOpensWithTheTopmostOfItsStartMode(CameraWindowStartMode startMode, bool normal, bool mini, bool expected) =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = startMode, TopmostNormal = normal, TopmostMini = mini };
            await using CameraHarness harness = new(window: defaults);

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.Equal(expected, window.Topmost);
            Assert.Equal(startMode == CameraWindowStartMode.FullScreen, window.WindowState == WindowState.FullScreen);
        });

    [Fact]
    public Task DefaultsKeepTodaysBehaviour() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);

            Assert.False(window.Topmost);
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.True(window.Topmost);
        });

    [Fact]
    public Task SavedOptionAppliesToTheOpenNormalWindowWithoutMovingIt() =>
        session.RunAsync(async scope =>
        {
            MemoryCameraWindowStateStore states = new();
            await using CameraHarness harness = new(states, window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });
            CameraWindow window = harness.Open();
            window.Settle();
            window.Position = new PixelPoint(123, 45);
            window.Width = 700;
            window.Height = 400;
            window.Settle();
            PixelPoint position = window.Position;
            Size size = window.ClientSize;
            Assert.False(window.Topmost);

            Save(harness, CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal, TopmostNormal = true });
            window.Settle();

            Assert.True(window.Topmost);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.True(ViewModel(window).IsNormal);

            Save(harness, CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal });
            window.Settle();

            Assert.False(window.Topmost);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);

            // The open window remembers its placement as before (T-035 Q3: topmost does not forget the state).
            window.Close();
            Assert.Equal(0, states.GetRemoveCount("a"));
            Assert.Equal(new CameraWindowPlacement(position.X, position.Y, size.Width, size.Height), states.Get("a").Normal);
        });

    [Fact]
    public Task SavedOptionAppliesToTheOpenMiniPreviewWithoutMovingIt() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            PixelPoint position = window.Position;
            Size size = window.ClientSize;
            Assert.True(window.Topmost);

            Save(harness, defaults with { TopmostMini = false });
            window.Settle();

            Assert.False(window.Topmost);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);

            // The new option also holds after switching modes.
            CameraViewModel viewModel = ViewModel(window);
            Run(window, () => viewModel.ShowNormalCommand.Execute(null));
            Assert.False(window.Topmost);
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.False(window.Topmost);
        });

    [Fact]
    public Task SavedOptionsDoNotMakeFullScreenTopmost() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.FullScreen };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            Save(harness, defaults with { TopmostNormal = true, TopmostMini = true });
            window.Settle();

            Assert.False(window.Topmost);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            // Leaving full screen applies the saved option of the normal window.
            Run(window, () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.True(window.Topmost);
        });

    // Consecutive double-clicks land on different points so that they are not taken as one longer click series.
    private static int _doubleClicks;

    /// <summary>A real double-click on the image (two clicks: the window's ClickCount == 2 path).</summary>
    private static void DoubleClickImage(CameraWindow window)
    {
        window.Settle();
        double offset = (_doubleClicks++ % 4) * 20;
        Point point = new((window.ClientSize.Width / 2) - 30 + offset, window.ClientSize.Height / 2);
        Ui.Click(window, point);
        Ui.Click(window, point);
        window.Settle();
    }

    [Theory]
    [InlineData("F11", false)]
    [InlineData("Esc", false)]
    [InlineData("double-click", false)]
    [InlineData("system", false)]
    [InlineData("F11", true)]
    [InlineData("Esc", true)]
    [InlineData("double-click", true)]
    [InlineData("system", true)]
    public Task FullScreenEnteredFromTheMiniPreviewReturnsToItWhereItWas(string exit, bool mini) =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini, TopmostMini = mini };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            CameraViewModel viewModel = ViewModel(window);
            window.Position = new PixelPoint(210, 120);
            window.Width = 400;
            window.Height = 225;
            window.Settle();
            PixelPoint position = window.Position;
            Size size = window.ClientSize;

            Run(window, () => window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null));
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            Run(window, () =>
            {
                if (exit == "F11")
                {
                    window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null);
                }
                else if (exit == "Esc")
                {
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                }
                else if (exit == "double-click")
                {
                    DoubleClickImage(window);
                }
                else
                {
                    // The system ends full screen (e.g. a shell gesture), not the app.
                    window.WindowState = WindowState.Normal;
                }
            });

            Assert.True(viewModel.IsMini);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Equal(mini, window.Topmost);
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);
        });

    [Fact]
    public Task FullScreenAtStartLeavesToTheNormalWindow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new(window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.FullScreen });
            CameraWindow window = harness.Open();
            window.Settle();

            Run(window, () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));

            Assert.True(ViewModel(window).IsNormal);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            Assert.False(window.Topmost);
        });

    private static Button? MiniButton(CameraWindow window, string toolTip) =>
        window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => ToolTip.GetTip(button) as string == toolTip);

    [Fact]
    public Task MiniButtonToolTipFollowsTheSavedOptionAndTheLanguage() =>
        session.RunAsync(async scope =>
        {
            // User decision 2026-10-20 (T-037 Q1).
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            Button button = Assert.IsType<Button>(MiniButton(window, "Mini preview, always on top"));
            Assert.Equal("Mini preview, always on top", AutomationProperties.GetName(button));

            Save(harness, defaults with { TopmostMini = false });
            window.Settle();

            Assert.Equal("Preview", ToolTip.GetTip(button));
            Assert.Equal("Preview", AutomationProperties.GetName(button));

            UiLanguage language = new();
            try
            {
                Assert.True(language.Apply("cs"));
                harness.Windows.RefreshTexts();
                window.Settle();
                Assert.Equal("Náhled", ToolTip.GetTip(button));

                Save(harness, defaults);
                window.Settle();
                Assert.Equal("Malý náhled vždy navrchu", ToolTip.GetTip(button));
                Assert.Equal("Malý náhled vždy navrchu", AutomationProperties.GetName(button));
            }
            finally
            {
                language.Apply(null);
                harness.Windows.RefreshTexts();
            }
        });

    [Fact]
    public Task SavedOptionChangesOnlyThatPrintersWindow() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };
            await using CameraHarness harness = new(window: defaults);
            CameraWindow alpha = harness.Open("a");
            CameraWindow beta = harness.Open("b");
            alpha.Settle();
            beta.Settle();

            ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings
            {
                Printers = [CameraHarness.CameraPrinter("a", "Alpha", defaults with { TopmostNormal = true }), CameraHarness.CameraPrinter("b", "Beta", defaults)],
            });
            Dispatcher.UIThread.RunJobs();

            Assert.True(alpha.Topmost);
            Assert.False(beta.Topmost);
        });
}
