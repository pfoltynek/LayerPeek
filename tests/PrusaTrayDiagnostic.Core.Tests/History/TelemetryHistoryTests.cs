using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.History;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Tests.Monitoring;

namespace PrusaTrayDiagnostic.Core.Tests.History;

public sealed class TelemetryHistoryTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private const int SamplesPerPrinter = 20_000;

    private static readonly PrinterDefinition Mk4 = Definition("mk4");
    private static readonly PrinterDefinition Xl = Definition("xl");

    // ---- Sample content ----

    [Fact]
    public void SampleCarriesEveryTelemetryValue()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        PrinterSnapshot snapshot = new()
        {
            PrinterId = "mk4",
            State = PrinterState.Printing,
            Timestamp = Start,
            Job = new PrintJob { ProgressPercent = 42.5 },
            Telemetry = new Telemetry
            {
                Nozzle = new Temperature(214.6, 215),
                Bed = new Temperature(59.9, 60),
                HotendFanRpm = 5900,
                PrintFanRpm = 3100,
                AxisZ = 12.35,
                SpeedPercent = 120,
                FlowPercent = 95,
            },
        };

        history.Record(snapshot);

        TelemetrySample sample = Assert.Single(ReadAll(history, "mk4"));
        Assert.Equal(Start, sample.Timestamp);
        Assert.Equal(Start.UtcTicks, sample.TimestampUtcTicks);
        Assert.Equal(PrinterState.Printing, sample.State);
        Assert.True(sample.HasTelemetry);
        Assert.Equal(214.6f, sample.NozzleActual);
        Assert.Equal(215f, sample.NozzleTarget);
        Assert.Equal(59.9f, sample.BedActual);
        Assert.Equal(60f, sample.BedTarget);
        Assert.Equal(5900f, sample.HotendFanRpm);
        Assert.Equal(3100f, sample.PrintFanRpm);
        Assert.Equal(12.35f, sample.AxisZ);
        Assert.Equal(120f, sample.SpeedPercent);
        Assert.Equal(95f, sample.FlowPercent);
        Assert.Equal(42.5f, sample.ProgressPercent);
    }

    [Fact]
    public void TimestampWithOffsetIsStoredAsUtc()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        DateTimeOffset local = new(2026, 10, 16, 14, 0, 0, TimeSpan.FromHours(2));

        history.Record(Snapshot(local, 1));

        TelemetrySample sample = Assert.Single(ReadAll(history, "mk4"));
        Assert.Equal(TimeSpan.Zero, sample.Timestamp.Offset);
        Assert.Equal(local, sample.Timestamp);
    }

    [Fact]
    public void ValuesTheSourceDoesNotProvideAreNaN()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);

        history.Record(new PrinterSnapshot
        {
            PrinterId = "mk4",
            State = PrinterState.Idle,
            Timestamp = Start,
            Telemetry = new Telemetry { Nozzle = new Temperature(25, null) },
        });

        TelemetrySample sample = Assert.Single(ReadAll(history, "mk4"));
        Assert.True(sample.HasTelemetry);
        Assert.Equal(25f, sample.NozzleActual);
        Assert.True(float.IsNaN(sample.NozzleTarget));
        Assert.True(float.IsNaN(sample.BedActual));
        Assert.True(float.IsNaN(sample.BedTarget));
        Assert.True(float.IsNaN(sample.HotendFanRpm));
        Assert.True(float.IsNaN(sample.PrintFanRpm));
        Assert.True(float.IsNaN(sample.AxisZ));
        Assert.True(float.IsNaN(sample.SpeedPercent));
        Assert.True(float.IsNaN(sample.FlowPercent));
        // No job: progress is unknown, not zero.
        Assert.True(float.IsNaN(sample.ProgressPercent));
    }

    [Fact]
    public void ValuesTheStorageTypeCannotRepresentAreNaN()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);

        history.Record(new PrinterSnapshot
        {
            PrinterId = "mk4",
            State = PrinterState.Printing,
            Timestamp = Start,
            Telemetry = new Telemetry
            {
                Nozzle = new Temperature(double.PositiveInfinity, 1e300),
                Bed = new Temperature(-1e40, double.NegativeInfinity),
                HotendFanRpm = int.MaxValue,
                AxisZ = 1e39,
                // Half.MaxValue is 65504.
                SpeedPercent = 70_000,
                FlowPercent = int.MinValue,
            },
        });

        TelemetrySample sample = Assert.Single(ReadAll(history, "mk4"));
        Assert.True(float.IsNaN(sample.NozzleActual));
        Assert.True(float.IsNaN(sample.NozzleTarget));
        Assert.True(float.IsNaN(sample.BedActual));
        Assert.True(float.IsNaN(sample.BedTarget));
        Assert.True(float.IsNaN(sample.AxisZ));
        Assert.True(float.IsNaN(sample.SpeedPercent));
        Assert.True(float.IsNaN(sample.FlowPercent));
        // Large but finite in float: kept (rounded).
        Assert.Equal(int.MaxValue, sample.HotendFanRpm);
    }

    [Fact]
    public void LargestRepresentableHalfValueIsKept()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);

        history.Record(Snapshot(Start, 1) with { Telemetry = new Telemetry { SpeedPercent = 65_504, FlowPercent = 999 } });

        TelemetrySample sample = Assert.Single(ReadAll(history, "mk4"));
        Assert.Equal(65_504f, sample.SpeedPercent);
        Assert.Equal(999f, sample.FlowPercent);
    }

    [Theory]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unknown)]
    [InlineData(PrinterState.Unauthorized)]
    public void UnavailableSnapshotIsStoredAsGapSample(PrinterState state)
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        history.Record(Snapshot(Start, 200));

        history.Record(PrinterSnapshot.Unavailable("mk4", state, Start + Second, "unreachable"));

        TelemetrySample[] samples = ReadAll(history, "mk4");
        Assert.Equal(2, samples.Length);
        TelemetrySample gap = samples[1];
        Assert.Equal(state, gap.State);
        Assert.False(gap.HasTelemetry);
        Assert.True(float.IsNaN(gap.NozzleActual));
        Assert.True(float.IsNaN(gap.BedActual));
        Assert.True(float.IsNaN(gap.AxisZ));
    }

    [Fact]
    public void UndefinedStateIsStoredAsUnknown()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);

        history.Record(Snapshot(Start, 1) with { State = (PrinterState)1000 });

        Assert.Equal(PrinterState.Unknown, Assert.Single(ReadAll(history, "mk4")).State);
    }

    [Fact]
    public void SampleIsCompactValueType()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<TelemetrySample>());
        Assert.Equal(48, Unsafe.SizeOf<TelemetrySample>());
    }

    // ---- Capacity ----

    [Theory]
    [InlineData(86_400, 1_000, 86_401)]
    [InlineData(86_400, 5_000, 17_281)]
    [InlineData(86_400, 7_000, 12_344)]
    [InlineData(86_400, 300_000, 289)]
    // Below the minimum sample interval the capacity is bounded as for 1 s.
    [InlineData(86_400, 1, 86_401)]
    [InlineData(10, 1_000, 11)]
    // Interval longer than the retention still keeps the newest two samples.
    [InlineData(10, 60_000, 2)]
    public void CapacityIsDerivedFromRetentionAndInterval(int retentionSeconds, int intervalMilliseconds, int expected)
    {
        TimeSpan retention = TimeSpan.FromSeconds(retentionSeconds);
        TimeSpan interval = TimeSpan.FromMilliseconds(intervalMilliseconds);

        Assert.Equal(expected, TelemetryHistory.CapacityFor(retention, interval));

        TelemetryHistory history = Create(retention, interval, Mk4);
        Assert.Equal(expected, history.GetCapacity("mk4"));
    }

    [Fact]
    public void DefaultRetentionIsOneDay()
    {
        TelemetryHistory history = new();

        Assert.Equal(TimeSpan.FromDays(1), history.Retention);
        Assert.Equal(TimeSpan.FromDays(1), TelemetryHistoryOptions.Default.Retention);
    }

    [Fact]
    public void UnknownPrinterHasNoCapacity()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);

        Assert.Equal(0, history.GetCapacity("xl"));
    }

    [Fact]
    public void FullBufferOverwritesOldestSamplesAndKeepsOrder()
    {
        // Capacity 11; samples arrive faster than the interval, so the capacity (not the retention) limits them.
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);

        for (int i = 0; i < 25; i++)
        {
            history.Record(Snapshot(Start + (i * TimeSpan.FromMilliseconds(100)), i));
        }

        AssertNozzles(ReadAll(history, "mk4"), 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24);
    }

    [Fact]
    public void WrapAroundManyTimesKeepsNewestSamplesInOrder()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);

        for (int i = 0; i < 1000; i++)
        {
            history.Record(Snapshot(Start + (i * TimeSpan.FromMilliseconds(1)), i));
        }

        AssertNozzles(ReadAll(history, "mk4"), 989, 990, 991, 992, 993, 994, 995, 996, 997, 998, 999);
    }

    [Fact]
    public void MemoryGrowsWithSamplesNotWithCapacity()
    {
        // A one-day history at 1 s holds up to ~4 MB; a fresh printer must not allocate it up front.
        TelemetryHistory history = new();
        long before = GC.GetAllocatedBytesForCurrentThread();

        history.Configure([Mk4], Second);
        history.Record(Snapshot(Start, 1));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 64 * 1024);
    }

    [Fact]
    public void GrowingBufferKeepsAllSamplesInOrder()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);

        for (int i = 0; i < 3601; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        TelemetrySample[] samples = ReadAll(history, "mk4");
        Assert.Equal(3601, samples.Length);
        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(i, samples[i].NozzleActual);
        }
    }

    // ---- Time-based retention ----

    [Fact]
    public void SamplesOlderThanRetentionAreDroppedBeforeCapacityIsReached()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);

        for (int i = 0; i <= 30; i += 2)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        // Newest is 30 s; a sample exactly at the retention boundary (20 s) is kept.
        AssertNozzles(ReadAll(history, "mk4"), 20, 22, 24, 26, 28, 30);
    }

    [Fact]
    public void LongOutageDropsEverythingButTheNewSample()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        for (int i = 0; i < 5; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        history.Record(Snapshot(Start + TimeSpan.FromHours(2), 99));

        AssertNozzles(ReadAll(history, "mk4"), 99);
    }

    // ---- Interval change ----

    [Fact]
    public void ShorterCapacityAfterIntervalChangeKeepsNewestSamples()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        for (int i = 0; i <= 10; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        history.Configure([Mk4], TimeSpan.FromSeconds(5));

        Assert.Equal(3, history.GetCapacity("mk4"));
        AssertNozzles(ReadAll(history, "mk4"), 8, 9, 10);

        history.Record(Snapshot(Start + (15 * Second), 15));
        AssertNozzles(ReadAll(history, "mk4"), 9, 10, 15);
    }

    [Fact]
    public void LongerCapacityAfterIntervalChangeKeepsAllSamples()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), Mk4);
        for (int i = 0; i < 6; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        AssertNozzles(ReadAll(history, "mk4"), 3, 4, 5);

        history.Configure([Mk4], Second);

        Assert.Equal(11, history.GetCapacity("mk4"));
        AssertNozzles(ReadAll(history, "mk4"), 3, 4, 5);
        for (int i = 6; i <= 13; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        AssertNozzles(ReadAll(history, "mk4"), 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
    }

    [Fact]
    public void ResizeOfWrappedBufferPreservesOrder()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        // 17 samples into capacity 11: the ring has wrapped.
        for (int i = 0; i < 17; i++)
        {
            history.Record(Snapshot(Start + (i * TimeSpan.FromMilliseconds(100)), i));
        }

        history.Configure([Mk4], TimeSpan.FromSeconds(2));

        Assert.Equal(6, history.GetCapacity("mk4"));
        AssertNozzles(ReadAll(history, "mk4"), 11, 12, 13, 14, 15, 16);
    }

    // ---- Printer add / remove ----

    [Fact]
    public void RemovedPrinterHistoryIsDropped()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4, Xl);
        history.Record(Snapshot(Start, 1, "mk4"));
        history.Record(Snapshot(Start, 2, "xl"));

        history.Configure([Mk4], Second);
        history.Record(Snapshot(Start + Second, 3, "xl"));

        Assert.Empty(ReadAll(history, "xl"));
        Assert.Equal(0, history.GetVersion("xl"));
        Assert.Equal(0, history.GetCapacity("xl"));
        AssertNozzles(ReadAll(history, "mk4"), 1);
    }

    [Fact]
    public void ReAddedPrinterStartsEmptyWithNewerVersion()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        history.Record(Snapshot(Start, 1));
        long versionBefore = history.GetVersion("mk4");

        history.Configure([], Second);
        history.Configure([Mk4], Second);

        Assert.Empty(ReadAll(history, "mk4"));
        Assert.True(history.GetVersion("mk4") > versionBefore);
    }

    [Fact]
    public void AddedPrinterGetsHistoryOthersAreKept()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        history.Record(Snapshot(Start, 1, "mk4"));

        history.Configure([Mk4, Xl], Second);
        history.Record(Snapshot(Start + Second, 2, "xl"));

        AssertNozzles(ReadAll(history, "mk4"), 1);
        AssertNozzles(ReadAll(history, "xl"), 2);
    }

    [Fact]
    public void RenamedPrinterKeepsHistoryAndIdsIgnoreCase()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        history.Record(Snapshot(Start, 1, "MK4"));

        history.Configure([Mk4 with { Id = "Mk4", Name = "Renamed" }], Second);

        AssertNozzles(ReadAll(history, "mk4"), 1);
    }

    [Theory]
    [InlineData("192.168.1.99", ConnectionType.PrusaLink)]
    [InlineData("192.168.1.10", ConnectionType.PrusaConnect)]
    public void PrinterPointingToAnotherMachineStartsNewHistory(string address, ConnectionType connectionType)
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        history.Record(Snapshot(Start, 1));

        history.Configure([Mk4 with { Address = address, ConnectionType = connectionType }], Second);

        Assert.Empty(ReadAll(history, "mk4"));
    }

    [Fact]
    public void SnapshotsBeforeConfigureAndOfUnknownPrintersAreIgnored()
    {
        TelemetryHistory history = new(new TelemetryHistoryOptions { Retention = TimeSpan.FromHours(1) });
        history.Record(Snapshot(Start, 1));

        history.Configure([Mk4], Second);
        history.Record(Snapshot(Start + Second, 2, "xl"));

        Assert.Empty(ReadAll(history, "mk4"));
        Assert.Empty(ReadAll(history, "xl"));
        Assert.Equal(0, history.CountSamples("xl", DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
    }

    // ---- Range queries ----

    [Theory]
    // Entirely before / after the samples.
    [InlineData(-10_000, -1, -1, 0)]
    [InlineData(10_000, 20_000, -1, 0)]
    // Boundaries are inclusive.
    [InlineData(3_000, 5_000, 3, 3)]
    [InlineData(0, 0, 0, 1)]
    [InlineData(9_000, 9_000, 9, 1)]
    // Between samples.
    [InlineData(2_500, 4_500, 3, 2)]
    [InlineData(2_100, 2_900, -1, 0)]
    // Partly outside.
    [InlineData(-5_000, 1_000, 0, 2)]
    [InlineData(8_000, 50_000, 8, 2)]
    // Everything.
    [InlineData(-5_000, 50_000, 0, 10)]
    // Reversed range.
    [InlineData(5_000, 3_000, -1, 0)]
    public void RangeQueryReturnsSamplesWithinInclusiveBounds(int fromMilliseconds, int toMilliseconds, int expectedFirst, int expectedCount)
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        for (int i = 0; i < 10; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        DateTimeOffset from = Start + TimeSpan.FromMilliseconds(fromMilliseconds);
        DateTimeOffset to = Start + TimeSpan.FromMilliseconds(toMilliseconds);
        TelemetrySample[] destination = new TelemetrySample[20];

        int count = history.CountSamples("mk4", from, to);
        int copied = history.CopySamples("mk4", from, to, destination, out _);

        Assert.Equal(expectedCount, count);
        Assert.Equal(expectedCount, copied);
        for (int i = 0; i < copied; i++)
        {
            Assert.Equal(expectedFirst + i, destination[i].NozzleActual);
        }
    }

    [Fact]
    public void RangeQueryWorksOnWrappedBuffer()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        for (int i = 0; i < 17; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        TelemetrySample[] destination = new TelemetrySample[20];
        int copied = history.CopySamples("mk4", Start + (8 * Second), Start + (12 * Second), destination, out _);

        AssertNozzles(destination.AsSpan(0, copied).ToArray(), 8, 9, 10, 11, 12);
    }

    [Fact]
    public void SmallDestinationReceivesNewestSamplesOfRange()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        for (int i = 0; i < 10; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        TelemetrySample[] destination = new TelemetrySample[3];
        int copied = history.CopySamples("mk4", Start, Start + (8 * Second), destination, out _);

        Assert.Equal(3, copied);
        AssertNozzles(destination, 6, 7, 8);
    }

    [Fact]
    public void EmptyDestinationCopiesNothing()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        history.Record(Snapshot(Start, 1));

        Assert.Equal(0, history.CopySamples("mk4", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, [], out long version));
        Assert.Equal(history.GetVersion("mk4"), version);
    }

    [Fact]
    public void EmptyHistoryReturnsNothing()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);

        Assert.Empty(ReadAll(history, "mk4"));
    }

    // ---- Gaps / segments ----

    [Fact]
    public void FirstSampleStartsSegmentRegularSamplesDoNot()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        for (int i = 0; i < 3; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        TelemetrySample[] samples = ReadAll(history, "mk4");
        Assert.True(samples[0].StartsSegment);
        Assert.False(samples[1].StartsSegment);
        Assert.False(samples[2].StartsSegment);
    }

    [Theory]
    // The gap threshold is three intervals, but at least 30 s (slow polls must not split the line).
    [InlineData(1, 30, false)]
    [InlineData(1, 31, true)]
    [InlineData(20, 60, false)]
    [InlineData(20, 61, true)]
    public void PauseLongerThanGapThresholdStartsSegment(int intervalSeconds, int pauseSeconds, bool expected)
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), TimeSpan.FromSeconds(intervalSeconds), Mk4);
        history.Record(Snapshot(Start, 1));

        history.Record(Snapshot(Start + TimeSpan.FromSeconds(pauseSeconds), 2));

        Assert.Equal(expected, ReadAll(history, "mk4")[1].StartsSegment);
    }

    [Fact]
    public void FirstSampleAfterRetentionDroppedEverythingStartsSegment()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        history.Record(Snapshot(Start, 1));
        history.Record(Snapshot(Start + Second, 2));

        history.Record(Snapshot(Start + TimeSpan.FromSeconds(20), 3));

        Assert.True(Assert.Single(ReadAll(history, "mk4")).StartsSegment);
    }

    // ---- Clock going backwards ----

    [Fact]
    public void ClockGoingBackwardsDropsSamplesFromTheFuture()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        for (int i = 0; i < 10; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        history.Record(Snapshot(Start + TimeSpan.FromMilliseconds(4_500), 100));

        TelemetrySample[] samples = ReadAll(history, "mk4");
        AssertNozzles(samples, 0, 1, 2, 3, 4, 100);
        Assert.False(samples[^1].StartsSegment);
    }

    [Fact]
    public void SampleWithSameTimestampReplacesNewest()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        history.Record(Snapshot(Start, 1));
        history.Record(Snapshot(Start + Second, 2));

        history.Record(Snapshot(Start + Second, 3));

        AssertNozzles(ReadAll(history, "mk4"), 1, 3);
    }

    [Fact]
    public void ClockGoingBeforeAllSamplesStartsOver()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        for (int i = 0; i < 5; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        history.Record(Snapshot(Start - TimeSpan.FromHours(3), 7));
        history.Record(Snapshot(Start - TimeSpan.FromHours(3) + Second, 8));

        TelemetrySample[] samples = ReadAll(history, "mk4");
        AssertNozzles(samples, 7, 8);
        Assert.True(samples[0].StartsSegment);
    }

    [Fact]
    public void ClockGoingBackwardsOnWrappedBufferKeepsOrder()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        for (int i = 0; i < 17; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        history.Record(Snapshot(Start + TimeSpan.FromMilliseconds(12_500), 100));

        AssertNozzles(ReadAll(history, "mk4"), 6, 7, 8, 9, 10, 11, 12, 100);
    }

    // ---- Versions and change notification ----

    [Fact]
    public void VersionChangesWithEveryRecordedSample()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4, Xl);
        long initial = history.GetVersion("mk4");

        history.Record(Snapshot(Start, 1));
        long afterFirst = history.GetVersion("mk4");
        history.Record(Snapshot(Start + Second, 2));
        long afterSecond = history.GetVersion("mk4");

        Assert.True(initial > 0);
        Assert.True(afterFirst > initial);
        Assert.True(afterSecond > afterFirst);
        TelemetrySample[] destination = new TelemetrySample[4];
        history.CopySamples("mk4", DateTimeOffset.MinValue, DateTimeOffset.MaxValue, destination, out long copiedVersion);
        Assert.Equal(afterSecond, copiedVersion);
    }

    [Fact]
    public void VersionOfOtherPrinterIsUnaffected()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4, Xl);
        long xlVersion = history.GetVersion("xl");

        history.Record(Snapshot(Start, 1, "mk4"));

        Assert.Equal(xlVersion, history.GetVersion("xl"));
    }

    [Fact]
    public void VersionChangesWhenResizeDropsSamples()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        for (int i = 0; i <= 10; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        long before = history.GetVersion("mk4");
        history.Configure([Mk4], TimeSpan.FromSeconds(5));

        Assert.True(history.GetVersion("mk4") > before);
    }

    [Fact]
    public void UnchangedConfigurationKeepsVersion()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        history.Record(Snapshot(Start, 1));
        long before = history.GetVersion("mk4");

        history.Configure([Mk4], Second);

        Assert.Equal(before, history.GetVersion("mk4"));
    }

    [Fact]
    public void ChangedIsRaisedForRecordedPrinter()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4, Xl);
        List<string> changed = [];
        history.Changed += (sender, printerId) =>
        {
            Assert.Same(history, sender);
            changed.Add(printerId);
        };

        history.Record(Snapshot(Start, 1, "xl"));
        history.Record(Snapshot(Start, 1, "mk4"));

        string[] expected = ["xl", "mk4"];
        Assert.Equal(expected, changed);
    }

    [Fact]
    public void ChangedIsRaisedForRemovedReplacedAndAddedPrinters()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4, Xl);
        List<string> changed = [];
        history.Changed += (_, printerId) => changed.Add(printerId);
        PrinterDefinition mini = Definition("mini");

        history.Configure([Mk4 with { Address = "10.0.0.1" }, mini], Second);

        string[] expected = ["mini", "mk4", "xl"];
        Assert.Equal(expected, changed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ChangedIsNotRaisedForIgnoredSnapshots()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        int raised = 0;
        history.Changed += (_, _) => raised++;

        history.Record(Snapshot(Start, 1, "xl"));

        Assert.Equal(0, raised);
    }

    [Fact]
    public void CapacityChangeBumpsVersionAndRaisesChanged()
    {
        // A chart sizing its buffer with GetCapacity must learn that the capacity grew, even though no sample was dropped.
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), Mk4);
        history.Record(Snapshot(Start, 1));
        long before = history.GetVersion("mk4");
        List<string> changed = [];
        history.Changed += (_, printerId) => changed.Add(printerId);

        history.Configure([Mk4], Second);

        Assert.Equal(11, history.GetCapacity("mk4"));
        Assert.True(history.GetVersion("mk4") > before);
        Assert.Equal("mk4", Assert.Single(changed));
        AssertNozzles(ReadAll(history, "mk4"), 1);
    }

    [Fact]
    public void ThrowingChangedHandlerIsLoggedAndOtherHandlersStillRun()
    {
        ListLogger<TelemetryHistory> logger = new();
        TelemetryHistory history = new(new TelemetryHistoryOptions { Retention = TimeSpan.FromHours(1) }, logger);
        List<string> received = [];
        history.Changed += static (_, _) => throw new InvalidOperationException("handler failed");
        history.Changed += (_, printerId) => received.Add(printerId);

        history.Configure([Mk4], Second);
        history.Record(Snapshot(Start, 1));

        string[] expected = ["mk4", "mk4"];
        Assert.Equal(expected, received);
        Assert.Equal(2, logger.Entries.Count(entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException));
        AssertNozzles(ReadAll(history, "mk4"), 1);
    }

    [Fact]
    public void RemovedChangedHandlerIsNotCalled()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        int raised = 0;
        void Handler(object? sender, string printerId) => raised++;
        history.Changed += Handler;
        history.Changed -= Handler;
        history.Changed -= Handler;

        history.Record(Snapshot(Start, 1));

        Assert.Equal(0, raised);
    }

    // ---- Allocation ----

    [Fact]
    public void RecordDoesNotAllocateInSteadyState()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        history.Changed += static (_, _) => { };
        PrinterSnapshot[] snapshots = new PrinterSnapshot[64];
        for (int i = 0; i < snapshots.Length; i++)
        {
            snapshots[i] = Snapshot(Start + (i * Second), i);
        }

        // Fill and wrap the buffer first (growth allocates).
        for (int i = 0; i < 32; i++)
        {
            history.Record(snapshots[i]);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 32; i < snapshots.Length; i++)
        {
            history.Record(snapshots[i]);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void ReadingDoesNotAllocate()
    {
        TelemetryHistory history = Create(TimeSpan.FromSeconds(10), Second, Mk4);
        for (int i = 0; i < 20; i++)
        {
            history.Record(Snapshot(Start + (i * Second), i));
        }

        TelemetrySample[] destination = new TelemetrySample[16];
        history.CopySamples("mk4", Start, Start + TimeSpan.FromMinutes(1), destination, out _);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = history.CountSamples("mk4", Start, Start + TimeSpan.FromMinutes(1));
        int copied = history.CopySamples("mk4", Start, Start + TimeSpan.FromMinutes(1), destination, out _);
        long version = history.GetVersion("mk4");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.Equal(11, count);
        Assert.Equal(11, copied);
        Assert.True(version > 0);
    }

    // ---- Concurrency ----

    [Fact]
    public async Task ConcurrentReadersSeeConsistentOrderedSnapshots()
    {
        // Capacity 3601 and samples 10 ms apart: the readers race with growth and many wrap-arounds.
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4, Xl);
        PrinterSnapshot[] mk4 = Snapshots("mk4", SamplesPerPrinter);
        PrinterSnapshot[] xl = Snapshots("xl", SamplesPerPrinter);

        int[] reads = await ReadWhileAsync(
            stop => [Writer(history, mk4, stop), Writer(history, xl, stop)],
            stop => Reader(history, "mk4", stop),
            stop => Reader(history, "mk4", stop),
            stop => Reader(history, "xl", stop));

        Assert.All(reads, count => Assert.True(count > 0));
        TelemetrySample[] final = ReadAll(history, "mk4");
        Assert.Equal(3601, final.Length);
        Assert.Equal(SamplesPerPrinter - 1, final[^1].NozzleActual);
        AssertConsistent(final);
    }

    [Fact]
    public async Task ReconfiguringWhileRecordingAndReadingKeepsSamplesConsistent()
    {
        // Capacity alternates between 3601 (1 s) and 721 (5 s); the xl printer alternates its address, so its history
        // is replaced on every Configure. Readers must still only ever see whole, contiguous, ordered samples.
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4, Xl);
        PrinterDefinition movedXl = Xl with { Address = "10.0.0.2" };
        PrinterSnapshot[] mk4 = Snapshots("mk4", SamplesPerPrinter);
        PrinterSnapshot[] xl = Snapshots("xl", SamplesPerPrinter);
        int configurations = 0;
        // All three workers start together, so the writers cannot finish before the configurator runs.
        using Barrier start = new(3);

        int[] reads = await ReadWhileAsync(
            stop =>
            {
                Task[] writers = [Writer(history, mk4, stop, start), Writer(history, xl, stop, start)];
                Task configurator = Task.Run(
                    () =>
                    {
                        start.SignalAndWait(stop);
                        do
                        {
                            bool odd = (configurations & 1) != 0;
                            history.Configure([Mk4, odd ? movedXl : Xl], odd ? TimeSpan.FromSeconds(5) : Second);
                            configurations++;
                        }
                        while (!stop.IsCancellationRequested && !Array.TrueForAll(writers, static writer => writer.IsCompleted));
                    },
                    stop);
                return [.. writers, configurator];
            },
            stop => Reader(history, "mk4", stop),
            stop => Reader(history, "xl", stop),
            stop => Reader(history, "xl", stop));

        Assert.All(reads, count => Assert.True(count > 0));
        Assert.True(configurations > 0);
        TelemetrySample[] final = ReadAll(history, "mk4");
        Assert.NotEmpty(final);
        Assert.Equal(SamplesPerPrinter - 1, final[^1].NozzleActual);
        AssertConsistent(final);
        AssertConsistent(ReadAll(history, "xl"));
    }

    // ---- Validation ----

    [Fact]
    public void InvalidArgumentsAreRejected()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);

        Assert.Throws<ArgumentNullException>(() => history.Configure(null!, Second));
        Assert.Throws<ArgumentException>(() => history.Configure([null!], Second));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.Configure([Mk4], TimeSpan.Zero));
        Assert.Throws<ArgumentNullException>(() => history.Record(null!));
        Assert.Throws<ArgumentNullException>(() => history.GetVersion(null!));
        Assert.Throws<ArgumentNullException>(() => history.GetCapacity(null!));
        Assert.Throws<ArgumentNullException>(() => history.CountSamples(null!, Start, Start));
        Assert.Throws<ArgumentNullException>(() => history.CopySamples(null!, Start, Start, [], out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => TelemetryHistory.CapacityFor(TimeSpan.FromHours(1), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => TelemetryHistory.GapThreshold(TimeSpan.Zero));
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(5, 30)]
    [InlineData(20, 60)]
    [InlineData(300, 900)]
    public void GapThresholdIsThreeIntervalsButAtLeastThirtySeconds(int intervalSeconds, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), TelemetryHistory.GapThreshold(TimeSpan.FromSeconds(intervalSeconds)));
        Assert.Equal(TimeSpan.MaxValue, TelemetryHistory.GapThreshold(TimeSpan.MaxValue));
    }

    [Fact]
    public void FailedConfigureKeepsPreviousConfiguration()
    {
        TelemetryHistory history = Create(TimeSpan.FromHours(1), Second, Mk4);
        history.Record(Snapshot(Start, 1));

        Assert.Throws<ArgumentException>(() => history.Configure([Xl, null!], Second));

        AssertNozzles(ReadAll(history, "mk4"), 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86_401)]
    public void RetentionOutsideRangeIsRejected(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryHistoryOptions { Retention = TimeSpan.FromSeconds(seconds) });
    }

    [Fact]
    public void NullOptionsUseDefaults()
    {
        Assert.Equal(TimeSpan.FromDays(1), new TelemetryHistory(null).Retention);
    }

    // ---- Helpers ----

    /// <summary>
    /// Runs <paramref name="readers"/> until the workers from <paramref name="startWorkers"/> complete. Whatever happens
    /// (a worker fails, times out, the test is cancelled) every task is told to stop and awaited, so no thread keeps
    /// spinning after the test.
    /// </summary>
    private static async Task<int[]> ReadWhileAsync(Func<CancellationToken, Task[]> startWorkers, params Func<CancellationToken, int>[] readers)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(30);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<int>[] readerTasks = Array.ConvertAll(readers, reader => Task.Run(() => reader(stop.Token), stop.Token));
        Task[] workers = [];
        try
        {
            workers = startWorkers(stop.Token);
            await Task.WhenAll(workers).WaitAsync(timeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            await stop.CancelAsync();
            // Only waits for the tasks to end; their failures are reported by the awaits below or the original exception.
            Task all = Task.WhenAll([.. workers, .. readerTasks]);
            try
            {
                await all.WaitAsync(timeout, CancellationToken.None);
            }
            catch (Exception)
            {
                // Swallowed on purpose: see the comment above.
            }
        }

        return await Task.WhenAll(readerTasks);
    }

    private static Task Writer(TelemetryHistory history, PrinterSnapshot[] snapshots, CancellationToken stop, Barrier? start = null) => Task.Run(
        () =>
        {
            start?.SignalAndWait(stop);
            foreach (PrinterSnapshot snapshot in snapshots)
            {
                if (stop.IsCancellationRequested)
                {
                    return;
                }

                history.Record(snapshot);
            }
        },
        stop);

    /// <summary>Copies the whole history in a loop until stopped (at least once) and checks every copy.</summary>
    private static int Reader(TelemetryHistory history, string printerId, CancellationToken stop)
    {
        // Large enough for the biggest capacity used by the concurrency tests.
        TelemetrySample[] destination = new TelemetrySample[TelemetryHistory.CapacityFor(TimeSpan.FromHours(1), Second)];
        int reads = 0;
        long lastVersion = 0;
        while (!stop.IsCancellationRequested || reads == 0)
        {
            int copied = history.CopySamples(printerId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, destination, out long version);
            Assert.True(version >= lastVersion, "Versions must not go back.");
            lastVersion = version;
            AssertConsistent(destination.AsSpan(0, copied));
            reads++;
        }

        return reads;
    }

    private static PrinterDefinition Definition(string id) =>
        new() { Id = id, Name = id.ToUpperInvariant(), ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.10" };

    private static TelemetryHistory Create(TimeSpan retention, TimeSpan interval, params PrinterDefinition[] printers)
    {
        TelemetryHistory history = new(new TelemetryHistoryOptions { Retention = retention });
        history.Configure(printers, interval);
        return history;
    }

    /// <summary>Snapshot whose nozzle temperature identifies it.</summary>
    private static PrinterSnapshot Snapshot(DateTimeOffset timestamp, double marker, string printerId = "mk4") =>
        new()
        {
            PrinterId = printerId,
            State = PrinterState.Printing,
            Timestamp = timestamp,
            Telemetry = new Telemetry { Nozzle = new Temperature(marker, 215) },
        };

    /// <summary>Snapshots 10 ms apart whose every value encodes the index, so torn samples are detectable.</summary>
    private static PrinterSnapshot[] Snapshots(string printerId, int count)
    {
        PrinterSnapshot[] snapshots = new PrinterSnapshot[count];
        for (int i = 0; i < count; i++)
        {
            snapshots[i] = new PrinterSnapshot
            {
                PrinterId = printerId,
                State = PrinterState.Printing,
                Timestamp = Start + (i * TimeSpan.FromMilliseconds(10)),
                Telemetry = new Telemetry
                {
                    Nozzle = new Temperature(i, i),
                    Bed = new Temperature(i, i),
                    HotendFanRpm = i,
                    PrintFanRpm = i,
                    AxisZ = i,
                },
            };
        }

        return snapshots;
    }

    private static void AssertConsistent(ReadOnlySpan<TelemetrySample> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            TelemetrySample sample = samples[i];
            float index = sample.NozzleActual;
            long expectedTicks = (Start + (index * TimeSpan.FromMilliseconds(10))).UtcTicks;
            bool consistent = sample.TimestampUtcTicks == expectedTicks
                && sample.NozzleTarget == index
                && sample.BedActual == index
                && sample.BedTarget == index
                && sample.HotendFanRpm == index
                && sample.PrintFanRpm == index
                && sample.AxisZ == index
                && (i == 0 || samples[i - 1].NozzleActual == index - 1);
            Assert.True(consistent, $"Sample {i} is torn or out of order: {sample}");
        }
    }

    private static TelemetrySample[] ReadAll(TelemetryHistory history, string printerId)
    {
        int count = history.CountSamples(printerId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue);
        TelemetrySample[] samples = new TelemetrySample[count];
        int copied = history.CopySamples(printerId, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, samples, out _);
        Assert.Equal(count, copied);
        return samples;
    }

    private static void AssertNozzles(TelemetrySample[] samples, params float[] expected)
    {
        float[] actual = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
        {
            actual[i] = samples[i].NozzleActual;
        }

        Assert.Equal(expected, actual);
    }
}
