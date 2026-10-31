using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Overlay;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The pin of the real camera window (T-117, design "Návrh vzhledu"): the last window button in the normal window and the
/// mini preview, none in full screen; filled while "always on top" is on, regular while it is off; the checked pin stays
/// visible dimmed without the pointer; a click, Space or the context menu item (also in the mini preview) switches
/// <see cref="Window.Topmost"/> at once, the taskbar button follows (T-102), and only that option of the printer is saved;
/// a failed save switches back. The real z-order and the look on a real screen are manual checks (T-114, T-115).
/// </summary>
public sealed class CameraWindowPinUiTests(HeadlessSession session)
{
    private const double Dimmed = 0.85;

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static ToggleButton Pin(CameraWindow window) => window.Find<ToggleButton>("CameraTopmostButton");

    private static Geometry? IconOf(ToggleButton pin) => pin.GetVisualDescendants().OfType<OverlayIcon>().Single().Data;

    private static StreamGeometry Resource(CameraWindow window, string key) =>
        Assert.IsType<StreamGeometry>(window.FindResource(key), exactMatch: false);

    private static CameraWindow Open(CameraHarness harness)
    {
        CameraWindow window = harness.Open();
        window.Settle();
        CameraHarness.SkipWindowButtonFade(window);
        return window;
    }

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void MouseAway(CameraWindow window) => Run(window, () => window.MouseMove(new Point(-10, -10)));

