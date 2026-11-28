using System.Runtime.Versioning;

using LayerPeek.App.Platform.Windows;
using LayerPeek.App.Tests.Tray;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// T-186: the real registrations of <see cref="WindowsDisplayActivityService"/>. The service owns a thread with its own
/// message loop, so it works from any calling thread: its message-only window exists, Windows reports the console display
/// state to it at once, and disposing (from another thread) removes the window, the registrations and the thread. Two
/// services side by side each get their own notifications. Locking the session and switching the display off are not done
/// here (shared desktop; checked by hand in T-125). <see cref="WindowsWindowCloaking.IsCloaked"/> answers for any handle.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayActivityServiceTests
{
    private const string WindowsOnly = "The session and power notifications exist only on Windows.";

    [Fact]
    public async Task StartedServiceGetsTheDisplayStateAndDisposeStopsListening()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        WindowsDisplayActivityService service = new();
        try
        {
            service.Start();
            await service.Listening.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
            Assert.True(service.IsListening);
            await service.DisplayStateReported.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        }
        finally
        {
            service.Dispose();
        }

        Assert.False(service.IsListening);
        Assert.False(service.HasThread);
    }

    [Fact]
    public async Task TwoServicesEachGetTheirOwnNotifications()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using WindowsDisplayActivityService first = new();
        using WindowsDisplayActivityService second = new();

        first.Start();
        second.Start();

        await first.DisplayStateReported.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
        await second.DisplayStateReported.WaitAsync(TestTimeouts.Wait, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void DisposeWithoutStartDoesNothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        WindowsDisplayActivityService service = new();

        service.Dispose();
        service.Start();

        Assert.False(service.IsListening);
        Assert.False(service.HasThread);
    }

    [Fact]
    public void CurrentSessionLockStateCanBeQueried()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        // The value depends on the desktop (the test session is normally unlocked); only that Windows answers is checked.
        Assert.NotNull(WindowsDisplayActivityService.QuerySessionFlags());
    }

    [Fact]
    public void CurrentSessionTypeCanBeQueried()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        // T-252: console or Remote Desktop depends on how the tests run; only that Windows answers is checked.
        Assert.NotNull(WindowsDisplayActivityService.QueryRemoteSession());
    }

    [Fact]
    public void NoWindowIsNeverCloaked()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        Assert.False(WindowsWindowCloaking.IsCloaked(0));
    }
}
