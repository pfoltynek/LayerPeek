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
/// The keyboard focus of the camera window around a new activation (T-122). On Windows unpinning gives the window its
/// taskbar button back by hiding and showing it, and the activation gives the focus back to the element the window
/// remembers, without the focus ring. A: a window button clicked with the mouse leaves the focus on the image, not on itself,
/// so a new activation does not focus the button again and show the whole row without the pointer. B: a pin focused again by
/// an activation still takes Enter / Space as keys (not as a mouse click), so the focus stays on it. Headless windows have no
/// taskbar: the tests hide and show the window or activate it; the real window is checked by the E2E tests (T-120).
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
    [InlineData(false)]
    [InlineData(true)]
    public Task KeysOnThePinFocusedAgainByAnActivationKeepTheFocusOnIt(bool mini) =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = Harness();
            CameraWindow window = Open(harness, mini);
            ToggleButton pin = Pin(window);
            MouseAway(window);
            Assert.True(pin.Focus(NavigationMethod.Tab));
            window.Settle();
            bool pinned = window.Topmost;
            Press(window, Key.Space, PhysicalKey.Space, " ");
            Assert.NotEqual(pinned, window.Topmost);

            // What Windows does when unpinning gives the window its taskbar button back (and on Alt+Tab away and back): the
            // window loses the focus while hidden and its activation gives it back to the pin, without the focus ring.
            Run(window, window.Hide);
            window.FocusManager?.Focus(null);
            Run(window, window.Show);
            Assert.True(window.IsActive);
            Assert.Same(pin, window.FocusManager?.GetFocusedElement());
            Assert.DoesNotContain(":focus-visible", pin.Classes);

            // Enter (a click on key down) and Space (on key up) are still keys: the focus stays on the pin.
            (Key Key, PhysicalKey Physical, string Symbol)[] keys =
                [(Key.Enter, PhysicalKey.Enter, "\r"), (Key.Space, PhysicalKey.Space, " "), (Key.Enter, PhysicalKey.Enter, "\r")];
            foreach ((Key key, PhysicalKey physical, string symbol) in keys)
            {
                bool before = window.Topmost;
                Press(window, key, physical, symbol);
                Assert.NotEqual(before, window.Topmost);
                Assert.Same(pin, window.FocusManager?.GetFocusedElement());
            }
        });
}
