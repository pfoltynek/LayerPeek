using System.Globalization;

using PrusaTrayDiagnostic.Core.Events;
using PrusaTrayDiagnostic.Core.Printers;

namespace PrusaTrayDiagnostic.Core.Tests.Events;

/// <summary>
/// State transitions → events. Scenarios are written as comma-separated steps, each step is one snapshot polled
/// <see cref="Step"/> after the previous one:
/// <list type="bullet">
/// <item><c>State</c> – snapshot without a job (e.g. <c>Idle</c>, <c>Offline</c>);</item>
/// <item><c>State:a</c> – job with id and file name derived from <c>a</c> (<c>a.bgcode</c>);</item>
/// <item><c>State:f=a</c> – job with the file name <c>a.bgcode</c> only (no id);</item>
/// <item><c>…@15</c> – the job reports 15 minutes remaining;</item>
/// <item><c>+60</c> – no snapshot, the clock advances 60 s.</item>
/// </list>
/// The first snapshot is the baseline; expected events are listed in order, comma-separated.
/// </summary>
public sealed class PrintEventDetectorTests
{
    internal const string PrinterId = "mk4";

    internal static readonly TimeSpan Step = TimeSpan.FromSeconds(5);

    internal static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    // Explicit (not the defaults), so that the scenarios do not change when a default does.
    internal static readonly PrintEventDetectorOptions Options = new()
    {
        RemainingTimeThreshold = TimeSpan.FromMinutes(10),
        OfflineDelay = TimeSpan.FromSeconds(60),
    };

    [Theory]
    [InlineData("Idle")]
    [InlineData("Printing:a")]
    [InlineData("Paused:a")]
    [InlineData("Finished:a")]
    [InlineData("Stopped:a")]
    [InlineData("Busy")]
    [InlineData("Offline")]
    [InlineData("Offline,+60,Offline,+60,Offline")]
    [InlineData("Unknown")]
    public void FirstSnapshotIsBaselineWithoutEvents(string steps) => AssertEvents(steps, "");

