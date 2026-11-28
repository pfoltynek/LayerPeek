using LayerPeek.App.Platform;

namespace LayerPeek.App.Tests.Platform;

/// <summary>T-186: what Windows reports (lock, console display state) combined into "the screen can be seen".</summary>
public sealed class DisplayActivityTrackerTests
{
    private readonly DisplayActivityTracker _tracker = new();
    private int _changes;

    public DisplayActivityTrackerTests() => _tracker.ScreenVisibilityChanged += (_, _) => _changes++;

    [Fact]
    public void StartsVisible() => Assert.True(_tracker.IsScreenVisible);

    [Fact]
    public void LockHidesTheScreenAndUnlockShowsItAgain()
    {
        _tracker.SetSessionLocked(true);
        Assert.False(_tracker.IsScreenVisible);

        _tracker.SetSessionLocked(false);
        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void DisplayOffHidesTheScreenAndDisplayOnShowsItAgain()
    {
        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOff);
        Assert.False(_tracker.IsScreenVisible);

        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOn);
        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void DisplaySleepHidesTheScreenAndWakeShowsItAgain()
    {
        // macOS (T-187): all screens sleep and wake together.
        _tracker.SetDisplayAsleep(true);
        Assert.False(_tracker.IsScreenVisible);

        _tracker.SetDisplayAsleep(false);
        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void WakeToTheLockScreenStaysHiddenUntilUnlocked()
    {
        _tracker.SetSessionLocked(true);
        _tracker.SetDisplayAsleep(true);
        _tracker.SetDisplayAsleep(false);
        Assert.False(_tracker.IsScreenVisible);

        _tracker.SetSessionLocked(false);
        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void DimmedDisplayCountsAsVisible()
    {
        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayDimmed);

        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void UnknownDisplayStateCountsAsVisible()
    {
        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOff);
        _tracker.SetConsoleDisplayState(7);

        Assert.True(_tracker.IsScreenVisible);
    }

    [Fact]
    public void UnlockWithTheDisplayOffStaysHidden()
    {
        _tracker.SetSessionLocked(true);
        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOff);
        _tracker.SetSessionLocked(false);

        Assert.False(_tracker.IsScreenVisible);
        // Hidden once by the lock; the display and the unlock changed nothing visible.
        Assert.Equal(1, _changes);

        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOn);
        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void RepeatedReportsRaiseNothing()
    {
        // The power setting registration reports the current state at once; a repeated lock notification is possible.
        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOn);
        _tracker.SetSessionLocked(false);
        _tracker.SetSessionLocked(true);
        _tracker.SetSessionLocked(true);

        Assert.Equal(1, _changes);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(5, false)]
    public void SessionFlagsTellALockedSession(int sessionFlags, bool locked) =>
        // WTSINFOEX_LEVEL1.SessionFlags: WTS_SESSIONSTATE_LOCK (0), UNLOCK (1), UNKNOWN (-1, counts as unlocked).
        Assert.Equal(locked, DisplayActivityTracker.IsLockedSession(sessionFlags));

    [Fact]
    public void SessionLockedAtStartHidesTheScreen()
    {
        // The service starts with the first camera window, which may open while the session is locked (T-151).
        _tracker.SetSessionLocked(DisplayActivityTracker.IsLockedSession(DisplayActivityTracker.SessionStateLock));

        Assert.False(_tracker.IsScreenVisible);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void RemoteSessionIgnoresTheConsoleDisplay()
    {
        // T-252: Remote Desktop reports the console display as off; the user looks at the remote client's screen.
        _tracker.SetRemoteSession(true);
        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOff);

        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void RemoteSessionStillHidesALockedScreen()
    {
        _tracker.SetRemoteSession(true);
        _tracker.SetSessionLocked(true);
        Assert.False(_tracker.IsScreenVisible);

        _tracker.SetSessionLocked(false);
        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void SwitchToRemoteDesktopShowsTheScreenWithTheConsoleDisplayOff()
    {
        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOff);
        Assert.False(_tracker.IsScreenVisible);

        _tracker.SetRemoteSession(true);

        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void BackOnTheConsoleTheLastConsoleDisplayStateCountsAgain()
    {
        _tracker.SetRemoteSession(true);
        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOff);

        _tracker.SetRemoteSession(false);
        Assert.False(_tracker.IsScreenVisible);
        Assert.Equal(1, _changes);

        _tracker.SetConsoleDisplayState(DisplayActivityTracker.DisplayOn);
        Assert.True(_tracker.IsScreenVisible);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void RemoteSessionReportedRepeatedlyRaisesNothing()
    {
        _tracker.SetRemoteSession(true);
        _tracker.SetRemoteSession(true);
        _tracker.SetRemoteSession(false);

        Assert.Equal(0, _changes);
    }
}
