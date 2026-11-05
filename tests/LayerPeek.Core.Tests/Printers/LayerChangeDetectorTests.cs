using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Printers;

/// <summary>
/// T-159: layer changes estimated from the Z axis, on a real Core One print (subsampled to the polling intervals of the
/// app) and on synthetic prints.
/// </summary>
public sealed class LayerChangeDetectorTests
{
    private const string CoreOnePrint = "coreone-job354-0.2mm.csv";

    /// <summary>The first layer of the recorded print (0.2 mm) starts here, after ~4 min of start sequence.</summary>
    private const int FirstLayerSecond = 235;

    /// <summary>Its last layer: 55.4 mm (277 layers of 0.2 mm); afterwards the head lifts to 105.4 and 225.6 mm.</summary>
    private const int LastLayerTenths = 554;

    private const long Job = 354;

    private static readonly int[] ExpectedChanges = [.. Enumerable.Range(2, LastLayerTenths / 2 - 1).Select(static layer => layer * 2)];

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void EveryLayerOfTheRealPrintIsFoundOnceAndNothingElse(int interval)
    {
        ZRecording recording = ZRecording.Load(CoreOnePrint);
        Dictionary<int, int> firstSeen = FirstSeen(recording);

        for (int offset = 0; offset < interval; offset++)
        {
            List<(int Second, int Tenths)> changes = Changes(recording, interval, offset);

            // 276 changes for 277 layers: the first layer is reached from above (the start sequence), which is no change.
            Assert.Equal(ExpectedChanges, changes.Select(static c => c.Tenths));
            foreach ((int second, int tenths) in changes)
            {
                // Never before the nozzle was at that height, and at most a few polls later.
                int latency = second - firstSeen[tenths];
                Assert.True(latency >= 0 && latency <= 4 * interval, $"layer {tenths}: seen {firstSeen[tenths]} s, change {second} s");
            }
        }
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(10, 3)]
    public void PollingEverySecondOrEveryTenSecondsStillFindsNearlyEveryLayer(int interval, int maxSpurious)
    {
        ZRecording recording = ZRecording.Load(CoreOnePrint);

        for (int offset = 0; offset < interval; offset++)
        {
            List<(int Second, int Tenths)> changes = Changes(recording, interval, offset);
            HashSet<int> found = [];
            int spurious = 0;
            foreach ((_, int tenths) in changes)
            {
                if (Array.BinarySearch(ExpectedChanges, tenths) < 0 || !found.Add(tenths))
                {
                    spurious++;
                }
            }

            Assert.InRange(found.Count, ExpectedChanges.Length - 1, ExpectedChanges.Length);
            Assert.InRange(spurious, 0, maxSpurious);
        }
    }

    [Fact]
    public void TheRealPrintHasNoChangeInTheStartSequenceNorInTheLiftAtItsEnd()
    {
        ZRecording recording = ZRecording.Load(CoreOnePrint);

        List<(int Second, int Tenths)> changes = Changes(recording, 5, 0);

        Assert.DoesNotContain(changes, static c => c.Second < FirstLayerSecond);
        Assert.Equal(LastLayerTenths, changes[^1].Tenths);
    }

    [Theory]
    [InlineData(0.2)]
    [InlineData(1.5)]
    public void AZHopOfOneOrTwoPollsIsNoLayer(double hop)
    {
        LayerChangeDetector detector = new();
        Feed(detector, 10.0, 3);

        Assert.False(Printing(detector, 10.0 + hop));
        Feed(detector, 10.0, 3);
        Assert.False(Printing(detector, 10.0 + hop));
        Assert.False(Printing(detector, 10.0 + hop));
        Assert.Equal(0, Feed(detector, 10.0, 3));

        Assert.Equal(1, Feed(detector, 10.2, 3));
        Assert.Equal(10.2, detector.LayerZ, 6);
        Assert.Equal(1, detector.LayerChanges);
    }