    [Theory]
    [InlineData("Error", "Error")]
    [InlineData("Error,Error,Error", "Error")]
    [InlineData("Attention", "AttentionRequired")]
    [InlineData("Attention:a,Attention:a", "AttentionRequired")]
    [InlineData("Attention:a,Printing:a", "AttentionRequired,PrintResumed")]
    [InlineData("Unauthorized", "CredentialsRejected")]
    [InlineData("Unknown,Error", "Error")]
    [InlineData("Offline,Error", "Error")]
    [InlineData("Unauthorized,Attention", "CredentialsRejected,AttentionRequired")]
    public void ProblemsPresentAtStartupAreReported(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Idle,Printing:a", "PrintStarted")]
    [InlineData("Busy,Printing:a", "PrintStarted")]
    [InlineData("Finished:a,Printing:b", "PrintStarted")]
    [InlineData("Stopped:a,Printing:b", "PrintStarted")]
    [InlineData("Idle,Paused:a", "PrintStarted,PrintPaused")]
    [InlineData("Idle,Busy,Printing:a", "PrintStarted")]
    [InlineData("Idle,Attention,Printing:a", "AttentionRequired,PrintStarted")]
    [InlineData("Idle,Printing", "PrintStarted")]
    [InlineData("Idle,Printing:a,Printing:a,Printing:a", "PrintStarted")]
    [InlineData("Finished:a,Printing:a", "PrintStarted")]
    [InlineData("Busy:a,Printing:a", "")]
    [InlineData("Busy:a,Paused:a", "PrintPaused")]
    [InlineData("Busy:a,Printing:b", "PrintStarted")]
    public void PrintStartIsDetectedOnce(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Printing:a,Printing:b", "PrintStarted")]
    [InlineData("Printing:f=a,Printing:f=b", "PrintStarted")]
    [InlineData("Printing:a,Printing:f=b", "PrintStarted")]
    [InlineData("Paused:a,Printing:b", "PrintStarted")]
    [InlineData("Paused:a,Paused:b", "PrintStarted,PrintPaused")]
    [InlineData("Printing:a,Printing:f=a", "")]
    [InlineData("Printing:a,Printing", "")]
    [InlineData("Printing,Printing:a", "")]
    [InlineData("Printing:f=a,Printing:a", "")]
    [InlineData("Printing:a,Offline,+60,Offline,Attention:b,Printing:b", "PrinterOffline,PrinterOnline,AttentionRequired,PrintStarted")]
    [InlineData("Printing:a,Offline,+60,Offline,Busy:b,Printing:b", "PrinterOffline,PrinterOnline,PrintStarted")]
    [InlineData("Printing:a,Error:b,Printing:b", "Error,PrintStarted")]
    public void JobIdentityChangeStartsNewPrint(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Printing:a,Paused:a", "PrintPaused")]
    [InlineData("Printing:a,Paused:a,Paused:a,Printing:a,Printing:a", "PrintPaused,PrintResumed")]
    [InlineData("Paused:a,Printing:a", "PrintResumed")]
    [InlineData("Printing:a,Attention:a,Attention:a,Printing:a", "AttentionRequired,PrintResumed")]
    [InlineData("Printing:a,Attention,Printing:a", "AttentionRequired,PrintResumed")]
    [InlineData("Printing:a,Paused:a,Attention:a,Paused:a", "PrintPaused,AttentionRequired,PrintPaused")]
    [InlineData("Printing:a,Busy,Printing:a", "")]
    [InlineData("Printing:a,Error,Printing:a", "Error")]
    [InlineData("Printing:a,Paused:a,Busy,Printing:a", "PrintPaused,PrintResumed")]
    [InlineData("Printing:a,Paused:a,Busy,Paused:a", "PrintPaused")]
    [InlineData("Printing:a,Attention:a,Busy,Attention:a", "AttentionRequired")]
    [InlineData("Printing:a,Attention:a,Busy,Printing:a", "AttentionRequired,PrintResumed")]
    public void PauseAndResumeAreDetected(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Printing:a,Finished:a", "PrintFinished")]
    [InlineData("Printing:a,Finished:a,Finished:a,Finished:a", "PrintFinished")]
    [InlineData("Printing:a,Finished", "PrintFinished")]
    [InlineData("Paused:a,Stopped:a", "PrintStopped")]
    [InlineData("Printing:a,Stopped:a,Stopped:a", "PrintStopped")]
    [InlineData("Idle,Finished:a", "PrintFinished")]
    [InlineData("Finished:a,Finished:b", "PrintFinished")]
    [InlineData("Finished:a,Stopped:b", "PrintStopped")]
    [InlineData("Printing:a,Finished:a,Idle,Printing:b,Finished:b", "PrintFinished,PrintStarted,PrintFinished")]
    [InlineData("Printing:a,Idle", "")]
    [InlineData("Printing:a,Idle,Finished", "PrintFinished")]
    public void PrintEndIsDetectedOnce(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Printing:a,Error,Error,Error", "Error")]
    [InlineData("Idle,Error,Idle,Error", "Error,Error")]
    [InlineData("Printing:a,Attention:a,Attention:a", "AttentionRequired")]
    [InlineData("Idle,Attention,Error,Attention", "AttentionRequired,Error,AttentionRequired")]
    [InlineData("Idle,Busy,Idle,Busy", "")]
    [InlineData("Idle,Attention,Busy,Attention", "AttentionRequired")]
    [InlineData("Idle,Error,Busy,Error", "Error")]
    [InlineData("Error,Busy,Error", "Error")]
    [InlineData("Idle,Error,Busy,Idle,Error", "Error,Error")]
    [InlineData("Idle,Attention,Busy,Printing:a", "AttentionRequired,PrintStarted")]
    public void ErrorAndAttentionAreDetectedOnEntry(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Printing:a,Unknown,Printing:a", "")]
    [InlineData("Idle,Unknown,Printing:a", "PrintStarted")]
    [InlineData("Unknown,Printing:a", "")]
    [InlineData("Error,Unknown,Error", "Error")]
    [InlineData("Undefined,Idle,Undefined,Printing:a", "PrintStarted")]
    public void UnknownStatesAreIgnored(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Idle,Unauthorized", "CredentialsRejected")]
    [InlineData("Idle,Unauthorized,Unauthorized,Unauthorized", "CredentialsRejected")]
    [InlineData("Printing:a,Unauthorized,Unauthorized,Printing:a", "CredentialsRejected")]
    [InlineData("Printing:a,Unauthorized,Finished:a", "CredentialsRejected,PrintFinished")]
    [InlineData("Unauthorized,Idle,Printing:a", "CredentialsRejected,PrintStarted")]
    [InlineData("Idle,Unauthorized,Idle,Unauthorized", "CredentialsRejected,CredentialsRejected")]
    [InlineData("Idle,Unauthorized,Offline,Unknown,Unauthorized", "CredentialsRejected")]
    [InlineData("Idle,Unauthorized,Offline,+60,Offline", "CredentialsRejected,PrinterOffline")]
    [InlineData("Unauthorized,Offline,+60,Offline", "CredentialsRejected,PrinterOffline")]
    public void RejectedCredentialsAreReportedOnceUntilAuthorizedAgain(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Idle,Offline,Idle", "")]
    [InlineData("Idle,Offline,Offline,Offline,Idle", "")]
    [InlineData("Idle,Offline,+60,Offline,Idle", "PrinterOffline,PrinterOnline")]
    [InlineData("Idle,Offline,+60,Offline,Offline,+60,Offline,Idle,Idle", "PrinterOffline,PrinterOnline")]
    [InlineData("Idle,Offline,+60,Offline,Unknown,Offline,Idle", "PrinterOffline,PrinterOnline")]
    [InlineData("Idle,Offline,+60,Offline,Unauthorized", "PrinterOffline,CredentialsRejected")]
    [InlineData("Idle,Offline,+60,Offline,Idle,Offline,+60,Offline,Idle", "PrinterOffline,PrinterOnline,PrinterOffline,PrinterOnline")]
    [InlineData("Idle,Offline,+60,Offline,Idle,Offline,Idle", "PrinterOffline,PrinterOnline")]
    [InlineData("Printing:a,Offline,+60,Offline,Printing:a", "PrinterOffline,PrinterOnline")]
    [InlineData("Printing:a,Offline,+60,Offline,Printing:b", "PrinterOffline,PrinterOnline,PrintStarted")]
    [InlineData("Printing:a,Offline,+60,Offline,Finished:a", "PrinterOffline,PrinterOnline,PrintFinished")]
    [InlineData("Printing:a,Offline,+60,Offline,Idle", "PrinterOffline,PrinterOnline")]
    [InlineData("Printing:a,Offline,Finished:a", "PrintFinished")]
    [InlineData("Finished:a,Offline,+60,Offline,Finished:a", "PrinterOffline,PrinterOnline")]
    [InlineData("Offline,+60,Offline,Idle", "")]
    [InlineData("Offline,+60,Offline,Idle,Offline,+60,Offline", "PrinterOffline")]
    [InlineData("Offline,Idle,Printing:a", "PrintStarted")]
    [InlineData("Offline,Printing:a", "")]
    [InlineData("Unknown,Offline,+60,Offline,Idle", "")]
    public void OfflineIsReportedOnlyAfterDelayAndOnlineOnlyAfterOffline(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData("Printing:a@30,Printing:a@15,Printing:a@9", "RemainingTime")]
    [InlineData("Printing:a@30,Printing:a@10", "RemainingTime")]
    [InlineData("Printing:a@30,Printing:a@10.5", "")]
    [InlineData("Printing:a@30,Printing:a@9,Printing:a@11,Printing:a@9,Printing:a@5", "RemainingTime")]
    [InlineData("Printing:a@30,Printing:a@9,Printing:a@30,Printing:a@9", "RemainingTime")]
    [InlineData("Printing:a@9,Printing:a@5", "")]
    [InlineData("Idle,Printing:a@8,Printing:a@5,Finished:a", "PrintStarted,PrintFinished")]
    [InlineData("Printing:a@30,Printing:a,Printing:a@9", "RemainingTime")]
    [InlineData("Printing:a,Printing:a@9", "")]
    [InlineData("Printing:a@30,Paused:a@9,Printing:a@9", "PrintPaused,PrintResumed,RemainingTime")]
    [InlineData("Printing:a@30,Attention:a@9,Printing:a@9", "AttentionRequired,PrintResumed,RemainingTime")]
    [InlineData("Printing:a@30,Printing:b@9", "PrintStarted")]
    [InlineData("Printing:a@30,Printing:b@30,Printing:b@9", "PrintStarted,RemainingTime")]
    [InlineData("Printing:a@30,Printing:a@9,Finished:a,Idle,Printing:b@30,Printing:b@9", "RemainingTime,PrintFinished,PrintStarted,RemainingTime")]
    [InlineData("Printing:a@30,Printing:a@9,Finished:a,Printing:a@30,Printing:a@9", "RemainingTime,PrintFinished,PrintStarted,RemainingTime")]
    [InlineData("Printing:a@30,Offline,+60,Offline,Printing:a@9", "PrinterOffline,PrinterOnline,RemainingTime")]
    [InlineData("Printing:a@30,Unknown,Printing:a@9", "RemainingTime")]
    [InlineData("Printing@30,Printing@9", "RemainingTime")]
    [InlineData("Printing:a@30,Printing:a@0", "RemainingTime")]
    [InlineData("Printing:a@30,Printing:a@9,Busy:b@30,Printing:b@30,Printing:b@9", "RemainingTime,PrintStarted,RemainingTime")]
    [InlineData("Printing:a@30,Printing:a@9,Attention:b@30,Printing:b@30,Printing:b@9", "RemainingTime,AttentionRequired,PrintStarted,RemainingTime")]
    public void RemainingTimeIsReportedOncePerJobWhenCrossingThreshold(string steps, string expected) => AssertEvents(steps, expected);

    [Theory]
    [InlineData(0, "Printing:a@30,Printing:a@9,Printing:a@0", "")]
    [InlineData(0, "Idle,Printing:a@30,Printing:a@0,Finished:a", "PrintStarted,PrintFinished")]
    [InlineData(5, "Printing:a@30,Printing:a@9", "")]
    [InlineData(5, "Printing:a@30,Printing:a@9,Printing:a@5", "RemainingTime")]
    [InlineData(60, "Printing:a@90,Printing:a@59", "RemainingTime")]
    public void RemainingTimeThresholdIsConfigurable(int thresholdMinutes, string steps, string expected) =>
        AssertEvents(steps, expected, Options with { RemainingTimeThreshold = TimeSpan.FromMinutes(thresholdMinutes) });

    [Theory]
    [InlineData("Idle,Offline,Idle", "PrinterOffline,PrinterOnline")]
    [InlineData("Idle,Offline,Offline,Idle", "PrinterOffline,PrinterOnline")]
    [InlineData("Offline,Idle,Offline", "PrinterOffline")]
    public void ZeroOfflineDelayReportsOfflineImmediately(string steps, string expected) =>
        AssertEvents(steps, expected, Options with { OfflineDelay = TimeSpan.Zero });

    [Fact]
    public void EventsCarryPrinterTimestampAndJobFileName()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Idle, Start));
        DateTimeOffset startedAt = Start + Step;

        PrintEvent started = Assert.Single(detector.Process(Snapshot(PrinterState.Printing, startedAt, Job("a", remainingMinutes: 30))));

        Assert.Equal(new PrintEvent(PrintEventKind.PrintStarted, PrinterId, startedAt, "a.bgcode", null), started);
    }

    [Fact]
    public void RemainingTimeEventCarriesEstimate()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Printing, Start, Job("a", remainingMinutes: 30)));

        PrintEvent remaining = Assert.Single(detector.Process(Snapshot(PrinterState.Printing, Start + Step, Job("a", remainingMinutes: 9.5))));

        Assert.Equal(PrintEventKind.RemainingTime, remaining.Kind);
        Assert.Equal(TimeSpan.FromMinutes(9.5), remaining.Remaining);
        Assert.Equal("a.bgcode", remaining.FileName);
    }

    [Fact]
    public void RemainingTimeUsesExtrapolationWhenPrinterReportsNoEstimate()
    {
        PrintEventDetector detector = new(Options);
        // 50 % after 20 min → 20 min left; 80 % after 36 min → 9 min left.
        detector.Process(Snapshot(PrinterState.Printing, Start, new PrintJob { Id = 1, ProgressPercent = 50, Elapsed = TimeSpan.FromMinutes(20) }));

        PrintEvent remaining = Assert.Single(detector.Process(
            Snapshot(PrinterState.Printing, Start + Step, new PrintJob { Id = 1, ProgressPercent = 80, Elapsed = TimeSpan.FromMinutes(36) })));

        Assert.Equal(PrintEventKind.RemainingTime, remaining.Kind);
        Assert.Equal(TimeSpan.FromMinutes(9), remaining.Remaining);
    }

    [Fact]
    public void EndOfJobUsesLastKnownFileNameWhenSnapshotHasNoJob()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Printing, Start, Job("a")));

