using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Cameras;
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Platform.Windows;
using LayerPeek.App.UiTests.Cameras;
using LayerPeek.App.UiTests.Infrastructure;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Windows;

/// <summary>
/// The shared path that brings an open auxiliary window to the front (T-119): "Bring camera to front", a second
/// Settings… / Diagnostics… / About of the tray menu and a second start of the app all activate the window through the
/// <see cref="IWindowActivator"/> (on Windows <see cref="WindowsWindowActivator"/>, a fake window manager here; the
/// headless windows have no native handle), after a minimized window was restored and without ever changing
/// <see cref="Window.Topmost"/>. Without an activator (macOS, other OSes) it is Avalonia's <see cref="Window.Activate"/>
/// as before, with the app activation of T-019 first.
/// </summary>
public sealed class WindowActivatorUiTests(HeadlessSession session)
{
    private const nint Handle = 4242;

    [Fact]
    public Task BringCameraToFrontActivatesTheRestoredWindowThroughTheActivator() =>
        session.RunAsync(static async scope =>
        {
            RecordingActivator activator = new();
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors(), activator: activator);
            Assert.True(cameras.Windows.Show("a"));
            CameraWindow window = scope.Track(cameras.Windows.OpenWindows["a"]);
            window.Settle();
            bool topmost = window.Topmost;
            List<bool> topmostChanges = TopmostChanges(window);
            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            activator.Calls.Clear();

            Assert.True(cameras.Windows.BringToFront("a"));
            Dispatcher.UIThread.RunJobs();

            // Restored before it was activated, and activated through the activator only.
            Assert.Equal([(window, WindowState.Normal, topmost)], activator.Calls);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(topmost, window.Topmost);
            Assert.Empty(topmostChanges);
        });

    [Fact]
    public Task PinnedMiniPreviewStaysOnTopWhenBroughtToTheFront() =>
        session.RunAsync(static async scope =>
        {
            RecordingActivator activator = new();
            CameraWindowDefaults mini = CameraWindowDefaults.Default with { StartMode = CameraWindowStartMode.Mini };
            await using CameraHarness cameras = new(screens: Tests.Cameras.FakeScreenCatalog.ThreeMonitors(), window: mini, activator: activator);
            Assert.True(cameras.Windows.Show("a"));
            CameraWindow window = scope.Track(cameras.Windows.OpenWindows["a"]);
            window.Settle();
            Assert.True(window.Topmost);
            List<bool> topmostChanges = TopmostChanges(window);
            activator.Calls.Clear();

            Assert.True(cameras.Windows.BringToFront("a"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([(window, WindowState.Normal, true)], activator.Calls);
            Assert.True(window.Topmost);
            Assert.Empty(topmostChanges);
        });

    [Fact]
    public Task SecondMenuChoiceActivatesTheOpenWindowThroughTheActivatorAfterTheApp() =>
        session.RunAsync(static scope =>
        {
            // Settings, Diagnostics and About share the slot path; the app activation of macOS (T-019) comes first.
            List<string> order = [];
            RecordingActivator activator = new(() => order.Add("window"));
            AuxiliaryWindowSlot slot = new();
            Window window = scope.Track(new Window());
            slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window, bringAppToFront: () => order.Add("app"), activator: activator), null));
            Dispatcher.UIThread.RunJobs();

            slot.ShowOrActivate(static () => throw new InvalidOperationException("The window is open."));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(["app", "window", "app", "window"], order);
            Assert.All(activator.Calls, call => Assert.Same(window, call.Window));
            return Task.CompletedTask;
        });

    [Fact]
    public Task SecondStartActivatesTheMostRecentWindowThroughTheActivatorBeforeTheShell() =>
        session.RunAsync(static scope =>
        {
            List<string> order = [];
            RecordingActivator activator = new(() => order.Add("activate"));
            AuxiliaryWindowTracker tracker = new(new OrderShell(order), static _ => Handle, activator: activator);
            AuxiliaryWindowSlot slot = new();
            Window window = scope.Track(new Window());
            slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window, tracker.Track), null));
            Dispatcher.UIThread.RunJobs();
            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            order.Clear();
            activator.Calls.Clear();

            new InstanceActivation(tracker.Recent, static () => Assert.Fail("A window is open.")).Activate();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal([(window, WindowState.Normal, false)], activator.Calls);
            // The taskbar button flashes (Windows, T-043) only after the window was asked to come to the front.
            Assert.Equal(["activate", "attention"], order);
            return Task.CompletedTask;
        });

    [Fact]
    public Task WithoutAnActivatorTheWindowIsActivatedByAvalonia() =>
        session.RunAsync(static scope =>
        {
            // macOS and other OSes: unchanged.
            AuxiliaryWindowSlot slot = new();
            Window window = scope.Track(new Window());
            int activated = 0;
            window.Activated += (_, _) => activated++;
            slot.ShowOrActivate(() => (new AvaloniaAuxiliaryWindow(window), null));
            Dispatcher.UIThread.RunJobs();
            activated = 0;

            slot.ShowOrActivate(static () => throw new InvalidOperationException("The window is open."));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, activated);
            return Task.CompletedTask;
        });

    [Fact]
    public Task WindowsActivatorRaisesTheNativeWindowInsteadOfAvaloniasActivate() =>
        session.RunAsync(static scope =>
        {
            FakeForegroundWindowApi api = ApiWithAnotherAppInFront();
            WindowsWindowActivator activator = new(NullLogger<WindowsWindowActivator>.Instance, api, static _ => Handle);
            Window window = scope.Track(new Window { ShowActivated = false });
            window.Show();
            Dispatcher.UIThread.RunJobs();
            int activated = 0;
            window.Activated += (_, _) => activated++;
            bool topmost = window.Topmost;

            activator.Activate(window);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains($"set foreground {Handle}", api.Calls);
            Assert.Equal(Handle, api.Foreground);
            Assert.Equal(0, activated);
            Assert.Equal(topmost, window.Topmost);
            return Task.CompletedTask;
        });

    [Fact]
    public Task WindowsActivatorFailureFallsBackToAvaloniaAndNeverThrows() =>
        session.RunAsync(static scope =>
        {
            FakeForegroundWindowApi api = ApiWithAnotherAppInFront();
            api.SetForegroundFailure = new EntryPointNotFoundException("SetForegroundWindow");
            WindowsWindowActivator activator = new(NullLogger<WindowsWindowActivator>.Instance, api, static _ => Handle);
            Window window = scope.Track(new Window { ShowActivated = false });
            window.Show();
            Dispatcher.UIThread.RunJobs();
            int activated = 0;
            window.Activated += (_, _) => activated++;

            activator.Activate(window);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, activated);
            Assert.False(api.IsAttached);
            return Task.CompletedTask;
        });

    [Fact]
    public Task WindowsActivatorWithoutANativeHandleUsesAvalonia() =>
        session.RunAsync(static scope =>
        {
            FakeForegroundWindowApi api = ApiWithAnotherAppInFront();
            WindowsWindowActivator activator = new(NullLogger<WindowsWindowActivator>.Instance, api, static _ => 0);
            Window window = scope.Track(new Window { ShowActivated = false });
            window.Show();
            Dispatcher.UIThread.RunJobs();
            int activated = 0;
            window.Activated += (_, _) => activated++;

            activator.Activate(window);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, activated);
            Assert.Empty(api.Calls);
            return Task.CompletedTask;
        });

    private static FakeForegroundWindowApi ApiWithAnotherAppInFront()
    {
        FakeForegroundWindowApi api = new() { Foreground = 7 };
        api.Threads[Handle] = 1;
        api.Threads[7] = 2;
        return api;
    }

    /// <summary>Every change of <see cref="Window.Topmost"/> from now on.</summary>
    private static List<bool> TopmostChanges(Window window)
    {
        List<bool> changes = [];
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.TopmostProperty)
            {
                changes.Add(e.GetNewValue<bool>());
            }
        };
        return changes;
    }

    /// <summary>Records each activation with the window's state and topmost at that moment; activates nothing.</summary>
    private sealed class RecordingActivator(Action? onActivate = null) : IWindowActivator
    {
        public List<(Window Window, WindowState State, bool Topmost)> Calls { get; } = [];

        public void Activate(Window window)
        {
            Calls.Add((window, window.WindowState, window.Topmost));
            onActivate?.Invoke();
        }
    }

    private sealed class OrderShell(List<string> order) : IWindowShell
    {
        public void Prepare(nint handle)
        {
        }

        public void Release(nint handle)
        {
        }

        public void RequestAttention(nint handle) => order.Add("attention");
    }
}
