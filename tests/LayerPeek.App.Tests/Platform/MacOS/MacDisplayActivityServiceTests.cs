using System.Runtime.Versioning;

using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// T-187: the real registrations of <see cref="MacDisplayActivityService"/> and what its blocks do with a notification.
/// The blocks run on the main queue, which no thread of the test process pumps, so the notifications are fed through
/// <see cref="MacDisplayActivityService.Report"/> (what every block calls); the screen is never locked here and no
/// distributed notification is posted (it would reach every app of the session). Real lock, display sleep: T-127.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacDisplayActivityServiceTests
{
    [Fact]
    public void StartObservesTheLockAndDisposeRemovesEveryObserver()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Foundation exists only on macOS.");
        MacDisplayActivityService service = new();

        service.Start();
        int observed = service.ObserverCount;
        service.Dispose();

        // The lock notifications (Foundation); the display ones too when AppKit is loaded (not in this process).
        Assert.True(observed is 2 or 4, $"observers: {observed}");
        Assert.Equal(0, service.ObserverCount);
    }

    [Fact]
    public void NotificationsChangeWhetherTheScreenIsVisible()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Foundation exists only on macOS.");
        using MacDisplayActivityService service = new();
        service.Start();
        int changes = 0;
        service.ScreenVisibilityChanged += (_, _) => changes++;
        // From a known state whatever the machine is doing (the state was read at start).
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Unlocked);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.DisplaysAwake);
        changes = 0;

        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Locked);
        Assert.False(service.IsScreenVisible);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.DisplaysAsleep);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.DisplaysAwake);
        Assert.False(service.IsScreenVisible);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Unlocked);
        Assert.True(service.IsScreenVisible);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.DisplaysAsleep);
        Assert.False(service.IsScreenVisible);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.DisplaysAwake);
        Assert.True(service.IsScreenVisible);

        Assert.Equal(4, changes);
    }

    [Fact]
    public void OnlyStartedAndNotDisposedInstancesFollowNotifications()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Foundation exists only on macOS.");
        using MacDisplayActivityService notStarted = new();
        MacDisplayActivityService disposed = new();
        disposed.Start();
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Unlocked);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.DisplaysAwake);
        disposed.Dispose();
        using MacDisplayActivityService listening = new();
        listening.Start();
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Unlocked);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.DisplaysAwake);

        try
        {
            MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Locked);

            Assert.False(listening.IsScreenVisible);
            Assert.True(notStarted.IsScreenVisible);
            Assert.True(disposed.IsScreenVisible);
        }
        finally
        {
            // Other tests may run beside this one: leave the shared report unlocked.
            MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Unlocked);
        }
    }

    [Fact]
    public void StartAfterDisposeListensToNothing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Foundation exists only on macOS.");
        MacDisplayActivityService service = new();
        service.Dispose();

        service.Start();
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Locked);
        MacDisplayActivityService.Report(MacDisplayActivityService.Notice.Unlocked);

        Assert.Equal(0, service.ObserverCount);
        Assert.True(service.IsScreenVisible);
    }

    [Fact]
    public void StartReadsWhetherTheScreenIsLockedOrTheDisplaySleeps()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "CoreGraphics exists only on macOS.");
        using MacDisplayActivityService service = new();

        service.Start();

        // A camera window opened while the screen is locked (T-151) must not stream until it is unlocked.
        bool hidden = MacDisplayActivityService.QueryScreenLocked() == true || MacDisplayActivityService.QueryMainDisplayAsleep() == true;
        Assert.Equal(!hidden, service.IsScreenVisible);
    }

    [Fact]
    public void CurrentStateIsReadWithoutThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "CoreGraphics exists only on macOS.");

        // The values depend on the machine (locked, display asleep, an SSH session without the window server: null).
        _ = MacDisplayActivityService.QueryScreenLocked();
        Assert.NotNull(MacDisplayActivityService.QueryMainDisplayAsleep());
    }

    [Theory]
    [InlineData(0u, true)]
    [InlineData((uint)MacWindowOcclusion.OcclusionStateVisible, false)]
    [InlineData((uint)MacWindowOcclusion.OcclusionStateVisible | 1u, false)]
    [InlineData(1u, true)]
    public void OcclusionStateWithoutTheVisibleFlagIsOccluded(uint state, bool occluded) =>
        Assert.Equal(occluded, MacWindowOcclusion.IsOccluded(state));
}
