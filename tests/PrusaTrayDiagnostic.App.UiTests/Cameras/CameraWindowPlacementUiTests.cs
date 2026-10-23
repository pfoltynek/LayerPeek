using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Tests.Cameras;
using PrusaTrayDiagnostic.App.Tray;
using PrusaTrayDiagnostic.App.UiTests.Infrastructure;
using PrusaTrayDiagnostic.App.Windows;
using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Core.Settings;

using static PrusaTrayDiagnostic.App.Tests.Cameras.FakeScreenCatalog;

namespace PrusaTrayDiagnostic.App.UiTests.Cameras;

/// <summary>
/// The real camera window opened with window defaults (T-035) on three fake monitors: the primary 2560 × 1440 at 100 %
/// (work area 2560 × 1392), "LG HDR 4K" 1920 × 1080 at 150 % on its left (at -1920, 200) and a rotated 1440 × 2560 on its right.
/// </summary>
public sealed class CameraWindowPlacementUiTests(HeadlessSession session)
{
    private static readonly CameraMonitorId Lg = new("LG HDR 4K", 1920, 1080);
    private static readonly CameraMonitorId Rotated = new("DELL U2719D", 1440, 2560);

    private static CameraViewModel ViewModel(CameraWindow window) => (CameraViewModel)window.DataContext!;

    /// <summary>Where the window should be: <paramref name="layout"/> placed on <paramref name="screen"/> with the chrome assumed before showing.</summary>
    private static (PixelPoint Position, Size Size) Expected(CameraWindowLayout layout, CameraScreen screen, CameraWindowMode mode)
    {
        CameraWindowPlacement placement = CameraWindowPlanner.Place(layout, screen, CameraWindowPlanner.AssumedChrome(mode));
        return (new PixelPoint(placement.X, placement.Y), new Size(placement.Width, placement.Height));
    }

    private static CameraScreen ScreenAt(FakeScreenCatalog screens, int x) => screens.GetScreens().Single(screen => screen.Bounds.X == x);

    private static void AssertPlaced(CameraWindow window, (PixelPoint Position, Size Size) expected)
    {
        Assert.Equal(expected.Position, window.Position);
        Assert.Equal(expected.Size, window.ClientSize);
    }

    [Fact]
    public Task NormalWindowOpensInItsCornerOfTheChosenMonitor() =>
        session.RunAsync(async scope =>
        {
            FakeScreenCatalog screens = ThreeMonitors();
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with
            {
                StartMode = CameraWindowStartMode.Normal,
                Monitor = Lg,
                Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
            };
            await using CameraHarness harness = new(screens: screens, window: defaults);

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.True(ViewModel(window).IsNormal);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
            // 150 %: the visible window is 24 px (16 DIP) from the work area (-1920, 200); the position is moved out by the invisible border.
            int invisibleLeft = (int)Math.Round(CameraWindowPlanner.AssumedNormalChrome.InvisibleBorders.Left * 1.5, MidpointRounding.AwayFromZero);
            Assert.Equal(new PixelPoint(-1896 - invisibleLeft, 224), window.Position);
            AssertPlaced(window, Expected(defaults.Normal, ScreenAt(screens, -1920), CameraWindowMode.Normal));
        });

