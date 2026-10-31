using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using LayerPeek.App.Cameras;
using LayerPeek.App.Tests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Cameras;

/// <summary>
/// The keyboard focus of the camera window across a change of its taskbar button (T-122). A: a window button clicked with
/// the mouse leaves the focus on the image, not on itself, so that a new activation (Alt+Tab back, or Windows hiding and
/// showing the window to give it its taskbar button back after unpinning) does not focus the button again and show the whole
/// row without the pointer. B: the taskbar button hidden or shown again while the window stays active (on Windows Avalonia
/// gives the window a hidden owner, and Windows may take the focus away) keeps the focus where it was, so a pin switched
/// with Space / Enter stays focused. Headless windows have no taskbar: the tests take the focus away while
/// <see cref="Window.ShowInTaskbar"/> changes, as Windows does; the real window is checked by the E2E tests (T-120).
/// </summary>
public sealed class CameraWindowFocusUiTests(HeadlessSession session)
{
    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    private static ToggleButton Pin(CameraWindow window) => window.Find<ToggleButton>("CameraTopmostButton");

    private static Border Surface(CameraWindow window) => window.GetVisualDescendants().OfType<Border>().Single(static b => b.Name == "Surface");

    private static CameraHarness Harness() =>
        new(screens: FakeScreenCatalog.ThreeMonitors(), window: CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Normal, TopmostNormal = false, TopmostMini = true });

    private static CameraWindow Open(CameraHarness harness, bool mini)
    {
        CameraWindow window = harness.Open();
        window.Settle();
        CameraHarness.SkipWindowButtonFade(window);
        if (mini)
        {
            Run(window, () => ViewModel(window).ShowMiniCommand.Execute(null));
            Assert.True(ViewModel(window).IsMini);
        }

        Assert.True(window.IsActive);
        return window;
    }

    private static void Run(CameraWindow window, Action action)
    {
        action();
        Dispatcher.UIThread.RunJobs();
        window.Settle();
    }

    private static void MouseAway(CameraWindow window) => Run(window, () => window.MouseMove(new Point(-10, -10)));

    /// <summary>What Windows does when the user switches back to the window (Alt+Tab, a click on the taskbar button).</summary>
    private static void Reactivate(CameraWindow window) => Run(window, window.Activate);

    /// <summary>The visible buttons of the row other than the pin: hidden (opacity 0) without the pointer and the focus.</summary>
    private static List<Control> OtherButtons(CameraWindow window) =>
        [.. window.Find<StackPanel>("CameraWindowButtons").Children.Where(static c => c.IsEffectivelyVisible && c is not ToggleButton)];

    private static void AssertRowHidden(CameraWindow window, string step)
    {
        StackPanel row = window.Find<StackPanel>("CameraWindowButtons");
        Assert.False(row.IsKeyboardFocusWithin, $"{step}: a window button has the focus ({window.FocusManager?.GetFocusedElement()}).");
        List<Control> others = OtherButtons(window);
        Assert.NotEmpty(others);
        Assert.All(others, button => Assert.Equal(0, button.Opacity));
    }

    private static void Press(CameraWindow window, Key key, PhysicalKey physical, string symbol) =>
        Run(window, () =>
        {
            window.KeyPress(key, RawInputModifiers.None, physical, symbol);
            window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PinClickedWithTheMouseIsNotFocusedAgainByTheNextActivation(bool mini) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness, mini);
            ToggleButton pin = Pin(window);
            bool pinned = window.Topmost;

            // Pin switched with the mouse (twice: on and off, both taskbar changes), then the pointer leaves the window.
            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.NotEqual(pinned, window.Topmost);
            MouseAway(window);
            AssertRowHidden(window, "after the first click");
            Reactivate(window);
            AssertRowHidden(window, "after the first click and a new activation");

            Run(window, () => window.MouseMove(Ui.CentreOf(pin, window)));
            Run(window, () => Ui.Click(window, Ui.CentreOf(pin, window)));
            Assert.Equal(pinned, window.Topmost);
            MouseAway(window);
            Reactivate(window);
            AssertRowHidden(window, "after the second click and a new activation");

            // Windows hides and shows the window when it gives it its taskbar button back (unpinning).
            Run(window, window.Hide);
            Run(window, window.Show);
            Assert.True(window.IsActive);
            AssertRowHidden(window, "after hiding and showing the window");

            // The image has the focus now; it is no tab stop, so Tab still goes to the first window button.
            Assert.Same(Surface(window), window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, PhysicalKey.Tab, "\t");
            Assert.Same(OtherButtons(window)[0], window.FocusManager?.GetFocusedElement());
        });

    [Fact]
    public Task ModeButtonClickedWithTheMouseIsNotFocusedAgainByTheNextActivation() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness, mini: false);
            CameraViewModel viewModel = ViewModel(window);

            // Full screen with the mouse, back with Esc: the full screen button is shown again.
            Run(window, () => Ui.Click(window, window.Find<Button>("CameraFullScreenButton")));
            Assert.True(viewModel.IsFullScreen);
            Press(window, Key.Escape, PhysicalKey.Escape, string.Empty);
            Assert.True(viewModel.IsNormal);
            MouseAway(window);
            Reactivate(window);

            AssertRowHidden(window, "after full screen and a new activation");
        });

    [Theory]
    [InlineData(false, Key.Space, false)]
    [InlineData(false, Key.Enter, false)]
    [InlineData(true, Key.Space, false)]
    [InlineData(true, Key.Enter, false)]
    [InlineData(false, Key.Space, true)]
    [InlineData(true, Key.Enter, true)]
    public Task PinSwitchedFromTheKeyboardKeepsTheFocusWhenTheTaskbarChangeTakesIt(bool mini, Key key, bool later) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness, mini);
            ToggleButton pin = Pin(window);
            MouseAway(window);
            Assert.True(pin.Focus(NavigationMethod.Tab));
            window.Settle();
            int taskbarChanges = 0;

            // Windows takes the focus away while the window keeps being active (WM_KILLFOCUS without WM_ACTIVATE), during
            // the change or right after it.
            void TakeFocus(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property != Window.ShowInTaskbarProperty)
                {
                    return;
                }

                taskbarChanges++;
                if (later)
                {
                    Dispatcher.UIThread.Post(() => window.FocusManager?.Focus(null), DispatcherPriority.Input);
                }
                else
                {
                    window.FocusManager?.Focus(null);
                }
            }

            window.PropertyChanged += TakeFocus;
            try
            {
                (PhysicalKey physical, string symbol) = key == Key.Space ? (PhysicalKey.Space, " ") : (PhysicalKey.Enter, "\r");
                for (int i = 0; i < 3; i++)
                {
                    bool before = window.Topmost;
                    Press(window, key, physical, symbol);
                    Assert.NotEqual(before, window.Topmost);
                    Assert.Equal(i + 1, taskbarChanges);
                    Assert.True(window.IsActive);
                    Assert.Same(pin, window.FocusManager?.GetFocusedElement());
                    // Still from the keyboard: the focus ring and the whole row stay shown.
                    Assert.True(pin.Classes.Contains(":focus-visible"), $"Press {i + 1}: the pin lost its keyboard focus look.");
                    Assert.All(OtherButtons(window), button => Assert.Equal(1, button.Opacity));
                }
            }
            finally
            {
                window.PropertyChanged -= TakeFocus;
            }
        });

    [Fact]
    public Task TaskbarChangeOfAWindowThatIsNotActiveTakesNoFocus() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness, mini: false);
            ToggleButton pin = Pin(window);
            Assert.True(pin.Focus(NavigationMethod.Tab));
            window.Settle();

            // Another window became active: the camera window keeps its scope's focus to itself (Windows gives it back on
            // the next activation) and grabs nothing when a saved option changes its taskbar button.
            Run(window, window.Hide);
            Assert.False(window.IsActive);
            window.FocusManager?.Focus(null);
            Run(window, () => window.Topmost = true);

            Assert.False(window.ShowInTaskbar);
            Assert.Null(window.FocusManager?.GetFocusedElement());
        });
}
