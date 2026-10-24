using LayerPeek.App.Diagnostics.Chart;
using LayerPeek.Core.History;
using LayerPeek.Core.Printers;

using static LayerPeek.App.Tests.Tray.TestPrinters;

namespace LayerPeek.App.Tests.Diagnostics;

public sealed class TemperatureChartDataTests
{
    internal static readonly DateTimeOffset T0 = Now;
    private readonly TelemetryHistory _history = new();
    private readonly TemperatureChartData _data = new();

    public TemperatureChartDataTests() => _history.Configure([Definition()], TimeSpan.FromSeconds(1));

    [Fact]
    public void EmptyHistoryHasNoColumnsAndNoMaximum()
    {
        _data.Update(_history, "mk4", T0, T0.AddMinutes(30), 100);

        Assert.Equal(0, _data.SampleCount);
        Assert.True(float.IsNaN(_data.MaxValue));
        foreach (TemperatureSeries series in Enum.GetValues<TemperatureSeries>())
        {
            Assert.True(_data.GetColumns(series).IsEmpty);
        }
    }

    [Fact]
    public void UnknownOrMissingPrinterIsEmpty()
    {
        Record(0, nozzle: 200);

        _data.Update(_history, "other", T0, T0.AddMinutes(1), 100);
        Assert.Equal(0, _data.SampleCount);

        _data.Update(_history, null, T0, T0.AddMinutes(1), 100);
        Assert.Equal(0, _data.SampleCount);
        Assert.True(_data.GetColumns(TemperatureSeries.NozzleActual).IsEmpty);
    }

    [Fact]
    public void SingleSampleIsOneColumnStartingALine()
    {
        Record(30, nozzle: 215.5, nozzleTarget: 215, bed: 60, bedTarget: 60);

        _data.Update(_history, "mk4", T0, T0.AddSeconds(60), 60);

        ChartColumn column = Assert.Single(_data.GetColumns(TemperatureSeries.NozzleActual).ToArray());
        Assert.Equal(new ChartColumn(30, 215.5f, 215.5f, 215.5f, 215.5f, StartsLine: true), column);
        Assert.Equal(60f, Assert.Single(_data.GetColumns(TemperatureSeries.BedTarget).ToArray()).Max);
        Assert.Equal(215.5f, _data.MaxValue);
        Assert.Equal(1, _data.SampleCount);
    }

    [Fact]
    public void DecimationKeepsFirstMinMaxAndLastOfEveryColumn()
    {
        // 1000 samples (one per second) into 37 columns: several samples per column, uneven split.
        double[] values = new double[1000];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = 150 + (60 * Math.Sin(i * 0.37)) + (i % 7);
            Record(i, nozzle: values[i]);
        }

        DateTimeOffset to = T0.AddSeconds(999);
        _data.Update(_history, "mk4", T0, to, 37);

        ReadOnlySpan<ChartColumn> columns = _data.GetColumns(TemperatureSeries.NozzleActual);
        Assert.Equal(37, columns.Length);
        for (int c = 0; c < columns.Length; c++)
        {
            float[] inColumn = [.. Enumerable.Range(0, values.Length).Where(i => ExpectedColumn(i, 999, 37) == c).Select(i => (float)values[i])];
            ChartColumn column = columns[c];
            Assert.Equal(c, column.Column);
            Assert.Equal(inColumn[0], column.First);
            Assert.Equal(inColumn[^1], column.Last);
            Assert.Equal(inColumn.Min(), column.Min);
            Assert.Equal(inColumn.Max(), column.Max);
            Assert.Equal(c == 0, column.StartsLine);
        }