    [Fact]
    public void ARampedZHopIsNoLayer()
    {
        LayerChangeDetector detector = new();
        Feed(detector, 10.0, 3);

        // The travel lift ramps up to 1.5 mm and down again; at 2–5 s polling a hop shows in one or two polls (1–3 s each on
        // the real print).
        int changes = 0;
        foreach (double z in (double[])[10.1, 10.0, 10.0, 10.6, 11.5, 10.0, 10.0, 10.8, 10.3, 10.0, 10.0, 10.0])
        {
            changes += Printing(detector, z) ? 1 : 0;
        }

        Assert.Equal(0, changes);
        Assert.Equal(10.0, detector.LayerZ, 6);
    }

    [Fact]
    public void AZHopThatStaysForAWholePlateauIsAnExtraLayerAndTheNextLayerIsStillFound()
    {
        LayerChangeDetector detector = new();
        Feed(detector, 10.0, 3);

        // E.g. a tool change of the MMU parked 0.6 mm above the print while still printing: a frame of the same layer.
        Assert.Equal(1, Feed(detector, 10.6, 6));
        Assert.Equal(0, Feed(detector, 10.0, 4));
        Assert.Equal(10.0, detector.LayerZ, 6);
        Assert.Equal(1, Feed(detector, 10.2, 4));
        Assert.Equal(10.2, detector.LayerZ, 6);
    }

    [Fact]
    public void AParkMoreThanALayerAboveThePrintIsNoLayer()
    {
        LayerChangeDetector detector = new();
        Feed(detector, 10.0, 3);

        Assert.Equal(0, Feed(detector, 12.0, 8));
        Assert.Equal(0, Feed(detector, 10.0, 4));
        Assert.Equal(1, Feed(detector, 10.2, 4));
    }

    [Theory]
    [InlineData(0.05, 6)]
    [InlineData(0.1, 6)]
    [InlineData(0.15, 4)]
    [InlineData(0.2, 6)]
    [InlineData(0.3, 4)]
    [InlineData(0.05, 2)]
    [InlineData(0.2, 1)]
    public void LayersOfEveryHeightAreFoundAsFarAsTheReportedTenthsShowThem(double layerHeight, int pollsPerLayer)
    {
        LayerChangeDetector detector = new();
        List<double> found = [];
        const int Layers = 200;
        for (int layer = 1; layer <= Layers; layer++)
        {
            // PrusaLink reports the height with one decimal (marlin_vars, float).
            double reported = Math.Round((float)(layer * layerHeight), 1, MidpointRounding.AwayFromZero);
            for (int poll = 0; poll < pollsPerLayer; poll++)
            {
                if (Printing(detector, reported))
                {
                    found.Add(detector.LayerZ);
                }
            }
        }

        // Every change is to a height the nozzle printed at, upwards, and never more than one per reported tenth.
        double[] heights = [.. Enumerable.Range(1, Layers).Select(layer => Math.Round((float)(layer * layerHeight), 1, MidpointRounding.AwayFromZero)).Distinct()];
        Assert.All(found, z => Assert.Contains(z, heights));
        Assert.Equal(found.Order(), found);
        Assert.Equal(found.Distinct().Count(), found.Count);
        // Layers shorter than a poll merge (at most one change per poll); otherwise every visible height but the first.
        int visible = heights.Length - 1;
        int atMost = Math.Min(visible, Layers * pollsPerLayer);
        Assert.InRange(found.Count, pollsPerLayer >= 3 ? visible - 1 : atMost * 8 / 10, atMost);
    }

    [Fact]
    public void LayersShorterThanAPollMergeIntoOneChangePerPoll()
    {
        LayerChangeDetector detector = new();
        int changes = 0;
        // 0.2 mm layers of 2 s at 5 s polling: 0.4–0.6 mm per poll (shown as 0.4 / 0.6 by the rounding).
        for (int poll = 0; poll < 100; poll++)
        {
            changes += Printing(detector, Math.Round(1.0 + poll * 0.5, 1)) ? 1 : 0;
        }

        Assert.InRange(changes, 90, 97);
    }