    [Fact]
    public Task NormalWindowIsPlacedWithTheFrameOfTheChosenMonitorAndNotCorrected() =>
        session.RunAsync(async scope =>
        {
            // T-071 Q3: Windows 11 at 150 % (11 px borders of which 2 px visible, 45 px title bar), not the 100 % chrome scaled up.
            WindowChrome at150 = WindowChrome.FromPixels(new PixelFrame(11, 45, 11, 11, VisibleBorder: 2), 1.5);
            List<double> predicted = [];
            int measured = 0;
            FakeScreenCatalog screens = ThreeMonitors();
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with
            {
                StartMode = CameraWindowStartMode.Normal,
                Monitor = Lg,
                Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.BottomRight),
            };
            await using CameraHarness harness = new(
                screens: screens,
                window: defaults,
                measureChrome: _ =>
                {
                    measured++;
                    return at150;
                },
                predictChrome: (_, scaling) =>
                {
                    predicted.Add(scaling);
                    return at150;
                });

            CameraWindow window = harness.Open();
            window.Settle();

            // LG work area (-1920, 200) 1920 × 1032: right edge 0 − 24 px margin − 2 px visible border − 960 px client − 2 px −
            // 9 px invisible border; bottom 1232 − 24 − 2 − 540 − 45 (the top border is all visible). Measured once, the same: no correction.
            Assert.Equal([1.5], predicted);
            Assert.Equal(1, measured);
            Assert.Equal(new PixelPoint(-24 - 2 - 960 - 2 - 9, 1232 - 24 - 2 - 540 - 45), window.Position);
            Assert.Equal(new Size(640, 360), window.ClientSize);
        });

    [Fact]
    public Task DefaultsWithoutSettingsCentreTheNormalWindowOnThePrimaryMonitor() =>
        session.RunAsync(async scope =>
        {
            FakeScreenCatalog screens = ThreeMonitors();
            await using CameraHarness harness = new(screens: screens);

            CameraWindow window = harness.Open();
            window.Settle();

            AssertPlaced(window, Expected(CameraWindowDefaults.DefaultNormal, ScreenAt(screens, 0), CameraWindowMode.Normal));
            Assert.Equal(new Size(960, 540), window.ClientSize);
        });

    [Fact]
    public Task MiniStartModeOpensAsTheBorderlessPreviewInItsCorner() =>
        session.RunAsync(async scope =>
        {
            FakeScreenCatalog screens = ThreeMonitors();
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };
            await using CameraHarness harness = new(screens: screens, window: defaults);

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.True(ViewModel(window).IsMini);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.True(window.Topmost);
            // Bottom right of the primary work area (2560 × 1392) with 16 px, no frame.
            Assert.Equal(new PixelPoint(2224, 1196), window.Position);
            Assert.Equal(new Size(320, 180), window.ClientSize);
        });

    [Fact]
    public Task FullScreenStartModeGoesFullScreenOnTheChosenMonitor() =>
        session.RunAsync(async scope =>
        {
            FakeScreenCatalog screens = ThreeMonitors();
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.FullScreen, Monitor = Rotated };
            await using CameraHarness harness = new(screens: screens, window: defaults);

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.True(ViewModel(window).IsFullScreen);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            // The normal window was placed on the chosen monitor first; Esc returns to it.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.Settle();
            Assert.Equal(WindowState.Normal, window.WindowState);
            AssertPlaced(window, Expected(defaults.Normal, ScreenAt(screens, 2560), CameraWindowMode.Normal));
        });

    [Fact]
    public Task RememberedPlacementWinsWhileRememberIsOn() =>
        session.RunAsync(async scope =>
        {
            MemoryCameraWindowStateStore states = new();
            states.Set("a", new CameraWindowState(CameraWindowMode.Normal, new CameraWindowPlacement(100, 120, 700, 400), null));
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { Monitor = Lg };
            await using CameraHarness harness = new(states, ThreeMonitors(), defaults);

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.Equal(new PixelPoint(100, 120), window.Position);
            Assert.Equal(new Size(700, 400), window.ClientSize);
        });

    [Fact]
    public Task RememberOffAlwaysOpensWithTheDefaultsButKeepsTheLastMode() =>
        session.RunAsync(async scope =>
        {
            FakeScreenCatalog screens = ThreeMonitors();
            MemoryCameraWindowStateStore states = new();
            states.Set("a", new CameraWindowState(CameraWindowMode.Mini, new CameraWindowPlacement(100, 120, 700, 400), new CameraWindowPlacement(300, 200, 400, 225)));
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with { RememberLastPlacement = false };
            await using CameraHarness harness = new(states, screens, defaults);

            CameraWindow window = harness.Open();
            window.Settle();

            // T-035 Q2: the remembered mode, at the default position and size.
            Assert.True(ViewModel(window).IsMini);
            AssertPlaced(window, Expected(defaults.Mini, ScreenAt(screens, 0), CameraWindowMode.Mini));
        });

    [Fact]
    public Task UnreachableRememberedPlacementFallsBackToTheDefaults() =>
        session.RunAsync(async scope =>
        {
            FakeScreenCatalog screens = ThreeMonitors();
            MemoryCameraWindowStateStore states = new();
            // Remembered on a monitor that is gone.
            states.Set("a", new CameraWindowState(CameraWindowMode.Normal, new CameraWindowPlacement(9000, 300, 700, 400), null));
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with
            {
                Normal = new CameraWindowLayout(new CameraWindowSize(800, 450), CameraWindowCorner.BottomLeft),
            };
            await using CameraHarness harness = new(states, screens, defaults);

            CameraWindow window = harness.Open();
            window.Settle();

            AssertPlaced(window, Expected(defaults.Normal, ScreenAt(screens, 0), CameraWindowMode.Normal));
        });

    [Fact]
    public Task MiniPreviewWithoutARememberedPlacementGoesToTheCornerOfTheCurrentMonitor() =>
        session.RunAsync(async scope =>
        {
            // T-035 Q5: the defaults choose the primary monitor, but the user moved the window to the LG one.
            FakeScreenCatalog screens = ThreeMonitors();
            await using CameraHarness harness = new(screens: screens);
            CameraWindow window = harness.Open();
            window.Settle();
            window.Position = new PixelPoint(-1700, 300);
            window.Width = 600;
            window.Height = 300;
            window.Settle();

            ViewModel(window).ShowMiniCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // 150 %: 480 × 270 px with 24 px from the bottom right of the work area (-1920, 200, 1920, 1032).
            Assert.Equal(new PixelPoint(-504, 938), window.Position);
            Assert.Equal(new Size(320, 180), window.ClientSize);

            // Back to the normal window: where it was (remembered when it left).
            ViewModel(window).ShowNormalCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new PixelPoint(-1700, 300), window.Position);
            Assert.Equal(new Size(600, 300), window.ClientSize);
        });

    [Fact]
    public Task NormalWindowWithoutARememberedPlacementComesBackOnTheCurrentMonitor() =>
        session.RunAsync(async scope =>
        {
            FakeScreenCatalog screens = ThreeMonitors();
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with
            {
                StartMode = CameraWindowStartMode.Mini,
                Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopRight),
            };
            await using CameraHarness harness = new(screens: screens, window: defaults);
            CameraWindow window = harness.Open();
            window.Settle();
            window.Position = new PixelPoint(2700, 1000);
            window.Settle();

            ViewModel(window).ShowNormalCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            AssertPlaced(window, Expected(defaults.Normal, ScreenAt(screens, 2560), CameraWindowMode.Normal));
        });

    [Fact]
    public Task SavedSettingsDoNotMoveTheOpenWindowAndApplyNextTime() =>
        session.RunAsync(async scope =>
        {
            FakeScreenCatalog screens = ThreeMonitors();
            MemoryCameraWindowStateStore states = new();
            await using CameraHarness harness = new(states, screens);
            CameraWindow window = harness.Open();
            window.Settle();
            PixelPoint before = window.Position;
            Size size = window.ClientSize;

            // What saving the settings does (SettingsViewModel + monitoring): forget the state, then apply the settings.
            CameraWindowDefaults changed = CameraWindowDefaults.Default with
            {
                Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
            };
            states.Remove("a");
            ((IMonitoringObserver)harness.Printers).ApplySettings(new AppSettings { Printers = [CameraHarness.CameraPrinter("a", "Alpha", changed)] });
            Dispatcher.UIThread.RunJobs();
            window.Settle();

            Assert.Equal(before, window.Position);
            Assert.Equal(size, window.ClientSize);
            Assert.Same(changed, ViewModel(window).Defaults);

            window.Close();
            Assert.Empty(harness.Windows.OpenWindows);
            Assert.Same(CameraWindowState.Default, states.Get("a"));

            CameraWindow reopened = harness.Open();
            reopened.Settle();
            AssertPlaced(reopened, Expected(changed.Normal, ScreenAt(screens, 0), CameraWindowMode.Normal));
        });

    [Fact]
    public Task WithoutScreensTheWindowKeepsTheDefaultSizeAndCentresItself() =>
        session.RunAsync(async scope =>
        {
            CameraWindowDefaults defaults = CameraWindowDefaults.Default with
            {
                Normal = new CameraWindowLayout(new CameraWindowSize(640, 360), CameraWindowCorner.TopLeft),
            };
            await using CameraHarness harness = new(screens: new FakeScreenCatalog(), window: defaults);

            CameraWindow window = harness.Open();
            window.Settle();

            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            Assert.Equal(new Size(640, 360), window.ClientSize);
        });

    [Fact]
    public Task HeadlessScreenIsMappedByTheAvaloniaCatalog() =>
        session.RunAsync(async scope =>
        {
            await using CameraHarness harness = new();
            CameraWindow window = harness.Open();

            IReadOnlyList<CameraScreen> screens = new AvaloniaScreenCatalog(window).GetScreens();

            CameraScreen screen = Assert.Single(screens);
            Assert.False(screen.Bounds.IsEmpty);
            Assert.True(screen.Scaling > 0);
        });
}
