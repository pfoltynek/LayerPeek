using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Printers;

public class PrinterSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void UnavailableCreatesSnapshotWithoutJobAndTelemetry()
    {
        PrinterSnapshot snapshot = PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, Now, "Connection refused");

        Assert.Equal("mk4", snapshot.PrinterId);
        Assert.Equal(PrinterState.Offline, snapshot.State);
        Assert.Equal(Now, snapshot.Timestamp);
        Assert.Equal("Connection refused", snapshot.StatusMessage);
        Assert.Null(snapshot.Job);
        Assert.Null(snapshot.Telemetry);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void PrinterIdMustNotBeEmpty(string? printerId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PrinterSnapshot
        {
            PrinterId = printerId!,
            State = PrinterState.Idle,
            Timestamp = Now,
        });
    }

    [Fact]
    public void EstimatedCompletionIsTimestampPlusEstimatedRemaining()
    {
        PrinterSnapshot snapshot = new()
        {
            PrinterId = "mk4",
            State = PrinterState.Printing,
            Timestamp = Now,
            Job = new PrintJob { ProgressPercent = 50, Remaining = TimeSpan.FromMinutes(30) },
        };

        Assert.Equal(Now.AddMinutes(30), snapshot.EstimatedCompletion);
    }

    [Theory]
    [InlineData(PrinterState.Stopped)]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Offline)]
    public void EstimatedCompletionIsUnknownWhenTheJobIsNotActive(PrinterState state)
    {
        PrinterSnapshot snapshot = new()
        {
            PrinterId = "mk4",
            State = state,
            Timestamp = Now,
            Job = new PrintJob { ProgressPercent = 50, Elapsed = TimeSpan.FromMinutes(30), Remaining = TimeSpan.FromMinutes(30) },
        };

        Assert.Null(snapshot.EstimatedCompletion);
    }

    [Theory]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    public void EstimatedCompletionIsKnownWhileThePrintIsInterrupted(PrinterState state)
    {
        PrinterSnapshot snapshot = new()
        {
            PrinterId = "mk4",
            State = state,
            Timestamp = Now,
            Job = new PrintJob { Remaining = TimeSpan.FromMinutes(30) },
        };

        Assert.Equal(Now.AddMinutes(30), snapshot.EstimatedCompletion);
    }

    [Fact]
    public void EstimatedCompletionIsUnknownWithoutJob()
    {
        PrinterSnapshot snapshot = new() { PrinterId = "mk4", State = PrinterState.Idle, Timestamp = Now };

        Assert.Null(snapshot.EstimatedCompletion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-5)]
    public void EstimatedCompletionIsUnknownWhenItWouldOverflow(int offsetHours)
    {
        TimeSpan offset = TimeSpan.FromHours(offsetHours);
        PrinterSnapshot snapshot = new()
        {
            PrinterId = "mk4",
            State = PrinterState.Printing,
            Timestamp = new DateTimeOffset(DateTime.MaxValue.AddDays(-1).Ticks - Math.Max(0, offset.Ticks), offset),
            Job = new PrintJob { Remaining = TimeSpan.FromDays(2) },
        };

        Assert.Null(snapshot.EstimatedCompletion);
    }

    [Fact]
    public void EstimatedCompletionWithHugeExtrapolationAndLocalOffsetDoesNotThrow()
    {
        PrinterSnapshot snapshot = new()
        {
            PrinterId = "mk4",
            State = PrinterState.Printing,
            Timestamp = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(2)),
            Job = new PrintJob { ProgressPercent = 1e-12, Elapsed = TimeSpan.FromDays(1) },
        };

        Assert.Null(snapshot.EstimatedCompletion);
    }

    [Fact]
    public void EstimatedCompletionRightAtTheLimitIsReturned()
    {
        DateTimeOffset timestamp = new(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(2));
        TimeSpan remaining = TimeSpan.FromTicks(DateTime.MaxValue.Ticks - timestamp.Ticks);
        PrinterSnapshot snapshot = new()
        {
            PrinterId = "mk4",
            State = PrinterState.Printing,
            Timestamp = timestamp,
            Job = new PrintJob { Remaining = remaining },
        };

        Assert.Equal(timestamp + remaining, snapshot.EstimatedCompletion);
    }

    [Fact]
    public void EqualityIsValueBased()
    {
        PrinterSnapshot first = new()
        {
            PrinterId = "mk4",
            State = PrinterState.Printing,
            Timestamp = Now,
            Job = new PrintJob { FileName = "a.bgcode", ProgressPercent = 12 },
            Telemetry = new Telemetry { Nozzle = new Temperature(215, 215), AxisZ = 1.2 },
        };
        PrinterSnapshot second = first with { Job = first.Job! with { }, Telemetry = first.Telemetry! with { } };

        Assert.Equal(first, second);
    }
}
