using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using LayerPeek.App.Cameras;
using LayerPeek.App.Platform;
using LayerPeek.App.Tests.Tray;
using LayerPeek.App.Tray;
using LayerPeek.App.Windows;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>
/// T-151: the wiring of the state machine of T-150 to the camera windows: snapshots, monitoring runs and the resume of the
/// computer in, automatic closes and silent opens out, the flags in the window state store. The state machine's own rules
/// are tested in Core (<c>CameraWindowAutoVisibilityTests</c>).
/// </summary>
public sealed class CameraWindowAutoVisibilityObserverTests : IDisposable
{
    private static readonly TimeSpan CloseAfter = CameraWindowAutoVisibilityOptions.Default.CloseAfterOffline;
    private static readonly TimeSpan OpenAfter = CameraWindowAutoVisibilityOptions.Default.OpenAfterOnline;

    private readonly FakeTimeProvider _time = new(TestPrinters.Now);
    private readonly FakePrinterMonitor _monitor = new();
    private readonly ManualDispatcher _dispatcher = new();
    private readonly MemoryCameraWindowStateStore _states = new();
    private readonly FakeResumeEvents _resume = new();
    private readonly FakeTarget _windows = new();
    private CameraWindowAutoVisibilityObserver? _observer;

    private CameraWindowAutoVisibilityObserver Observer => _observer ??= new(_monitor, _states, _dispatcher, _time, NullLogger<CameraWindowAutoVisibilityObserver>.Instance, resumeEvents: _resume);

    public void Dispose() => _observer?.Dispose();