    private static CameraHarness Harness(CameraWindowStartMode startMode = CameraWindowStartMode.Normal, bool normal = false, bool mini = true) =>
        new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { StartMode = startMode, TopmostNormal = normal, TopmostMini = mini });

    [Fact]
    public Task PinIsTheLastButtonShowsTheStateOfTheModeAndIsMissingInFullScreen() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            CameraViewModel viewModel = ViewModel(window);
            ToggleButton pin = Pin(window);
            StackPanel buttons = window.Find<StackPanel>("CameraWindowButtons");

            Assert.Same(pin, buttons.Children[^1]);
            Assert.True(pin.IsEffectivelyVisible);
            Assert.False(pin.IsChecked);
            Assert.Equal("Always on top: off", ToolTip.GetTip(pin));
            Assert.Equal("Always on top: off", AutomationProperties.GetName(pin));
            Assert.Same(Resource(window, "IconPin16Regular"), IconOf(pin));
            Rect bounds = pin.TranslatePoint(default, window) is { } origin ? new Rect(origin, pin.Bounds.Size) : default;
            Assert.Equal(new Size(30, 30), bounds.Size);
            Assert.Equal(window.ClientSize.Width - 6, bounds.Right, 0.5);
            Assert.Equal(6, bounds.Top, 0.5);

            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.True(pin.IsEffectivelyVisible);
            Assert.True(pin.IsChecked);
            Assert.Equal("Always on top: on", AutomationProperties.GetName(pin));
            Assert.Same(Resource(window, "IconPin16Filled"), IconOf(pin));
            Rect mini = new(pin.TranslatePoint(default, window)!.Value, pin.Bounds.Size);
            Assert.Equal(window.ClientSize.Width - 6, mini.Right, 0.5);

            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            Assert.True(viewModel.IsFullScreen);
            Assert.False(pin.IsVisible);

            // The layout mode hides the whole row, the pin with it.
            Run(window, () => window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null));
            Run(window, () => viewModel.ShowNormalCommand.Execute(null));
            Run(window, () => viewModel.EditLayoutCommand.Execute(null));
            Assert.True(viewModel.IsLayoutModeActive);
            Assert.False(pin.IsEffectivelyVisible);
        });

    [Fact]
    public Task PinFollowsOptionsSavedInTheSettings() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal };
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ToggleButton pin = Pin(window);
            Assert.False(pin.IsChecked);

            // What saving the settings does for an open window: the monitoring session applies the new printer definitions.
            Run(window, () => ((LayerPeek.App.Tray.IMonitoringObserver)harness.Printers).ApplySettings(new LayerPeek.Core.Settings.AppSettings
            {
                Printers = [CameraHarness.CameraPrinter("a", "Alpha", defaults with { TopmostNormal = true }), CameraHarness.CameraPrinter("b", "Beta")],
            }));

            Assert.True(window.Topmost);
            Assert.True(pin.IsChecked);
            Assert.Equal("Always on top: on", AutomationProperties.GetName(pin));
            Assert.Same(Resource(window, "IconPin16Filled"), IconOf(pin));
            Assert.Empty(harness.TopmostWriter.Saves);
        });

    [Fact]
    public Task CheckedPinStaysVisibleDimmedAndTheRowShowsOnHoverAndFocus() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(normal: true);
            CameraWindow window = Open(harness);
            ToggleButton pin = Pin(window);
            Button fullScreen = window.Find<Button>("CameraFullScreenButton");
            MouseAway(window);

            Assert.Equal(Dimmed, pin.Opacity, 3);
            Assert.Equal(0, fullScreen.Opacity);
            Assert.Equal(0, window.Find<Button>("CameraLayoutButton").Opacity);
            Assert.Equal(0, window.Find<Button>("CameraMiniButton").Opacity);
            // The row itself is not dimmed (its opacity would multiply).
            Assert.Equal(1, window.Find<StackPanel>("CameraWindowButtons").Opacity);

            // The pointer over the window shows the whole row, the pin at full strength.
            Run(window, () => window.MouseMove(new Point(200, 200)));
            Assert.Equal(1, pin.Opacity);
            Assert.Equal(1, fullScreen.Opacity);

            // Off: hidden without the pointer like the other buttons.
            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.False(pin.IsChecked);
            MouseAway(window);
            Assert.Equal(0, pin.Opacity);

            // Keyboard focus on a button shows the whole row too.
            Assert.True(fullScreen.Focus(NavigationMethod.Tab));
            window.Settle();
            Assert.Equal(1, pin.Opacity);
            Assert.Equal(1, fullScreen.Opacity);
        });

    [Fact]
    public Task CheckedPinKeepsTheDarkBackgroundOfTheRow() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness(normal: true);
            CameraWindow window = Open(harness);
            ToggleButton pin = Pin(window);
            Button fullScreen = window.Find<Button>("CameraFullScreenButton");

            static IBrush? BackgroundOf(Control button) => button.GetVisualDescendants().OfType<ContentPresenter>().First(static p => p.Name == "PART_ContentPresenter").Background;

            static Color? ColourOf(IBrush? brush) => (brush as ISolidColorBrush)?.Color;

            MouseAway(window);
            Assert.True(pin.IsChecked);
            // No accent background for the checked state (design 4): the same as an unchecked button of the row.
            Assert.Equal<Color?>(Color.Parse("#99000000"), ColourOf(BackgroundOf(pin)));
            Assert.Equal<Color?>(Color.Parse("#99000000"), ColourOf(BackgroundOf(fullScreen)));

            // Hover: dark for every button of the row (Q-D2), checked or not.
            Run(window, () => window.MouseMove(Ui.CentreOf(pin, window)));
            Assert.Equal<Color?>(Color.Parse("#B3000000"), ColourOf(BackgroundOf(pin)));
            Run(window, () => window.MouseMove(Ui.CentreOf(fullScreen, window)));
            Assert.Equal<Color?>(Color.Parse("#B3000000"), ColourOf(BackgroundOf(fullScreen)));
            Assert.Equal<Color?>(Color.Parse("#99000000"), ColourOf(BackgroundOf(pin)));
        });

    [Fact]
    public Task ClickSwitchesTopmostAtOnceSavesOnlyThatOptionAndTheTaskbarButtonFollows() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            CameraViewModel viewModel = ViewModel(window);
            ToggleButton pin = Pin(window);
            PixelPoint position = window.Position;
            Size size = window.ClientSize;
            Assert.False(window.Topmost);
            Assert.True(window.ShowInTaskbar);

            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));

            Assert.True(window.Topmost);
            Assert.False(window.ShowInTaskbar);
            Assert.True(pin.IsChecked);
            Assert.Equal("Always on top: on", AutomationProperties.GetName(pin));
            Assert.Equal([("a", CameraWindowMode.Normal, true)], harness.TopmostWriter.Saves);
            // Only the z-order changes, never the place (T-037).
            Assert.Equal(position, window.Position);
            Assert.Equal(size, window.ClientSize);

            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.False(window.Topmost);
            Assert.True(window.ShowInTaskbar);

            // The mini preview saves its own option.
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            Assert.True(window.Topmost);
            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.False(window.Topmost);
            Assert.True(window.ShowInTaskbar);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Equal(
                [("a", CameraWindowMode.Normal, true), ("a", CameraWindowMode.Normal, false), ("a", CameraWindowMode.Mini, false)],
                harness.TopmostWriter.Saves);
            await viewModel.TopmostSaves.WaitAsync(TestTimeouts.Wait);
        });

    [Fact]
    public Task SpaceSwitchesThePinAndTheFocusStaysOnIt() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ToggleButton pin = Pin(window);
            Assert.True(pin.Focus(NavigationMethod.Tab));

            Run(window, () =>
            {
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            });

            Assert.True(window.Topmost);
            Assert.True(pin.IsChecked);
            Assert.Same(pin, window.FocusManager?.GetFocusedElement());

            Run(window, () => window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r"));
            Assert.False(window.Topmost);
            Assert.Same(pin, window.FocusManager?.GetFocusedElement());
        });

    [Fact]
    public Task FailedSaveSwitchesTheWindowBack() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            ToggleButton pin = Pin(window);
            harness.TopmostWriter.Hold();

            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.True(window.Topmost);
            Assert.False(window.ShowInTaskbar);

            harness.TopmostWriter.Failure = new IOException("The settings file is locked.");
            harness.TopmostWriter.Release(false);
            await ViewModel(window).TopmostSaves.WaitAsync(TestTimeouts.Wait);
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.False(window.Topmost);
            Assert.True(window.ShowInTaskbar);
            Assert.False(pin.IsChecked);
            Assert.Equal("Always on top: off", AutomationProperties.GetName(pin));
        });

    [Fact]
    public Task ContextMenuItemIsCheckedLikeThePinAndSwitchesInBothModes() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness);
            CameraViewModel viewModel = ViewModel(window);
            ContextMenu menu = window.ContextMenu!;
            MenuItem layout = menu.Items.OfType<MenuItem>().Single(static item => AutomationProperties.GetAutomationId(item) == "CameraLayoutMenuItem");
            MenuItem topmost = menu.Items.OfType<MenuItem>().Single(static item => AutomationProperties.GetAutomationId(item) == "CameraTopmostMenuItem");
            Assert.Equal("Always on _top", topmost.Header);
            Assert.Equal(MenuItemToggleType.CheckBox, topmost.ToggleType);
            Assert.Null(topmost.Icon);

            // Normal window: both items; "always on top" stays enabled where the layout item is disabled (below 480 × 300).
            window.Width = 400;
            window.Height = 250;
            RightClick(window);
            Assert.True(menu.IsOpen);
            Assert.True(layout.IsVisible);
            Assert.False(layout.IsEffectivelyEnabled);
            Assert.True(topmost.IsVisible);
            Assert.True(topmost.IsEffectivelyEnabled);
            Assert.False(topmost.IsChecked);
            Click(window, topmost);
            Assert.True(window.Topmost);
            Assert.True(topmost.IsChecked);
            Assert.True(Pin(window).IsChecked);

            // Mini preview: only "always on top", checked by its own option.
            Run(window, () => viewModel.ShowMiniCommand.Execute(null));
            RightClick(window);
            Assert.True(menu.IsOpen);
            Assert.False(layout.IsVisible);
            Assert.True(topmost.IsVisible);
            Assert.True(topmost.IsChecked);
            Click(window, topmost);
            Assert.False(window.Topmost);
            Assert.False(topmost.IsChecked);
            Assert.Equal([("a", CameraWindowMode.Normal, true), ("a", CameraWindowMode.Mini, false)], harness.TopmostWriter.Saves);

            // No menu in full screen.
            Run(window, () => viewModel.ToggleFullScreenCommand.Execute(null));
            RightClick(window);
            Assert.False(menu.IsOpen);
        });

    private static void RightClick(CameraWindow window)
    {
        window.Settle();
        Point point = new(60, 60);
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    /// <summary>Chooses <paramref name="item"/> of the open menu as a click does (the click event of the item), then the menu closes.</summary>
    private static void Click(CameraWindow window, MenuItem item)
    {
        Run(window, () =>
        {
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            window.ContextMenu!.Close();
        });
    }
}
