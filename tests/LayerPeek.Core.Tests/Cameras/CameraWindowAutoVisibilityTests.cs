using Microsoft.Extensions.Time.Testing;

using LayerPeek.Core.Cameras;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Cameras;

/// <summary>
/// The camera window of an offline printer closes and opens again once it is back (T-150), with fake clocks: closing after
/// <see cref="CameraWindowAutoVisibilityOptions.CloseAfterOffline"/> of continuous <see cref="PrinterState.Offline"/>,
/// opening again after <see cref="CameraWindowAutoVisibilityOptions.OpenAfterOnline"/> of stable online.
/// </summary>
public sealed class CameraWindowAutoVisibilityTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
    private static readonly Uri StreamUri = new("rtsp://192.168.0.227/live");

    private readonly FakeTimeProvider _time = new(Start);

    private static CameraWindowAutoCommand Close(string id) => new(id, CameraWindowAutoAction.Close);

    private static CameraWindowAutoCommand Open(string id) => new(id, CameraWindowAutoAction.Open);

    private static CameraWindowAutoCommand Forget(string id) => new(id, CameraWindowAutoAction.ForgetAutoClosed);

    private static PrinterDefinition Printer(string id, bool camera = true) => new()
    {
        Id = id,
        Name = id.ToUpperInvariant(),
        ConnectionType = ConnectionType.PrusaLink,
        Address = $"10.0.0.{10 + id.Length}",
        Camera = new CameraSettings(camera, StreamUri),
    };

    private static AppSettings Settings(bool autoClose = true, params PrinterDefinition[] printers) =>
        new() { CameraWindowAutoClose = autoClose, Printers = printers };

    private static AppSettings Settings(params PrinterDefinition[] printers) => Settings(true, printers);

    private PrinterSnapshot Snapshot(string id, PrinterState state) =>
        PrinterSnapshot.Unavailable(id, state, _time.GetUtcNow());

    private void Advance(double seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

    private CameraWindowAutoVisibility Create(IEnumerable<string>? autoClosed = null, CameraWindowAutoVisibilityOptions? options = null) =>
        new(_time, options, autoClosed);

    /// <summary>One printer "mk4" with its window open and the printer seen online.</summary>
    private CameraWindowAutoVisibility CreateWithOpenWindow(string id = "mk4")
    {
        CameraWindowAutoVisibility machine = Create();
        Assert.Empty(machine.ApplySettings(Settings(Printer(id))));
        Assert.Empty(machine.Process(Snapshot(id, PrinterState.Idle)));
        Assert.Empty(machine.WindowOpened(id));
        return machine;
    }

    /// <summary>The window of "mk4" closed automatically, the printer still offline.</summary>
    private CameraWindowAutoVisibility CreateAutoClosed()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(30);
        Assert.Equal([Close("mk4")], machine.Evaluate());
        return machine;
    }

    // ---- Options ----

    [Fact]
    public void DefaultsAreThirtySecondsOfflineAndTenSecondsOnline()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), CameraWindowAutoVisibilityOptions.Default.CloseAfterOffline);
        Assert.Equal(TimeSpan.FromSeconds(10), CameraWindowAutoVisibilityOptions.Default.OpenAfterOnline);
        Assert.Same(CameraWindowAutoVisibilityOptions.Default, Create().Options);
    }

    [Fact]
    public void NegativeOrHugeDurationsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraWindowAutoVisibilityOptions { CloseAfterOffline = TimeSpan.FromSeconds(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraWindowAutoVisibilityOptions { OpenAfterOnline = TimeSpan.FromTicks(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraWindowAutoVisibilityOptions { CloseAfterOffline = CameraWindowAutoVisibilityOptions.MaxDelay + TimeSpan.FromTicks(1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraWindowAutoVisibilityOptions { OpenAfterOnline = TimeSpan.MaxValue });
        Assert.Throws<ArgumentNullException>(() => new CameraWindowAutoVisibility(null!));
    }

    [Fact]
    public void ShortenedDurationsApply()
    {
        // The Debug-only E2E switch of T-151 shortens both.
        CameraWindowAutoVisibility machine = Create(options: new() { CloseAfterOffline = TimeSpan.FromSeconds(2), OpenAfterOnline = TimeSpan.FromSeconds(1) });
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        machine.WindowOpened("mk4");

        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(2);
        Assert.Equal([Close("mk4")], machine.Evaluate());

        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(1);
        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void ZeroDurationsActOnTheFirstSnapshot()
    {
        CameraWindowAutoVisibility machine = Create(options: new() { CloseAfterOffline = TimeSpan.Zero, OpenAfterOnline = TimeSpan.Zero });
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.WindowOpened("mk4");
        machine.Process(Snapshot("mk4", PrinterState.Idle));

        Assert.Equal([Close("mk4")], machine.Process(Snapshot("mk4", PrinterState.Offline)));
        Assert.Equal([Open("mk4")], machine.Process(Snapshot("mk4", PrinterState.Idle)));
    }

    // ---- Closing ----

    [Fact]
    public void OfflineShorterThanTheDelayDoesNotClose()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();

        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Offline)));
        Advance(29.9);
        Assert.Empty(machine.Evaluate());
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Printing)));
        Advance(120);

        Assert.Empty(machine.Evaluate());
        Assert.Null(machine.NextDueAt);
        Assert.False(machine.IsAutoClosed("mk4"));
    }

    [Fact]
    public void OfflineForTheDelayClosesExactlyOnce()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Assert.Equal(Start.AddSeconds(30), machine.NextDueAt);

        Advance(30);
        Assert.Equal([Close("mk4")], machine.Evaluate());

        Assert.True(machine.IsAutoClosed("mk4"));
        Assert.Equal(["mk4"], machine.AutoClosedPrinterIds);
        Assert.True(Close("mk4").AutoClosed);
        Assert.Null(machine.NextDueAt);
        Advance(60);
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Offline)));
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void FurtherOfflineSnapshotsDoNotRestartTheDelay()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Offline)));
        Advance(10);

        // A snapshot that arrives when the delay is over closes too, without waiting for the timer.
        Assert.Equal([Close("mk4")], machine.Process(Snapshot("mk4", PrinterState.Offline)));
    }

    [Fact]
    public void ReachableSnapshotRestartsTheDelay()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(25);
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(1);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(29);
        Assert.Empty(machine.Evaluate());

        Advance(1);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Theory]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    [InlineData(PrinterState.Error)]
    [InlineData(PrinterState.Attention)]
    public void OnlyOfflineCountsAsSwitchedOff(PrinterState state)
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);
        machine.Process(Snapshot("mk4", state));
        Advance(600);

        Assert.Empty(machine.Evaluate());
        Assert.Empty(machine.Process(Snapshot("mk4", state)));
    }

    [Fact]
    public void ClosedWindowIsNotClosedAgain()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(60);

        Assert.Empty(machine.Evaluate());
        Assert.False(machine.IsAutoClosed("mk4"));
        Assert.Null(machine.NextDueAt);
    }

    // ---- Opening again ----

    [Fact]
    public void AutoClosedWindowOpensOnceAfterStableOnline()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        Advance(100);
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Idle)));
        Assert.Equal(_time.GetUtcNow().AddSeconds(10), machine.NextDueAt);

        Advance(9.9);
        Assert.Empty(machine.Evaluate());
        Advance(0.1);
        Assert.Equal([Open("mk4")], machine.Evaluate());

        Assert.False(Open("mk4").AutoClosed);
        Assert.False(machine.IsAutoClosed("mk4"));
        Assert.Empty(machine.AutoClosedPrinterIds);
        Assert.Null(machine.NextDueAt);
        Advance(60);
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Idle)));
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void ReopenedWindowClosesAgainWhenThePrinterGoesOffAgain()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(10);
        Assert.Equal([Open("mk4")], machine.Evaluate());
        // The App reports its own automatic open too: nothing changes.
        Assert.Empty(machine.WindowOpened("mk4"));

        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(30);

        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void OutageDuringTheOnlineDelayRestartsIt()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(5);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(3);
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(7);
        Assert.Empty(machine.Evaluate());

        Advance(3);
        Assert.Equal([Open("mk4")], machine.Process(Snapshot("mk4", PrinterState.Idle)));
    }

    [Theory]
    [InlineData(PrinterState.Unauthorized)]
    [InlineData(PrinterState.Unknown)]
    public void UnauthorizedAndUnknownCountAsOnlineForOpening(PrinterState state)
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", state));
        Advance(10);

        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void WindowClosedByTheUserNeverOpensAgain()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        Assert.Empty(machine.WindowClosedByUser("mk4"));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);

        Assert.Empty(machine.Evaluate());
        Assert.False(machine.IsAutoClosed("mk4"));
    }

    [Fact]
    public void WindowClosedByTheUserWhileOfflineDoesNotClose()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);
        Assert.Empty(machine.WindowClosedByUser("mk4"));
        Advance(20);

        Assert.Empty(machine.Evaluate());
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(20);
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void ManualOpenWhileWaitingForReopeningForgetsTheFlag()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(5);

        Assert.Equal([Forget("mk4")], machine.WindowOpened("mk4"));
        Assert.False(Forget("mk4").AutoClosed);
        Advance(10);
        Assert.Empty(machine.Evaluate());

        // Closed by the user meanwhile: never opened again.
        Assert.Empty(machine.WindowClosedByUser("mk4"));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        Assert.False(machine.IsAutoClosed("mk4"));
    }

    [Fact]
    public void UserClosingAnAutoClosedWindowForgetsTheFlag()
    {
        // E.g. "Hide camera" while the window is already gone: the user does not want it back.
        CameraWindowAutoVisibility machine = CreateAutoClosed();

        Assert.Equal([Forget("mk4")], machine.WindowClosedByUser("mk4"));
        Assert.Empty(machine.WindowClosedByUser("mk4"));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void WindowOpenedByTheUserWhileOfflineStaysOpenUntilThePrinterIsBack()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(60);

        Assert.Empty(machine.WindowOpened("mk4"));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Offline)));
        Assert.Null(machine.NextDueAt);

        // Back online and off again: now it closes as usual.
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(5);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(30);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void WindowOpenedByTheUserBeforeTheFirstSnapshotStaysOpenWhileOffline()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.WindowOpened("mk4");
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(60);

        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void ManualOpenOfAnAutoClosedWindowWhileOfflineForgetsTheFlagAndKeepsItOpen()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();

        Assert.Equal([Forget("mk4")], machine.WindowOpened("mk4"));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);
        Assert.Empty(machine.Evaluate());
    }

    // ---- Deferring (layout mode, open context menu: T-151) ----

    [Fact]
    public void DeferredCloseHappensWhenTheDeferralEndsAndThePrinterIsStillOffline()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        Assert.Empty(machine.SetCloseDeferred("mk4", deferred: true));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(45);

        Assert.Empty(machine.Evaluate());
        Assert.Null(machine.NextDueAt);
        Assert.Equal([Close("mk4")], machine.SetCloseDeferred("mk4", deferred: false));
        Assert.True(machine.IsAutoClosed("mk4"));
    }

    [Fact]
    public void DeferralEndingBeforeTheDelayKeepsTheDeadline()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.SetCloseDeferred("mk4", deferred: true);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(10);

        Assert.Empty(machine.SetCloseDeferred("mk4", deferred: false));
        Assert.Equal(Start.AddSeconds(30), machine.NextDueAt);
        Advance(20);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void DeferredCloseIsDroppedWhenThePrinterCameBack()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.SetCloseDeferred("mk4", deferred: true);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(45);
        machine.Process(Snapshot("mk4", PrinterState.Idle));

        Assert.Empty(machine.SetCloseDeferred("mk4", deferred: false));
        Assert.Empty(machine.SetCloseDeferred("mk4", deferred: false));
        Advance(60);
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void DeferralDoesNotStopOpening()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.SetCloseDeferred("mk4", deferred: true);
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(10);

        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    // ---- Settings ----

    [Fact]
    public void OptionIsOnByDefault()
    {
        Assert.True(AppSettings.Default.CameraWindowAutoClose);
        CameraWindowAutoVisibility machine = Create();
        Assert.False(machine.Enabled);

        machine.ApplySettings(new AppSettings { Printers = [Printer("mk4")] });

        Assert.True(machine.Enabled);
    }

    [Fact]
    public void OptionOffNeverClosesAndForgetsTheFlags()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();

        Assert.Equal([Forget("mk4")], machine.UpdateSettings(Settings(autoClose: false, Printer("mk4"))));
        Assert.False(machine.Enabled);
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);
        Assert.Empty(machine.Evaluate());

        // A window opened meanwhile is not closed while the option is off.
        machine.WindowOpened("mk4");
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        Assert.Null(machine.NextDueAt);
    }

    [Fact]
    public void OptionTurnedOnCountsTheDelayFromThen()
    {
        // Decision 8 (2026-11-03): an outage that began while the option was off counts only from turning it on.
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(autoClose: false, Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        machine.WindowOpened("mk4");
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);

        Assert.Empty(machine.UpdateSettings(Settings(Printer("mk4"))));
        Assert.Equal(_time.GetUtcNow().AddSeconds(30), machine.NextDueAt);
        Advance(10);
        Assert.Empty(machine.Evaluate());
        Advance(19.9);
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Offline)));
        Advance(0.1);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void OptionTurnedOnDuringALongOutageDoesNotCloseRightAway()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(autoClose: false, Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        machine.WindowOpened("mk4");
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        for (int i = 0; i < 10; i++)
        {
            Advance(60);
            machine.Process(Snapshot("mk4", PrinterState.Offline));
        }

        Assert.Empty(machine.UpdateSettings(Settings(Printer("mk4"))));
        Advance(29);
        Assert.Empty(machine.Evaluate());
        Advance(1);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void OptionTurnedOnWithANewMonitoringRunWaitsForTheNextSnapshot()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(autoClose: false, Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        machine.WindowOpened("mk4");
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(120);

        Assert.Empty(machine.ApplySettings(Settings(Printer("mk4"))));
        Assert.Null(machine.NextDueAt);
        Advance(5);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(29);
        Assert.Empty(machine.Evaluate());
        Advance(1);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void CameraEnabledAgainCountsTheDelayFromThen()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.UpdateSettings(Settings(Printer("mk4", camera: false)));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(60);

        Assert.Empty(machine.UpdateSettings(Settings(Printer("mk4"))));
        Advance(29);
        Assert.Empty(machine.Evaluate());
        Advance(1);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void UnchangedOptionKeepsTheRunningDelay()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);

        Assert.Empty(machine.UpdateSettings(Settings(Printer("mk4"))));
        Advance(10);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void DisabledCameraForgetsTheFlagAndIsNotClosed()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();

        Assert.Equal([Forget("mk4")], machine.UpdateSettings(Settings(Printer("mk4", camera: false))));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);
        Assert.Empty(machine.Evaluate());

        Assert.Empty(machine.UpdateSettings(Settings(Printer("mk4"))));
        Advance(60);
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void RemovedPrinterForgetsTheFlagAndItsSnapshotsAreIgnored()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();

        Assert.Equal([Forget("mk4")], machine.ApplySettings(Settings(Printer("xl"))));
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Idle)));
        Advance(60);
        Assert.Empty(machine.Evaluate());

        // Added again: starts fresh.
        Assert.Empty(machine.ApplySettings(Settings(Printer("mk4"))));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void RemovedPrinterWithAnOpenWindowIsNotClosedAndItsWindowIsStillKnownWhenAddedAgain()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();

        // Removed while its window is open: no snapshots count, nothing is due.
        Assert.Empty(machine.UpdateSettings(Settings(Printer("xl"))));
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Offline)));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        Assert.Null(machine.NextDueAt);

        // Added again with the window still open: it closes after the usual delay.
        Assert.Empty(machine.UpdateSettings(Settings(Printer("mk4"))));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(30);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void WindowOfARemovedPrinterClosedMeanwhileIsForgotten()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.UpdateSettings(Settings(Printer("xl")));

        Assert.Empty(machine.WindowClosedByUser("mk4"));

        // Added again: the window is closed, so going offline closes nothing and nothing opens later.
        Assert.Empty(machine.UpdateSettings(Settings(Printer("mk4"))));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        Assert.False(machine.IsAutoClosed("mk4"));
    }

    [Fact]
    public void UpdatedSettingsKeepTheRunningDelay()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);

        Assert.Empty(machine.UpdateSettings(Settings(Printer("mk4") with { Name = "Renamed" })));
        Advance(10);

        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void NewMonitoringRunStartsTheObservationAgain()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);

        Assert.Empty(machine.ApplySettings(Settings(Printer("mk4"))));
        Assert.Null(machine.NextDueAt);
        Advance(20);
        Assert.Empty(machine.Evaluate());

        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(29);
        Assert.Empty(machine.Evaluate());
        Advance(1);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void NewMonitoringRunKeepsTheFlagAndTheWindow()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(5);

        Assert.Empty(machine.ApplySettings(Settings(Printer("mk4"))));
        Assert.True(machine.IsAutoClosed("mk4"));
        Advance(10);
        Assert.Empty(machine.Evaluate());

        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(10);
        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void StoppedMonitoringCancelsPendingActions()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);

        Assert.Empty(machine.MonitoringStopped());
        Assert.Null(machine.NextDueAt);
        Advance(60);
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void NothingHappensBeforeTheFirstSettings()
    {
        CameraWindowAutoVisibility machine = Create(["mk4"]);
        machine.WindowOpened("xl");

        Assert.Empty(machine.Process(Snapshot("xl", PrinterState.Offline)));
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Idle)));
        Advance(60);
        Assert.Empty(machine.Evaluate());
        Assert.True(machine.IsAutoClosed("mk4"));
    }

    [Fact]
    public void WindowOpenedBeforeTheFirstSettingsIsClosedLater()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.WindowOpened("mk4");
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(30);

        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    // ---- Restart with a remembered flag (decision 4) ----

    [Fact]
    public void RememberedFlagOpensTheWindowOnceThePrinterIsStablyOnline()
    {
        CameraWindowAutoVisibility machine = Create(["MK4"]);
        Assert.Empty(machine.ApplySettings(Settings(Printer("mk4"))));
        Assert.True(machine.IsAutoClosed("mk4"));

        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(120);
        Assert.Empty(machine.Evaluate());
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(9);
        Assert.Empty(machine.Evaluate());
        Advance(1);

        // The id as configured.
        Assert.Equal([Open("mk4")], machine.Evaluate());
        Assert.False(machine.IsAutoClosed("MK4"));
    }

    [Fact]
    public void RememberedFlagOfAnOnlinePrinterOpensAfterTheOnlineDelay()
    {
        CameraWindowAutoVisibility machine = Create(["mk4"]);
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Printing));

        Assert.Equal(Start.AddSeconds(10), machine.NextDueAt);
        Advance(10);
        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void RememberedFlagOfAnUnknownPrinterIsForgotten()
    {
        CameraWindowAutoVisibility machine = Create(["gone", "mk4", " ", "MK4"]);

        Assert.Equal([Forget("gone")], machine.ApplySettings(Settings(Printer("mk4"))));
        Assert.Equal(["mk4"], machine.AutoClosedPrinterIds);
    }

    [Fact]
    public void RememberedFlagIsForgottenWhenTheOptionIsOff()
    {
        CameraWindowAutoVisibility machine = Create(["mk4"]);

        Assert.Equal([Forget("mk4")], machine.ApplySettings(Settings(autoClose: false, Printer("mk4"))));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);
        Assert.Empty(machine.Evaluate());
    }

    [Fact]
    public void WindowOpenedByTheUserAfterTheRestartForgetsTheRememberedFlag()
    {
        CameraWindowAutoVisibility machine = Create(["mk4"]);
        machine.ApplySettings(Settings(Printer("mk4")));

        Assert.Equal([Forget("mk4")], machine.WindowOpened("mk4"));
        machine.WindowClosedByUser("mk4");
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(60);
        Assert.Empty(machine.Evaluate());
    }

    // ---- Sleep and resume, clock jumps (decision 9) ----

    [Fact]
    public void StaleLimitFollowsThePollingIntervalAndTheOfflineBackOff()
    {
        CameraWindowAutoVisibility machine = Create();
        // Before any settings: the default polling interval (5 s) and the monitor defaults (1 min back-off, 10 s timeout).
        Assert.Equal(TimeSpan.FromSeconds(140), machine.StaleAfter);

        machine.ApplySettings(new AppSettings { PollingInterval = TimeSpan.FromMinutes(5), Printers = [Printer("mk4")] });
        Assert.Equal(TimeSpan.FromSeconds(620), machine.StaleAfter);

        CameraWindowAutoVisibility custom = Create(options: new()
        {
            Monitor = new PrinterMonitorOptions { MaxBackoff = TimeSpan.FromSeconds(20), PollTimeout = TimeSpan.FromSeconds(5) },
        });
        Assert.Equal(TimeSpan.FromSeconds(50), custom.StaleAfter);
        Assert.Throws<ArgumentNullException>(() => new CameraWindowAutoVisibilityOptions { Monitor = null! });
    }

    [Fact]
    public void CloseDueAfterALongGapIsNotExecuted()
    {
        // The computer slept with the printer offline less than the delay: after waking nothing is known.
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(3600);

        Assert.Empty(machine.Evaluate());
        Assert.Null(machine.NextDueAt);
        Assert.False(machine.IsAutoClosed("mk4"));
    }

    [Fact]
    public void FirstSnapshotAfterALongGapStartsTheDelayAgain()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(3600);

        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Offline)));
        Assert.Equal(_time.GetUtcNow().AddSeconds(30), machine.NextDueAt);
        Advance(29);
        Assert.Empty(machine.Evaluate());
        Advance(1);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void OpenDueAfterALongGapWaitsForFreshOnlineSnapshots()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(3600);

        Assert.Empty(machine.Evaluate());
        Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Idle)));
        Advance(9);
        Assert.Empty(machine.Evaluate());
        Advance(1);
        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void GapsOfTheOfflineBackOffAreNotStale()
    {
        // The monitor polls an unreachable printer at most every minute, plus the poll timeout.
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(70);
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(10);

        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void LongPollingIntervalIsNotStale()
    {
        CameraWindowAutoVisibility machine = Create();
        AppSettings slow = new() { PollingInterval = TimeSpan.FromMinutes(5), Printers = [Printer("mk4")] };
        machine.ApplySettings(slow);
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        machine.WindowOpened("mk4");
        Advance(300);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(30);

        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void LongDeferralWithOfflineBackOffSnapshotsClosesWhenItEnds()
    {
        // Layout mode for minutes while the monitor keeps reporting offline at its back-off cadence: the close is not lost.
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.SetCloseDeferred("mk4", deferred: true);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        for (int i = 0; i < 10; i++)
        {
            Advance(70);
            Assert.Empty(machine.Process(Snapshot("mk4", PrinterState.Offline)));
        }

        Advance(69);
        Assert.Equal([Close("mk4")], machine.SetCloseDeferred("mk4", deferred: false));
    }

    [Fact]
    public void SnapshotAsOldAsTheStaleLimitIsStillFresh()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        _time.Advance(machine.StaleAfter);

        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void SnapshotJustOlderThanTheStaleLimitIsNotActedOn()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        _time.Advance(machine.StaleAfter + TimeSpan.FromTicks(1));

        Assert.Empty(machine.Evaluate());
        Assert.Null(machine.NextDueAt);
        Assert.True(machine.IsAutoClosed("mk4"));
    }

    [Fact]
    public void WindowOpenedByTheUserAfterAStaleOnlineStaysOpenWhileOffline()
    {
        // Decision 7 with decision 9: "online" from before a sleep is not known to be online any more.
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(3600);

        Assert.Empty(machine.WindowOpened("mk4"));
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(60);
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Assert.Empty(machine.Evaluate());
        Assert.Null(machine.NextDueAt);
    }

    [Fact]
    public void SystemResumedDropsPendingActions()
    {
        CameraWindowAutoVisibility machine = CreateWithOpenWindow();
        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(20);

        Assert.Empty(machine.SystemResumed());
        Assert.Null(machine.NextDueAt);
        Advance(10);
        Assert.Empty(machine.Evaluate());

        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(29);
        Assert.Empty(machine.Evaluate());
        Advance(1);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    [Fact]
    public void SystemResumedKeepsFlagsAndWindows()
    {
        CameraWindowAutoVisibility machine = CreateAutoClosed();

        machine.SystemResumed();

        Assert.True(machine.IsAutoClosed("mk4"));
        machine.Process(Snapshot("mk4", PrinterState.Idle));
        Advance(10);
        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void ClockGoingBackDoesNotActOnTheOldState()
    {
        ManualClock clock = new(Start);
        CameraWindowAutoVisibility machine = new(clock);
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.Process(PrinterSnapshot.Unavailable("mk4", PrinterState.Idle, clock.Now));
        machine.WindowOpened("mk4");
        machine.Process(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, clock.Now));
        clock.Now = Start.AddHours(-1);

        Assert.Empty(machine.Evaluate());
        Assert.Null(machine.NextDueAt);

        machine.Process(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, clock.Now));
        clock.Now = clock.Now.AddSeconds(30);
        Assert.Equal([Close("mk4")], machine.Evaluate());
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // ---- Several printers ----

    [Fact]
    public void EveryPrinterHasItsOwnState()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(Printer("mk4"), Printer("xl"), Printer("mini")));
        foreach (string id in new[] { "mk4", "xl", "mini" })
        {
            machine.Process(Snapshot(id, PrinterState.Idle));
            machine.WindowOpened(id);
        }

        machine.Process(Snapshot("mk4", PrinterState.Offline));
        Advance(10);
        machine.Process(Snapshot("xl", PrinterState.Offline));
        Assert.Equal(Start.AddSeconds(30), machine.NextDueAt);
        Advance(20);
        Assert.Equal([Close("mk4")], machine.Evaluate());
        Assert.Equal(Start.AddSeconds(40), machine.NextDueAt);
        machine.WindowClosedByUser("xl");
        Advance(10);
        Assert.Empty(machine.Evaluate());

        machine.Process(Snapshot("mk4", PrinterState.Idle));
        machine.Process(Snapshot("xl", PrinterState.Idle));
        Advance(10);
        Assert.Equal([Open("mk4")], machine.Evaluate());
    }

    [Fact]
    public void SeveralPrintersDueAtOnceGiveOneCommandEach()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(Printer("mk4"), Printer("xl")));
        foreach (string id in new[] { "mk4", "xl" })
        {
            machine.Process(Snapshot(id, PrinterState.Idle));
            machine.WindowOpened(id);
            machine.Process(Snapshot(id, PrinterState.Offline));
        }

        Advance(30);

        Assert.Equal([Close("mk4"), Close("xl")], machine.Evaluate());
    }

    [Fact]
    public void PrinterIdsAreCaseInsensitive()
    {
        CameraWindowAutoVisibility machine = Create();
        machine.ApplySettings(Settings(Printer("mk4")));
        machine.Process(Snapshot("MK4", PrinterState.Idle));
        machine.WindowOpened("Mk4");
        machine.Process(Snapshot("mK4", PrinterState.Offline));
        Advance(30);

        Assert.Equal([Close("mk4")], machine.Evaluate());
        Assert.True(machine.IsAutoClosed("MK4"));
    }

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        CameraWindowAutoVisibility machine = Create();

        Assert.Throws<ArgumentNullException>(() => machine.ApplySettings(null!));
        Assert.Throws<ArgumentNullException>(() => machine.UpdateSettings(null!));
        Assert.Throws<ArgumentNullException>(() => machine.Process(null!));
        Assert.ThrowsAny<ArgumentException>(() => machine.WindowOpened(" "));
        Assert.ThrowsAny<ArgumentException>(() => machine.WindowClosedByUser(null!));
        Assert.ThrowsAny<ArgumentException>(() => machine.SetCloseDeferred("", deferred: true));
        Assert.ThrowsAny<ArgumentException>(() => machine.IsAutoClosed(""));
    }
}