        Assert.Equal((float)values.Max(), _data.MaxValue);
        Assert.Equal(1000, _data.SampleCount);
    }

    [Fact]
    public void DayOfOneSecondSamplesIsReducedToPixelColumns()
    {
        TelemetryHistory history = new();
        history.Configure([Definition()], TimeSpan.FromSeconds(1));
        int count = TelemetryHistory.CapacityFor(TimeSpan.FromDays(1), TimeSpan.FromSeconds(1));
        for (int i = 0; i < count; i++)
        {
            history.Record(Sample(i, nozzle: i % 100 == 50 ? 290 : 200, bed: 60));
        }

        _data.Update(history, "mk4", T0, T0.AddSeconds(count - 1), 640);

        ReadOnlySpan<ChartColumn> columns = _data.GetColumns(TemperatureSeries.NozzleActual);
        Assert.Equal(count, _data.SampleCount);
        Assert.Equal(640, columns.Length);
        // Every column covers ~135 samples, so it contains at least one spike (one per 100 s): none is lost by decimation.
        foreach (ChartColumn column in columns)
        {
            Assert.Equal(290f, column.Max);
            Assert.Equal(200f, column.Min);
        }
    }

    [Fact]
    public void MissingValueBreaksTheLine()
    {
        for (int i = 0; i < 20; i++)
        {
            if (i == 10)
            {
                _history.Record(PrinterSnapshot.Unavailable("mk4", PrinterState.Offline, T0.AddSeconds(i)));
            }
            else
            {
                Record(i, nozzle: 200 + i, bed: i == 15 ? null : 60);
            }
        }

        _data.Update(_history, "mk4", T0, T0.AddSeconds(19), 20);

        ReadOnlySpan<ChartColumn> nozzle = _data.GetColumns(TemperatureSeries.NozzleActual);
        Assert.Equal(19, nozzle.Length);
        Assert.Equal([0, 11], StartingColumns(nozzle));
        ReadOnlySpan<ChartColumn> bed = _data.GetColumns(TemperatureSeries.BedActual);
        Assert.Equal(18, bed.Length);
        Assert.Equal([0, 11, 16], StartingColumns(bed));
    }

    [Fact]
    public void SegmentStartBreaksTheLineEvenWithoutMissingValues()
    {
        Record(0, nozzle: 200);
        Record(1, nozzle: 201);
        // Longer pause than the gap threshold (30 s): the history marks the next sample as a new segment.
        Record(100, nozzle: 202);
        Record(101, nozzle: 203);

        _data.Update(_history, "mk4", T0, T0.AddSeconds(101), 102);

        Assert.Equal([0, 100], StartingColumns(_data.GetColumns(TemperatureSeries.NozzleActual)));
    }

    [Fact]
    public void LineContinuesAcrossEmptyColumnsBetweenConnectedSamples()
    {
        // Sparse data (5 s polling) in a wide chart: consecutive samples are connected although columns lie between them.
        TelemetryHistory history = new();
        history.Configure([Definition()], TimeSpan.FromSeconds(5));
        for (int i = 0; i < 10; i++)
        {
            history.Record(Sample(i * 5, nozzle: 200));
        }

        _data.Update(history, "mk4", T0, T0.AddSeconds(45), 450);

        ReadOnlySpan<ChartColumn> columns = _data.GetColumns(TemperatureSeries.NozzleActual);
        Assert.Equal(10, columns.Length);
        Assert.Equal([0], StartingColumns(columns));
    }

    [Fact]
    public void ZeroTargetMeansHeaterOffAndIsNotDrawn()
    {
        Record(0, nozzle: 25, nozzleTarget: 0);
        Record(1, nozzle: 30, nozzleTarget: 215);
        Record(2, nozzle: 40, nozzleTarget: 215);
        Record(3, nozzle: 50, nozzleTarget: 0);
        Record(4, nozzle: 50, nozzleTarget: 215);

        _data.Update(_history, "mk4", T0, T0.AddSeconds(4), 5);

        ReadOnlySpan<ChartColumn> target = _data.GetColumns(TemperatureSeries.NozzleTarget);
        Assert.Equal([1, 2, 4], ColumnsOf(target));
        Assert.Equal([1, 4], StartingColumns(target));
    }

    [Fact]
    public void SamplesOutsideTheWindowAreClipped()
    {
        for (int i = 0; i < 100; i++)
        {
            Record(i, nozzle: i < 40 || i > 59 ? 999 : 200);
        }

        _data.Update(_history, "mk4", T0.AddSeconds(40), T0.AddSeconds(59), 20);

        ReadOnlySpan<ChartColumn> columns = _data.GetColumns(TemperatureSeries.NozzleActual);
        Assert.Equal(20, _data.SampleCount);
        Assert.Equal(20, columns.Length);
        Assert.Equal(200f, _data.MaxValue);
        Assert.Equal(0, columns[0].Column);
        Assert.Equal(19, columns[^1].Column);
    }

    [Fact]
    public void NewestSampleAtTheRightEdgeFallsIntoTheLastColumn()
    {
        Record(0, nozzle: 200);
        Record(10, nozzle: 210);

        _data.Update(_history, "mk4", T0, T0.AddSeconds(10), 4);

        Assert.Equal([0, 3], ColumnsOf(_data.GetColumns(TemperatureSeries.NozzleActual)));
    }

    [Fact]
    public void UpdateReportsChangesOnlyWhenSomethingChanged()
    {
        Record(0, nozzle: 200);
        DateTimeOffset to = T0.AddSeconds(60);

        Assert.True(_data.Update(_history, "mk4", T0, to, 60));
        Assert.False(_data.Update(_history, "mk4", T0, to, 60));

        Record(1, nozzle: 201);
        Assert.True(_data.Update(_history, "mk4", T0, to, 60));
        Assert.False(_data.Update(_history, "mk4", T0, to, 60));

        Assert.True(_data.Update(_history, "mk4", T0, to, 30));
        Assert.True(_data.Update(_history, "mk4", T0, to.AddSeconds(1), 30));
        Assert.True(_data.Update(_history, null, T0, to, 30));
        Assert.False(_data.Update(_history, null, T0, to, 30));
    }

    [Fact]
    public void SwitchingPrintersShowsTheOtherHistory()
    {
        _history.Configure([Definition("a"), Definition("b")], TimeSpan.FromSeconds(1));
        _history.Record(Sample(0, nozzle: 200, printerId: "a"));
        _history.Record(Sample(0, nozzle: 100, printerId: "b"));

        _data.Update(_history, "a", T0, T0.AddSeconds(10), 10);
        Assert.Equal(200f, _data.MaxValue);

        Assert.True(_data.Update(_history, "B", T0, T0.AddSeconds(10), 10));
        Assert.Equal(100f, _data.MaxValue);
    }

    [Fact]
    public void SampleBufferIsSizedByCapacityAndReusedUntilItChanges()
    {
        Record(0, nozzle: 200);
        _data.Update(_history, "mk4", T0, T0.AddMinutes(1), 60);
        Assert.Equal(_history.GetCapacity("mk4"), _data.SampleBufferLength);
        int allocations = _data.SampleBufferAllocations;

        Record(1, nozzle: 201);
        _data.Update(_history, "mk4", T0, T0.AddMinutes(1), 60);
        Assert.Equal(allocations, _data.SampleBufferAllocations);

        // A longer polling interval shrinks the capacity: the large buffer is released.
        _history.Configure([Definition()], TimeSpan.FromSeconds(10));
        _data.Update(_history, "mk4", T0, T0.AddMinutes(1), 60);
        Assert.Equal(_history.GetCapacity("mk4"), _data.SampleBufferLength);
        Assert.Equal(allocations + 1, _data.SampleBufferAllocations);
        Assert.Equal(2, _data.SampleCount);

        // A shorter one grows it again; the samples are still all copied.
        _history.Configure([Definition()], TimeSpan.FromSeconds(1));
        _data.Update(_history, "mk4", T0, T0.AddMinutes(1), 60);
        Assert.Equal(_history.GetCapacity("mk4"), _data.SampleBufferLength);
        Assert.Equal(2, _data.SampleCount);
    }

    [Fact]
    public void ColumnBuffersGrowOnlyForWiderCharts()
    {
        Record(0, nozzle: 200);
        _data.Update(_history, "mk4", T0, T0.AddMinutes(1), 800);
        int allocations = _data.ColumnBufferAllocations;

        _data.Update(_history, "mk4", T0, T0.AddMinutes(1), 400);
        _data.Update(_history, "mk4", T0, T0.AddMinutes(1), 800);
        Assert.Equal(allocations, _data.ColumnBufferAllocations);

        _data.Update(_history, "mk4", T0, T0.AddMinutes(1), 1600);
        Assert.True(_data.ColumnBufferAllocations > allocations);
    }

    private static int ExpectedColumn(int second, int spanSeconds, int columns) =>
        Math.Min((int)(second * (double)columns / spanSeconds), columns - 1);

    private static int[] StartingColumns(ReadOnlySpan<ChartColumn> columns)
    {
        List<int> starts = [];
        foreach (ChartColumn column in columns)
        {
            if (column.StartsLine)
            {
                starts.Add(column.Column);
            }
        }

        return [.. starts];
    }

    private static int[] ColumnsOf(ReadOnlySpan<ChartColumn> columns)
    {
        int[] result = new int[columns.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            result[i] = columns[i].Column;
        }

        return result;
    }

    private void Record(int second, double? nozzle = null, double? nozzleTarget = null, double? bed = null, double? bedTarget = null) =>
        _history.Record(Sample(second, nozzle, nozzleTarget, bed, bedTarget));

    internal static PrinterSnapshot Sample(
        int second,
        double? nozzle = null,
        double? nozzleTarget = null,
        double? bed = null,
        double? bedTarget = null,
        string printerId = "mk4") => new()
        {
            PrinterId = printerId,
            State = PrinterState.Printing,
            Timestamp = T0.AddSeconds(second),
            Telemetry = new Telemetry
            {
                Nozzle = nozzle is { } n ? new Temperature(n, nozzleTarget) : null,
                Bed = bed is { } b ? new Temperature(b, bedTarget) : null,
            },
        };
}