    [Fact]
    public void ASequentialPrintFindsTheLayersOfEachObjectAndNoneInBetween()
    {
        LayerChangeDetector detector = new();
        int first = Layers(detector, 0.2, 0.2, 25, 4);
        // The head lifts above the first object, travels to the next one and goes down to its first layer.
        int between = Feed(detector, 25.0, 6) + Feed(detector, 0.2, 4);
        int second = Layers(detector, 0.4, 0.2, 24, 4);

        Assert.Equal(24, first);
        Assert.Equal(0, between);
        Assert.Equal(24, second);
        Assert.Equal(5.0, detector.LayerZ, 6);
    }

    [Theory]
    [InlineData(PrinterState.Paused)]
    [InlineData(PrinterState.Attention)]
    [InlineData(PrinterState.Busy)]
    public void APauseKeepsTheLayerAndItsParkedHeadIsNoLayer(PrinterState pause)
    {
        LayerChangeDetector detector = new();
        Layers(detector, 0.2, 0.2, 10, 4);

        for (int poll = 0; poll < 20; poll++)
        {
            Assert.False(detector.Observe(pause, Job, 2.6));
        }

        Assert.Equal(0, Feed(detector, 2.0, 5));
        Assert.Equal(1, Feed(detector, 2.2, 4));
        Assert.Equal(10, detector.LayerChanges);
    }

    [Theory]
    [InlineData(PrinterState.Offline)]
    [InlineData(PrinterState.Unknown)]
    [InlineData(PrinterState.Unauthorized)]
    public void AnOutageKeepsTheLayerAndTheLayersPrintedMeanwhileShowAsOneChange(PrinterState outage)
    {
        LayerChangeDetector detector = new();
        Layers(detector, 0.2, 0.2, 10, 4);

        for (int poll = 0; poll < 12; poll++)
        {
            Assert.False(detector.Observe(outage, null, null));
        }

        // Three layers later.
        Assert.Equal(1, Feed(detector, 2.6, 4));
        Assert.Equal(2.6, detector.LayerZ, 6);
        Assert.Equal(1, Feed(detector, 2.8, 4));
    }

    [Fact]
    public void AnOutageLongerThanALayerRiseStillGoesOnAfterwards()
    {
        LayerChangeDetector detector = new();
        Layers(detector, 0.2, 0.2, 10, 4);
        detector.Observe(PrinterState.Offline, null, null);

        Assert.Equal(0, Feed(detector, 4.0, 4));
        Assert.Equal(1, Feed(detector, 4.2, 4));
    }

    [Theory]
    [InlineData(PrinterState.Finished)]
    [InlineData(PrinterState.Stopped)]
    [InlineData(PrinterState.Idle)]
    [InlineData(PrinterState.Error)]
    public void TheEndOfThePrintStartsOver(PrinterState end)
    {
        LayerChangeDetector detector = new();
        Layers(detector, 0.2, 0.2, 10, 4);

        Assert.False(detector.Observe(end, Job, 225.6));

        Assert.Equal(0, detector.LayerChanges);
        Assert.True(double.IsNaN(detector.LayerZ));
        // The next print's first plateau is its first layer, even above the last one.
        Assert.Equal(0, Feed(detector, 2.2, 4, job: Job + 1));
    }

    [Fact]
    public void AnotherJobStartsOver()
    {
        LayerChangeDetector detector = new();
        Layers(detector, 0.2, 0.2, 10, 4);

        // A new print right away (no state in between) whose first plateau is above the last layer.
        Assert.Equal(0, Feed(detector, 2.4, 4, job: Job + 1));
        Assert.Equal(0, detector.LayerChanges);
        Assert.Equal(1, Feed(detector, 2.6, 4, job: Job + 1));
        // An unknown job id is no other job.
        Assert.Equal(1, Feed(detector, 2.8, 4, job: null));
        Assert.Equal(2, detector.LayerChanges);
    }

    [Fact]
    public void AnUnknownZBreaksTheRunOfSamplesWithoutAChange()
    {
        LayerChangeDetector detector = new();
        Feed(detector, 1.0, 3);
        Printing(detector, 1.2);
        Printing(detector, 1.2);

        Assert.False(detector.Observe(PrinterState.Printing, Job, null));
        Assert.False(detector.Observe(PrinterState.Printing, Job, double.NaN));
        // The run starts again: two samples are no plateau yet.
        Assert.False(Printing(detector, 1.2));
        Assert.False(Printing(detector, 1.2));
        Assert.True(Printing(detector, 1.2));
    }