    [Fact]
    public void ClosesTheOpenWindowAfterThirtySecondsOfflineOnlyOnceAndRemembersIt()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);

        Raise(PrinterState.Offline);
        Advance(CloseAfter - TimeSpan.FromSeconds(1));
        Assert.Empty(_windows.AutomaticCloses);

        // No further snapshot needed (the offline back-off is long): the timer wakes the state machine.
        Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["a"], _windows.AutomaticCloses);
        Assert.False(_windows.IsOpen("a"));
        Assert.Equal(["a"], _states.GetAutoClosedPrinters());

        Raise(PrinterState.Offline);
        Advance(CloseAfter);
        Assert.Single(_windows.AutomaticCloses);
    }

    [Fact]
    public void AShortOutageClosesNothing()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);

        Raise(PrinterState.Offline);
        Advance(CloseAfter - TimeSpan.FromSeconds(5));
        Raise(PrinterState.Idle);
        Advance(CloseAfter);

        Assert.Empty(_windows.AutomaticCloses);
        Assert.True(_windows.IsOpen("a"));
    }

    [Fact]
    public void ReopensTheAutomaticallyClosedWindowSilentlyOnceThePrinterIsBackAndForgetsTheFlag()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);
        Raise(PrinterState.Offline);
        Advance(CloseAfter);

        Raise(PrinterState.Idle);
        Advance(OpenAfter - TimeSpan.FromSeconds(1));
        Assert.Empty(_windows.AutomaticOpens);
        Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(["a"], _windows.AutomaticOpens);
        Assert.True(_windows.IsOpen("a"));
        Assert.Empty(_states.GetAutoClosedPrinters());
    }

    [Fact]
    public void AWindowTheUserClosedNeverOpensByItself()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);
        Raise(PrinterState.Offline);
        Advance(CloseAfter);
        Assert.Single(_windows.AutomaticCloses);

        // Reopened by the user while offline, then closed by the user: the flag is gone.
        _windows.UserOpens("a");
        _windows.UserCloses("a");
        Pump();
        Raise(PrinterState.Idle);
        Advance(OpenAfter * 3);

        Assert.Empty(_windows.AutomaticOpens);
        Assert.Empty(_states.GetAutoClosedPrinters());
    }

    [Fact]
    public void TheCloseWaitsForTheLayoutModeOrContextMenuAndHappensWhenItEnds()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);
        _windows.SetDeferred("a", true);

        Raise(PrinterState.Offline);
        Advance(CloseAfter * 2);
        Assert.Empty(_windows.AutomaticCloses);

        _windows.SetDeferred("a", false);
        Pump();
        Assert.Equal(["a"], _windows.AutomaticCloses);
    }

    [Fact]
    public void AWindowOpenedRightIntoTheLayoutModeDefersTheClose()
    {
        Start();
        _windows.DeferNextOpen = true;
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);

        Raise(PrinterState.Offline);
        Advance(CloseAfter * 2);

        Assert.Empty(_windows.AutomaticCloses);
    }

    [Fact]
    public void AFlagOfTheLastRunOpensTheWindowOnceThePrinterIsOnline()
    {
        _states.SetAutoClosed("a", true);
        Start();

        Raise(PrinterState.Printing);
        Advance(OpenAfter);

        Assert.Equal(["a"], _windows.AutomaticOpens);
        Assert.Empty(_states.GetAutoClosedPrinters());
    }

    [Fact]
    public void NothingHappensWithTheOptionOff()
    {
        Start(autoClose: false);
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);

        Raise(PrinterState.Offline);
        Advance(CloseAfter * 2);

        Assert.Empty(_windows.AutomaticCloses);
    }

    [Fact]
    public void SavedSettingsThatTurnTheOptionOffForgetTheFlag()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);
        Raise(PrinterState.Offline);
        Advance(CloseAfter);
        Assert.Equal(["a"], _states.GetAutoClosedPrinters());

        Observer.UpdateSettings(Settings(autoClose: false));
        Pump();
        Raise(PrinterState.Idle);
        Advance(OpenAfter * 2);

        Assert.Empty(_states.GetAutoClosedPrinters());
        Assert.Empty(_windows.AutomaticOpens);
    }

    [Fact]
    public void AWindowThatCannotOpenStaysClosedForGood()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);
        Raise(PrinterState.Offline);
        Advance(CloseAfter);
        _windows.RefuseOpening = true;

        Raise(PrinterState.Idle);
        Advance(OpenAfter);
        _windows.RefuseOpening = false;
        Raise(PrinterState.Offline);
        Advance(CloseAfter);
        Raise(PrinterState.Idle);
        Advance(OpenAfter * 2);

        Assert.Equal(["a"], _windows.AutomaticOpens);
        Assert.False(_windows.IsOpen("a"));
        Assert.Empty(_states.GetAutoClosedPrinters());
    }

    [Fact]
    public void TheResumeOfTheComputerStartsThePendingCloseAgain()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);
        Raise(PrinterState.Offline);
        Advance(CloseAfter - TimeSpan.FromSeconds(5));

        _resume.Raise();
        Pump();
        Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(_windows.AutomaticCloses);

        // The first fresh snapshot after the resume starts the delay.
        Raise(PrinterState.Offline);
        Advance(CloseAfter - TimeSpan.FromSeconds(1));
        Assert.Empty(_windows.AutomaticCloses);
        Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["a"], _windows.AutomaticCloses);
    }

    [Fact]
    public void NothingRunsBeforeTheWindowsAreAttachedAndTheResumeEventsStartWithThem()
    {
        Observer.ApplySettings(Settings());
        Pump();
        Raise(PrinterState.Offline);
        Advance(CloseAfter * 2);
        Assert.False(Observer.IsRunning);
        Assert.False(_resume.Started);

        Observer.Attach(_windows);
        Pump();

        Assert.True(Observer.IsRunning);
        Assert.True(_resume.Started);
        Assert.Empty(_windows.AutomaticCloses);
    }

    [Fact]
    public void AWindowOpenBeforeTheStartIsFollowed()
    {
        _windows.UserOpens("a");
        Start();

        Raise(PrinterState.Idle);
        Raise(PrinterState.Offline);
        Advance(CloseAfter);

        Assert.Equal(["a"], _windows.AutomaticCloses);
    }

    [Fact]
    public void AfterDetachTheClosesOfQuittingAreNoClosesByTheUser()
    {
        Start();
        _windows.UserOpens("a");
        // Known online (T-150 decision 7: a window opened without that stays open until the printer was online).
        Raise(PrinterState.Idle);
        Raise(PrinterState.Offline);
        Advance(CloseAfter);
        Assert.Equal(["a"], _states.GetAutoClosedPrinters());
        _windows.UserOpens("b");
        Pump();

        Observer.Detach();
        _windows.UserCloses("b");
        Raise(PrinterState.Offline, "b");
        Advance(CloseAfter * 2);

        Assert.Equal(["a"], _states.GetAutoClosedPrinters());
        Assert.Equal(["a"], _windows.AutomaticCloses);
        Assert.Equal(0, _windows.Subscribers);
    }

    [Fact]
    public async Task SnapshotsAndSettingsFromOtherThreadsArePostedToTheUiThread()
    {
        Start();
        int posts = _dispatcher.PostCount;
        CancellationToken cancellation = TestContext.Current.CancellationToken;

        await Task.Run(() => _monitor.Raise(TestPrinters.Snapshot(PrinterState.Offline, printerId: "a")), cancellation).WaitAsync(TestTimeouts.Wait, cancellation);
        await Task.Run(() => Observer.UpdateSettings(Settings()), cancellation).WaitAsync(TestTimeouts.Wait, cancellation);

        Assert.Equal(posts + 2, _dispatcher.PostCount);
    }

    [Fact]
    public void DisposeStopsFollowingTheMonitorAndTheResume()
    {
        Start();
        Observer.Dispose();
        Pump();

        Assert.Equal(0, _monitor.SubscriberCount);
        Assert.Equal(0, _resume.Subscribers);
        Assert.Equal(0, _windows.Subscribers);
    }

    private static AppSettings Settings(bool autoClose = true) => new()
    {
        CameraWindowAutoClose = autoClose,
        Printers = [Camera("a"), Camera("b")],
    };

    private static PrinterDefinition Camera(string id) =>
        TestPrinters.Definition(id, id.ToUpperInvariant()) with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://192.168.1.20/live")) };

    private void Start(bool autoClose = true)
    {
        Observer.ApplySettings(Settings(autoClose));
        Observer.Attach(_windows);
        Pump();
    }

    private void Raise(PrinterState state, string printerId = "a")
    {
        _monitor.Raise(TestPrinters.Snapshot(state, printerId: printerId) with { Timestamp = _time.GetUtcNow() });
        Pump();
    }

    private void Advance(TimeSpan by)
    {
        _time.Advance(by);
        Pump();
    }

    private void Pump() => _dispatcher.RunAll();

    private sealed class FakeResumeEvents : ISystemResumeEvents
    {
        private EventHandler? _resumed;

        public event EventHandler? Resumed
        {
            add => _resumed += value;
            remove => _resumed -= value;
        }

        public bool Started { get; private set; }

        public int Subscribers => _resumed?.GetInvocationList().Length ?? 0;

        public void Start() => Started = true;

        public void Raise() => _resumed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Camera windows that only remember which are open, reporting like <see cref="CameraWindows"/>.</summary>
    private sealed class FakeTarget : ICameraWindowAutoTarget
    {
        private readonly HashSet<string> _open = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _deferred = new(StringComparer.OrdinalIgnoreCase);
        private EventHandler<CameraWindowOpenChangedEventArgs>? _openChanged;
        private EventHandler<CameraWindowCloseDeferralChangedEventArgs>? _deferralChanged;

        public event EventHandler<CameraWindowOpenChangedEventArgs>? OpenChanged
        {
            add => _openChanged += value;
            remove => _openChanged -= value;
        }

        public event EventHandler<CameraWindowCloseDeferralChangedEventArgs>? CloseDeferralChanged
        {
            add => _deferralChanged += value;
            remove => _deferralChanged -= value;
        }

        public IReadOnlyCollection<string> OpenPrinterIds => _open;

        public List<string> AutomaticCloses { get; } = [];

        public List<string> AutomaticOpens { get; } = [];

        public bool RefuseOpening { get; set; }

        /// <summary>The next window opens in the layout mode (T-092: started from the settings window).</summary>
        public bool DeferNextOpen { get; set; }

        public int Subscribers => (_openChanged?.GetInvocationList().Length ?? 0) + (_deferralChanged?.GetInvocationList().Length ?? 0);

        public bool IsOpen(string printerId) => _open.Contains(printerId);

        public void UserOpens(string printerId)
        {
            if (_open.Add(printerId))
            {
                if (DeferNextOpen)
                {
                    DeferNextOpen = false;
                    _ = _deferred.Add(printerId);
                }

                _openChanged?.Invoke(this, new CameraWindowOpenChangedEventArgs(printerId, isOpen: true));
            }
        }

        public void UserCloses(string printerId)
        {
            if (_open.Remove(printerId))
            {
                _ = _deferred.Remove(printerId);
                _openChanged?.Invoke(this, new CameraWindowOpenChangedEventArgs(printerId, isOpen: false));
            }
        }

        public void SetDeferred(string printerId, bool deferred)
        {
            if (deferred ? _deferred.Add(printerId) : _deferred.Remove(printerId))
            {
                _deferralChanged?.Invoke(this, new CameraWindowCloseDeferralChangedEventArgs(printerId, deferred));
            }
        }

        public bool ShowAutomatically(string printerId)
        {
            AutomaticOpens.Add(printerId);
            if (RefuseOpening)
            {
                return false;
            }

            if (_open.Add(printerId))
            {
                _openChanged?.Invoke(this, new CameraWindowOpenChangedEventArgs(printerId, isOpen: true));
            }

            return true;
        }

        public bool CloseAutomatically(string printerId)
        {
            if (!_open.Remove(printerId))
            {
                return false;
            }

            AutomaticCloses.Add(printerId);
            _ = _deferred.Remove(printerId);
            _openChanged?.Invoke(this, new CameraWindowOpenChangedEventArgs(printerId, isOpen: false, automatic: true));
            return true;
        }

        public bool IsCloseDeferred(string printerId) => _deferred.Contains(printerId);
    }
}
