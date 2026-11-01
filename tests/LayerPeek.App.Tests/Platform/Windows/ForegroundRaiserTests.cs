using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Windows;

using Microsoft.Extensions.Logging.Abstractions;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// Bringing a window to the front past the foreground lock of Windows (T-119), on a fake window manager: the input of
/// the window's thread is attached to the foreground thread only for the call and always detached again, a refused
/// foreground still raises the window in the z-order, and nothing that changes the topmost state is used.
/// </summary>
public sealed class ForegroundRaiserTests
{
    private const nint Window = 100;
    private const nint OtherApp = 200;
    private const uint UiThread = 1;
    private const uint OtherThread = 2;

    private readonly FakeForegroundWindowApi _api = new();

    public ForegroundRaiserTests()
    {
        _api.Threads[Window] = UiThread;
        _api.Threads[OtherApp] = OtherThread;
        _api.Foreground = OtherApp;
    }

    [Fact]
    public void AnotherAppInFrontIsPassedWithTheAttachedInputAndDetachedAgain()
    {
        ForegroundRaiseResult result = ForegroundRaiser.Raise(_api, Window);

        Assert.Equal(ForegroundRaiseResult.Foreground, result);
        Assert.Equal(["foreground?", "attach 2->1", "top 100", "set foreground 100", "detach 2->1", "foreground?"], _api.Calls);
        Assert.False(_api.IsAttached);
    }

    [Fact]
    public void WindowInTheForegroundAlreadyIsLeftAlone()
    {
        _api.Foreground = Window;

        Assert.Equal(ForegroundRaiseResult.AlreadyForeground, ForegroundRaiser.Raise(_api, Window));
        Assert.Equal(["foreground?"], _api.Calls);
    }

    [Fact]
    public void AnotherWindowOfTheUiThreadIsNotAttachedTo()
    {
        const nint Settings = 300;
        _api.Threads[Settings] = UiThread;
        _api.Foreground = Settings;
        _api.Grants = static _ => true;

        Assert.Equal(ForegroundRaiseResult.Foreground, ForegroundRaiser.Raise(_api, Window));
        Assert.DoesNotContain(_api.Calls, static call => call.StartsWith("attach", StringComparison.Ordinal));
    }

    [Fact]
    public void NoForegroundWindowIsNotAttachedTo()
    {
        _api.Foreground = 0;
        _api.Grants = static _ => true;

        Assert.Equal(ForegroundRaiseResult.Foreground, ForegroundRaiser.Raise(_api, Window));
        Assert.Equal(["foreground?", "top 100", "set foreground 100", "foreground?"], _api.Calls);
    }

    [Fact]
    public void HungForegroundAppIsNotAttachedTo()
    {
        // Attaching to a thread that handles no messages could block the UI thread as well.
        _api.Hung.Add(OtherApp);

        Assert.Equal(ForegroundRaiseResult.RaisedOnly, ForegroundRaiser.Raise(_api, Window));
        Assert.Equal(["foreground?", "top 100", "set foreground 100", "foreground?", "raise 100"], _api.Calls);
    }

    [Fact]
    public void RefusedForegroundStillRaisesTheWindowInTheOrder()
    {
        // 2026-10-31: Windows denied attaching to the foreground thread of VMware Workstation.
        _api.Grants = static _ => false;

        Assert.Equal(ForegroundRaiseResult.RaisedOnly, ForegroundRaiser.Raise(_api, Window));
        Assert.Equal(["foreground?", "attach 2->1", "top 100", "set foreground 100", "detach 2->1", "foreground?", "raise 100"], _api.Calls);
        Assert.Equal(OtherApp, _api.Foreground);
    }

    [Fact]
    public void DeniedAttachIsNotDetachedAndStillRaisesTheWindow()
    {
        _api.AttachAllowed = false;

        Assert.Equal(ForegroundRaiseResult.RaisedOnly, ForegroundRaiser.Raise(_api, Window));
        Assert.Equal(["foreground?", "attach 2->1", "top 100", "set foreground 100", "foreground?", "raise 100"], _api.Calls);
        Assert.False(_api.IsAttached);
    }

    [Fact]
    public void WindowWithoutAThreadIsNotAttached()
    {
        // GetWindowThreadProcessId returns 0 for a handle that is no longer valid; thread 0 must never be attached.
        _api.Threads.Remove(Window);

        Assert.Equal(ForegroundRaiseResult.RaisedOnly, ForegroundRaiser.Raise(_api, Window));
        Assert.DoesNotContain(_api.Calls, static call => call.StartsWith("attach", StringComparison.Ordinal));
    }

    [Fact]
    public void FailingCallStillDetachesTheInput()
    {
        _api.SetForegroundFailure = new EntryPointNotFoundException("SetForegroundWindow");

        Assert.Throws<EntryPointNotFoundException>(() => ForegroundRaiser.Raise(_api, Window));
        Assert.Equal("detach 2->1", _api.Calls[^1]);
        Assert.False(_api.IsAttached);
    }

    [Fact]
    public void WindowsGetsTheNativeActivatorAndOtherOperatingSystemsAvalonias()
    {
        IWindowActivator activator = WindowsWindowActivator.ForCurrentPlatform(static () => NullLogger<WindowsWindowActivator>.Instance);

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsWindowActivator>(activator);
        }
        else
        {
            // macOS keeps Window.Activate with the app activation of T-019 around it.
            Assert.Same(AvaloniaWindowActivator.Instance, activator);
        }
    }
}