    [Fact]
    public void ASnapshotIsReadLikeItsValues()
    {
        LayerChangeDetector detector = new();
        static PrinterSnapshot Snapshot(double z) => new()
        {
            PrinterId = "core-one",
            State = PrinterState.Printing,
            Timestamp = DateTimeOffset.UnixEpoch,
            Job = new PrintJob { Id = Job },
            Telemetry = new Telemetry { AxisZ = z },
        };

        bool[] results = [.. ((double[])[1.0, 1.0, 1.0, 1.2, 1.2, 1.2]).Select(z => detector.Observe(Snapshot(z)))];

        Assert.Equal([false, false, false, false, false, true], results);
        Assert.False(detector.Observe(new PrinterSnapshot { PrinterId = "core-one", State = PrinterState.Printing, Timestamp = DateTimeOffset.UnixEpoch }));
        Assert.Throws<ArgumentNullException>(() => detector.Observe(null!));
    }

    [Fact]
    public void ASampleAllocatesNothing()
    {
        LayerChangeDetector detector = new();
        PrinterSnapshot snapshot = new()
        {
            PrinterId = "core-one",
            State = PrinterState.Printing,
            Timestamp = DateTimeOffset.UnixEpoch,
            Job = new PrintJob { Id = Job },
            Telemetry = new Telemetry { AxisZ = 1.0 },
        };
        Feed(detector, 1.0, 4);
        detector.Observe(snapshot);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int changes = 0;
        for (int poll = 0; poll < 10_000; poll++)
        {
            changes += detector.Observe(PrinterState.Printing, Job, 1.0 + (poll / 4 * 0.2)) ? 1 : 0;
            if (poll % 100 == 99)
            {
                detector.Observe(PrinterState.Paused, Job, 50.0);
            }
        }

        for (int poll = 0; poll < 1_000; poll++)
        {
            detector.Observe(snapshot);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
        Assert.True(changes > 0);
    }

    /// <summary>The changes of the recording polled every <paramref name="interval"/> s from <paramref name="offset"/>.</summary>
    private static List<(int Second, int Tenths)> Changes(ZRecording recording, int interval, int offset)
    {
        LayerChangeDetector detector = new();
        List<(int, int)> changes = [];
        foreach ((int second, PrinterState state, double z) in recording.Poll(interval, offset))
        {
            if (detector.Observe(state, Job, z))
            {
                changes.Add((second, (int)Math.Round(detector.LayerZ * 10)));
            }
        }

        return changes;
    }

    /// <summary>
    /// When each layer started: the first second after the start sequence from which the nozzle stayed at its height for three
    /// seconds (a z-hop to that height during the layer below, at most one sample of the recording, does not count).
    /// </summary>
    private static Dictionary<int, int> FirstSeen(ZRecording recording)
    {
        Dictionary<int, int> first = [];
        for (int second = FirstLayerSecond; second < recording.Length - 1; second++)
        {
            double z = recording.At(second).Z;
            if (z == recording.At(second + 1).Z && z == recording.At(second + 2).Z)
            {
                first.TryAdd((int)Math.Round(z * 10), second);
            }
        }

        return first;
    }

    private static bool Printing(LayerChangeDetector detector, double z, long? job = Job) => detector.Observe(PrinterState.Printing, job, z);

    /// <summary><paramref name="polls"/> polls at <paramref name="z"/>; returns the changes.</summary>
    private static int Feed(LayerChangeDetector detector, double z, int polls, long? job = Job)
    {
        int changes = 0;
        for (int poll = 0; poll < polls; poll++)
        {
            changes += Printing(detector, z, job) ? 1 : 0;
        }

        return changes;
    }

    /// <summary><paramref name="count"/> layers from <paramref name="first"/>, <paramref name="polls"/> polls each; returns the changes.</summary>
    private static int Layers(LayerChangeDetector detector, double first, double height, int count, int polls)
    {
        int changes = 0;
        for (int layer = 0; layer < count; layer++)
        {
            changes += Feed(detector, Math.Round(first + layer * height, 1), polls);
        }

        return changes;
    }
}