        PrintEvent finished = Assert.Single(detector.Process(Snapshot(PrinterState.Finished, Start + Step)));

        Assert.Equal(PrintEventKind.PrintFinished, finished.Kind);
        Assert.Equal("a.bgcode", finished.FileName);
    }

    [Fact]
    public void OfflineEventCarriesFileNameOfInterruptedJob()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Printing, Start, Job("a")));
        detector.Process(Snapshot(PrinterState.Offline, Start + Step));

        PrintEvent offline = Assert.Single(detector.Process(Snapshot(PrinterState.Offline, Start + Step + Options.OfflineDelay)));

        Assert.Equal(new PrintEvent(PrintEventKind.PrinterOffline, PrinterId, Start + Step + Options.OfflineDelay, "a.bgcode", null), offline);
    }

    [Fact]
    public void OfflineEventOfIdlePrinterHasNoFileName()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Printing, Start, Job("a")));
        detector.Process(Snapshot(PrinterState.Finished, Start + Step, Job("a")));
        detector.Process(Snapshot(PrinterState.Offline, Start + (2 * Step)));

        PrintEvent offline = Assert.Single(detector.Process(Snapshot(PrinterState.Offline, Start + (2 * Step) + Options.OfflineDelay)));

        Assert.Null(offline.FileName);
    }

    [Fact]
    public void PrintersAreTrackedIndependently()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Idle, Start, printerId: "a"));
        detector.Process(Snapshot(PrinterState.Printing, Start, Job("x"), printerId: "b"));

        IReadOnlyList<PrintEvent> a = detector.Process(Snapshot(PrinterState.Printing, Start + Step, Job("x"), printerId: "a"));
        IReadOnlyList<PrintEvent> b = detector.Process(Snapshot(PrinterState.Printing, Start + Step, Job("x"), printerId: "b"));

        Assert.Equal(PrintEventKind.PrintStarted, Assert.Single(a).Kind);
        Assert.Equal("a", a[0].PrinterId);
        Assert.Empty(b);
    }

    [Fact]
    public void PrinterIdsAreCaseInsensitive()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Idle, Start, printerId: "MK4"));

        PrintEvent started = Assert.Single(detector.Process(Snapshot(PrinterState.Printing, Start + Step, Job("a"), printerId: "mk4")));

        Assert.Equal("mk4", started.PrinterId);
    }

    [Fact]
    public void ClockGoingBackwardsDoesNotReportOffline()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Idle, Start));
        detector.Process(Snapshot(PrinterState.Offline, Start + TimeSpan.FromHours(1)));

        Assert.Empty(detector.Process(Snapshot(PrinterState.Offline, Start)));
        // The outage is measured from the earlier time, so the jump back does not postpone the report.
        Assert.Empty(detector.Process(Snapshot(PrinterState.Offline, Start + Options.OfflineDelay - TimeSpan.FromTicks(1))));
        PrintEvent offline = Assert.Single(detector.Process(Snapshot(PrinterState.Offline, Start + Options.OfflineDelay)));
        Assert.Equal(PrintEventKind.PrinterOffline, offline.Kind);
    }

    [Fact]
    public void UpdatedOptionsKeepTrackedState()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Printing, Start, Job("a", remainingMinutes: 30)));

        detector.UpdateOptions(Options with { RemainingTimeThreshold = TimeSpan.FromMinutes(5) });

        // Same job, no new start; the new threshold applies.
        Assert.Empty(detector.Process(Snapshot(PrinterState.Printing, Start + Step, Job("a", remainingMinutes: 9))));
        Assert.Equal(PrintEventKind.RemainingTime, Assert.Single(detector.Process(Snapshot(PrinterState.Printing, Start + (2 * Step), Job("a", remainingMinutes: 5)))).Kind);
    }

    [Fact]
    public void RetainPrintersForgetsOthers()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Idle, Start, printerId: "kept"));
        detector.Process(Snapshot(PrinterState.Idle, Start, printerId: "removed"));

        detector.RetainPrinters(static id => id == "kept");

        Assert.Single(detector.Process(Snapshot(PrinterState.Printing, Start + Step, Job("a"), printerId: "kept")));
        // Forgotten: the next snapshot is a new baseline.
        Assert.Empty(detector.Process(Snapshot(PrinterState.Printing, Start + Step, Job("a"), printerId: "removed")));
    }

    [Fact]
    public void RejectedCredentialsEventHasNoFileName()
    {
        PrintEventDetector detector = new(Options);
        detector.Process(Snapshot(PrinterState.Printing, Start, Job("a")));

        PrintEvent rejected = Assert.Single(detector.Process(Snapshot(PrinterState.Unauthorized, Start + Step)));

        Assert.Equal(new PrintEvent(PrintEventKind.CredentialsRejected, PrinterId, Start + Step, null, null), rejected);
    }

    [Fact]
    public void NullSnapshotIsRejected()
    {
        PrintEventDetector detector = new(Options);

        Assert.Throws<ArgumentNullException>(() => detector.Process(null!));
    }

    [Fact]
    public void DefaultOptionsUseTenMinutesAndOneMinuteOfflineDelay()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), PrintEventDetectorOptions.Default.RemainingTimeThreshold);
        Assert.Equal(TimeSpan.FromMinutes(1), PrintEventDetectorOptions.Default.OfflineDelay);
    }

    [Fact]
    public void NegativeOptionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrintEventDetectorOptions { RemainingTimeThreshold = TimeSpan.FromTicks(-1) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PrintEventDetectorOptions { OfflineDelay = TimeSpan.FromTicks(-1) });
    }

    private static void AssertEvents(string steps, string expected, PrintEventDetectorOptions? options = null)
    {
        PrintEventDetector detector = new(options ?? Options);
        List<string> actual = [];
        DateTimeOffset now = Start;
        foreach (string step in steps.Split(','))
        {
            if (step.StartsWith('+'))
            {
                now += TimeSpan.FromSeconds(int.Parse(step.AsSpan(1), CultureInfo.InvariantCulture));
                continue;
            }

            foreach (PrintEvent printEvent in detector.Process(Parse(step, now)))
            {
                actual.Add(printEvent.Kind.ToString());
            }

            now += Step;
        }

        Assert.Equal(expected, string.Join(",", actual));
    }

    private static PrinterSnapshot Parse(string step, DateTimeOffset timestamp)
    {
        string[] stateAndRemaining = step.Split('@');
        double? remainingMinutes = stateAndRemaining.Length > 1 ? double.Parse(stateAndRemaining[1], CultureInfo.InvariantCulture) : null;
        string[] stateAndJob = stateAndRemaining[0].Split(':');
        PrinterState state = stateAndJob[0] == "Undefined" ? (PrinterState)999 : Enum.Parse<PrinterState>(stateAndJob[0]);

        PrintJob? job;
        if (stateAndJob.Length == 1)
        {
            job = remainingMinutes is null ? null : new PrintJob { Remaining = TimeSpan.FromMinutes(remainingMinutes.Value) };
        }
        else if (stateAndJob[1].StartsWith("f=", StringComparison.Ordinal))
        {
            job = Job(stateAndJob[1][2..], remainingMinutes, withId: false);
        }
        else
        {
            job = Job(stateAndJob[1], remainingMinutes);
        }

        return Snapshot(state, timestamp, job);
    }

    internal static PrintJob Job(string name, double? remainingMinutes = null, bool withId = true) => new()
    {
        Id = withId ? name[0] : null,
        FileName = name + ".bgcode",
        ProgressPercent = 50,
        Remaining = remainingMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
    };

    internal static PrinterSnapshot Snapshot(PrinterState state, DateTimeOffset timestamp, PrintJob? job = null, string printerId = PrinterId) =>
        new() { PrinterId = printerId, State = state, Timestamp = timestamp, Job = job };
}
