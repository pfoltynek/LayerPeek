using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

using LayerPeek.App.Settings;
using LayerPeek.App.UiTests.Diagnostics;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.UiTests.Settings;
using LayerPeek.App.Windows;
using LayerPeek.App.Tests.Tray;

using static LayerPeek.App.Tests.Settings.SettingsTestData;

namespace LayerPeek.App.UiTests.Windows;

/// <summary>
/// The settings and diagnostics windows can be maximized and remember the size and maximized state the user left them in
/// (T-094); a size shrunk only to fit the screen is not remembered; at the minimum size their content scrolls and stays
/// reachable. The headless screen is 1920 × 1280 at 100 %; it does not resize a maximized window itself.
/// </summary>
public sealed class AuxiliaryWindowSizeUiTests(HeadlessSession session)
{
    private static readonly Size UserSize = new(1000, 640);

    // ---- Maximize and restore ----

    [Fact]
    public Task SettingsWindowCanBeMaximizedAndRestored() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenSettingsAsync(scope, new MemoryAuxiliaryWindowStateStore());

            AssertMaximizesAndRestores(ui.Window);
        });

    [Fact]
    public Task DiagnosticsWindowCanBeMaximizedAndRestored() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = OpenDiagnostics(scope, new MemoryAuxiliaryWindowStateStore());

            AssertMaximizesAndRestores(ui.Window);
            return Task.CompletedTask;
        });

    // ---- Remembered between openings ----

    [Fact]
    public Task SettingsWindowReopensWithTheSizeAndStateLeftEvenAfterCancel() =>
        session.RunAsync(static async scope =>
        {
            MemoryAuxiliaryWindowStateStore store = new();
            SettingsWindowHarness first = await OpenSettingsAsync(scope, store);
            ResizeByUser(first.Window, UserSize);
            MaximizeLikeWindows(first.Window, new Size(1920, 1248));

            // User decision Q4: "Cancel" discards the settings, but the window state is remembered.
            first.ViewModel.CancelCommand.Execute(null);
            first.Window.Settle();
            Assert.False(first.IsOpen);
            Assert.Equal(new AuxiliaryWindowState(UserSize, Maximized: true), store.Get(AuxiliaryWindowKeys.Settings));
            Assert.Same(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Diagnostics));

            SettingsWindowHarness second = await OpenSettingsAsync(scope, store);
            Assert.Equal(WindowState.Maximized, second.Window.WindowState);
            second.Window.WindowState = WindowState.Normal;
            second.Window.Settle();
            Assert.Equal(UserSize, second.Window.ClientSize);
            Assert.Equal(WindowStartupLocation.CenterScreen, second.Window.WindowStartupLocation);

            // Restored and closed with the title bar button: no longer maximized next time.
            second.Window.Close();
            Assert.Equal(new AuxiliaryWindowState(UserSize, Maximized: false), store.Get(AuxiliaryWindowKeys.Settings));
        });

    [Fact]
    public Task DiagnosticsWindowReopensWithItsOwnSizeAndState() =>
        session.RunAsync(static scope =>
        {
            MemoryAuxiliaryWindowStateStore store = new();
            store.Set(AuxiliaryWindowKeys.Settings, new AuxiliaryWindowState(new Size(700, 450), Maximized: true));
            DiagnosticsWindowHarness first = OpenDiagnostics(scope, store);
            Assert.Equal(AuxiliaryWindowPlacement.DefaultClientSize, first.Window.ClientSize);
            Assert.Equal(WindowState.Normal, first.Window.WindowState);
            ResizeByUser(first.Window, UserSize);
            first.Window.Close();

            DiagnosticsWindowHarness second = OpenDiagnostics(scope, store);

            Assert.Equal(UserSize, second.Window.ClientSize);
            Assert.Equal(WindowState.Normal, second.Window.WindowState);
            Assert.Equal(new AuxiliaryWindowState(UserSize, Maximized: false), store.Get(AuxiliaryWindowKeys.Diagnostics));
            Assert.Equal(new AuxiliaryWindowState(new Size(700, 450), Maximized: true), store.Get(AuxiliaryWindowKeys.Settings));
            return Task.CompletedTask;
        });

    [Fact]
    public Task WithoutARememberedStateTheWindowsOpenAtTheDefaultSizeAndRememberNothing() =>
        session.RunAsync(static async scope =>
        {
            MemoryAuxiliaryWindowStateStore store = new();
            SettingsWindowHarness settings = await OpenSettingsAsync(scope, store);
            DiagnosticsWindowHarness diagnostics = OpenDiagnostics(scope, store);

            Assert.Equal(AuxiliaryWindowPlacement.DefaultClientSize, settings.Window.ClientSize);
            Assert.Equal(AuxiliaryWindowPlacement.DefaultClientSize, diagnostics.Window.ClientSize);
            settings.Window.Close();
            diagnostics.Window.Close();

            Assert.Equal(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Settings));
            Assert.Equal(AuxiliaryWindowState.Default, store.Get(AuxiliaryWindowKeys.Diagnostics));
        });

    [Fact]
    public Task RememberedSizeLargerThanTheScreenIsFittedButNotForgotten() =>
        session.RunAsync(static scope =>
        {
            // Remembered on a larger monitor: fitted to the 1920 × 1280 work area (with the assumed 32 DIP title bar).
            AuxiliaryWindowState remembered = new(new Size(2400, 1400), Maximized: false);
            MemoryAuxiliaryWindowStateStore store = new();
            store.Set(AuxiliaryWindowKeys.Diagnostics, remembered);

            DiagnosticsWindowHarness ui = OpenDiagnostics(scope, store);
            Assert.Equal(new Size(1920, 1248), ui.Window.ClientSize);
            ui.Window.Close();

            // Shrunk only to fit: the larger preference stays for the next, larger screen.
            Assert.Equal(remembered, store.Get(AuxiliaryWindowKeys.Diagnostics));
            return Task.CompletedTask;
        });

    [Fact]
    public Task RememberedSizeBelowTheMinimumOpensAtTheMinimum() =>
        session.RunAsync(static async scope =>
        {
            MemoryAuxiliaryWindowStateStore store = new();
            store.Set(AuxiliaryWindowKeys.Settings, new AuxiliaryWindowState(new Size(300, 200), Maximized: false));

            SettingsWindowHarness ui = await OpenSettingsAsync(scope, store);

            Assert.Equal(AuxiliaryWindowPlacement.MinClientSize, ui.Window.ClientSize);
        });

    // ---- The minimum size scrolls, and everything stays reachable ----

    [Fact]
    public Task SettingsWindowAtTheMinimumScrollsAndKeepsEverythingReachable() =>
        session.RunAsync(static async scope =>
        {
            SettingsWindowHarness ui = await OpenSettingsAsync(scope, new MemoryAuxiliaryWindowStateStore());
            ResizeByUser(ui.Window, AuxiliaryWindowPlacement.MinClientSize);
            Assert.Equal(AuxiliaryWindowPlacement.MinClientSize, ui.Window.ClientSize);

            (string Name, int Tab, int PrinterTab)[] views =
            [
                ("Printer", SettingsViewModel.PrintersTab, SettingsViewModel.ConnectionPrinterTab),
                ("Camera", SettingsViewModel.PrintersTab, SettingsViewModel.CameraPrinterTab),
                ("Camera window", SettingsViewModel.PrintersTab, SettingsViewModel.CameraWindowPrinterTab),
                ("Overlay", SettingsViewModel.PrintersTab, SettingsViewModel.OverlayPrinterTab),
                ("General", SettingsViewModel.GeneralTab, SettingsViewModel.ConnectionPrinterTab),
                ("Startup and updates", SettingsViewModel.StartupUpdatesTab, SettingsViewModel.ConnectionPrinterTab),
            ];
            List<string> scrolled = [];
            foreach ((string name, int tab, int printerTab) in views)
            {
                ui.ViewModel.SelectedTabIndex = tab;
                ui.ViewModel.SelectedPrinterTabIndex = printerTab;
                ui.Window.Settle();
                if (ScrollsToTheEnd(ui.Window, name))
                {
                    scrolled.Add(name);
                }
            }

            // The printer form is the tallest view: it needs the scroll bar at the minimum size.
            Assert.Contains("Printer", scrolled);

            // The buttons below the tabs stay inside the window.
            AssertInside(ui.Window, ui.Find<Button>("SettingsSave"));
            AssertInside(ui.Window, ui.Find<Button>("SettingsCancel"));
        });

    [Fact]
    public Task DiagnosticsWindowAtTheMinimumScrollsAndKeepsEverythingReachable() =>
        session.RunAsync(static scope =>
        {
            DiagnosticsWindowHarness ui = OpenDiagnostics(scope, new MemoryAuxiliaryWindowStateStore());
            ResizeByUser(ui.Window, AuxiliaryWindowPlacement.MinClientSize);

            Assert.True(ScrollsToTheEnd(ui.Window, "diagnostics"), "The diagnostics need the scroll bar at the minimum size.");
            AssertInside(ui.Window, ui.Find<Button>("DiagnosticsClose"));
            return Task.CompletedTask;
        });

    // ---- Helpers ----

    private static Task<SettingsWindowHarness> OpenSettingsAsync(UiTestScope scope, IAuxiliaryWindowStateStore store) =>
        SettingsWindowHarness.OpenAsync(scope, SettingsWindowHarness.StoreWith(LinkPrinter("a", "Alpha")), windowStates: store);

    private static DiagnosticsWindowHarness OpenDiagnostics(UiTestScope scope, IAuxiliaryWindowStateStore store) =>
        DiagnosticsWindowHarness.OpenRemembering(scope, store, [TestPrinters.Definition("a", "Alpha")]);

    /// <summary>
    /// Resizes the window and reports it the way the platform reports the user dragging the border (a headless window
    /// reports its own resizes as requested by the app).
    /// </summary>
    private static void ResizeByUser(Window window, Size clientSize)
    {
        window.Width = clientSize.Width;
        window.Height = clientSize.Height;
        window.Settle();
        Assert.Equal(clientSize, window.ClientSize);
        Sizing(window).OnResized(clientSize, WindowResizeReason.User);
    }

    /// <summary>
    /// Maximizes the window the way Windows reports it: the maximized size first (without a reason, while the window is
    /// still normal), then the new state.
    /// </summary>
    private static void MaximizeLikeWindows(Window window, Size maximizedClient)
    {
        Sizing(window).OnResized(maximizedClient, WindowResizeReason.Unspecified);
        window.WindowState = WindowState.Maximized;
        window.Settle();
        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    private static WorkAreaFitter Sizing(Window window) => window switch
    {
        SettingsWindow settings => settings.Sizing,
        DiagnosticsWindow diagnostics => diagnostics.Sizing,
        _ => throw new ArgumentException($"{window.GetType().Name} remembers no size.", nameof(window)),
    };

    private static void AssertMaximizesAndRestores(Window window)
    {
        Assert.True(window.CanMaximize);
        Assert.False(window.CanMinimize);
        Assert.True(window.CanResize);
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.WindowState = WindowState.Maximized;
        window.Settle();
        Assert.Equal(WindowState.Maximized, window.WindowState);

        window.WindowState = WindowState.Normal;
        window.Settle();
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal(AuxiliaryWindowPlacement.DefaultClientSize, window.ClientSize);
    }

    /// <summary>
    /// Every visible content scroll viewer whose content does not fit can be scrolled to the end of its content (which so
    /// stays reachable; nothing is wider than the window); <see langword="true"/> when at least one of them had to scroll.
    /// </summary>
    private static bool ScrollsToTheEnd(Window window, string context)
    {
        ScrollViewer[] viewers = [.. window.GetVisualDescendants().OfType<ScrollViewer>().Where(static viewer => viewer.TemplatedParent is null && viewer.IsEffectivelyVisible)];
        Assert.True(viewers.Length > 0, $"{context}: no scroll viewer of the content is shown.");
        bool scrolled = false;
        foreach (ScrollViewer viewer in viewers)
        {
            Assert.True(viewer.Extent.Width <= viewer.Viewport.Width + 0.01, $"{context}: the content is {viewer.Extent.Width:0.#} DIP wide, more than the {viewer.Viewport.Width:0.#} visible.");
            double hidden = viewer.Extent.Height - viewer.Viewport.Height;
            if (hidden <= 0.01)
            {
                continue;
            }

            scrolled = true;
            Assert.True(
                viewer.VerticalScrollBarVisibility is Avalonia.Controls.Primitives.ScrollBarVisibility.Auto or Avalonia.Controls.Primitives.ScrollBarVisibility.Visible,
                $"{context}: the scroll bar is {viewer.VerticalScrollBarVisibility}.");
            viewer.ScrollToEnd();
            window.Settle();
            Assert.Equal(hidden, viewer.Offset.Y, 0.5);
            viewer.ScrollToHome();
            window.Settle();
        }

        return scrolled;
    }

    private static void AssertInside(Window window, Control control)
    {
        Point bottomRight = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)
            ?? throw new InvalidOperationException("Not in the window.");
        Assert.True(control.IsEffectivelyVisible, $"{control.Name} is not visible.");
        Assert.True(bottomRight.X <= window.ClientSize.Width + 0.5 && bottomRight.Y <= window.ClientSize.Height + 0.5, $"{AutomationIdOf(control)} ends at {bottomRight}, outside {window.ClientSize}.");
    }

    private static string? AutomationIdOf(Control control) => Avalonia.Automation.AutomationProperties.GetAutomationId(control);
}
