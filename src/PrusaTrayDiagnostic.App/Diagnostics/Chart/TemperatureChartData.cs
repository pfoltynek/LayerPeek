using PrusaTrayDiagnostic.Core.History;

namespace PrusaTrayDiagnostic.App.Diagnostics.Chart;

/// <summary>Lines of the temperature chart.</summary>
public enum TemperatureSeries
{
    NozzleActual,
    NozzleTarget,
    BedActual,
    BedTarget,
}

/// <summary>
/// One pixel column of a series after decimation: the first, lowest, highest and last value of the samples that fall
/// into it, so that no peak is lost however many samples share the column.
/// </summary>
/// <param name="Column">Column index, 0 = left edge of the plot.</param>
/// <param name="StartsLine">Not connected to the previous column: the first value of the series, or a missing value
/// (NaN, target 0) or a new history segment (<see cref="TelemetrySample.StartsSegment"/>) lies between them.</param>
public readonly record struct ChartColumn(int Column, float First, float Min, float Max, float Last, bool StartsLine);

/// <summary>
/// Prepares the temperature chart from <see cref="TelemetryHistory"/> without Avalonia: copies the samples of a time
/// range into one reusable buffer and reduces them to at most one <see cref="ChartColumn"/> per pixel column and series.
/// </summary>
/// <remarks>
/// <para>
/// Cost: samples are copied only when the history version, the printer or the range changed; a new width only
/// re-decimates. Decimation is a single pass over the copied samples (a day of 1 s samples = 86 401) for all series.
/// </para>
/// <para>
/// Memory: the sample buffer has <see cref="TelemetryHistory.GetCapacity"/> samples and is reallocated only when that
/// capacity changes (polling interval changed) or released when no printer is shown; the column buffers grow with the
/// widest chart seen. Steady-state updates do not allocate. Not thread-safe (the chart uses it on the UI thread).
/// </para>
/// <para>
/// A target of 0 means the heater is off and is treated as a missing value, so the dashed target line shows only while
/// heating. A break inside one pixel column is not visible at that width and is merged into the column.
/// </para>
/// </remarks>
public sealed class TemperatureChartData
{
    public const int SeriesCount = 4;

    private readonly ChartColumn[][] _series = [[], [], [], []];
    private readonly int[] _seriesCounts = new int[SeriesCount];
    private TelemetrySample[] _samples = [];
    private TelemetryHistory? _history;
    private string? _printerId;
    private bool _initialized;

    /// <summary>Start of the range of the last <see cref="Update"/> (left edge of the plot).</summary>
    public DateTimeOffset From { get; private set; }

    /// <summary>End of the range of the last <see cref="Update"/> (right edge of the plot).</summary>
    public DateTimeOffset To { get; private set; }

    public int Columns { get; private set; }

    /// <summary>History version of the copied samples; 0 when no printer is shown.</summary>
    public long Version { get; private set; }

    /// <summary>Samples in the range, including gap samples without values.</summary>
    public int SampleCount { get; private set; }

    /// <summary>Highest value of all series in the range; NaN when there is none.</summary>
    public float MaxValue { get; private set; } = float.NaN;

    internal int SampleBufferLength => _samples.Length;

    internal int SampleBufferAllocations { get; private set; }

    internal int ColumnBufferAllocations { get; private set; }

    /// <summary>
    /// Brings the chart data up to date for <paramref name="printerId"/> (<see langword="null"/> = nothing to show)
    /// within <paramref name="from"/> – <paramref name="to"/> at <paramref name="columns"/> pixel columns.
    /// </summary>
    /// <returns><see langword="true"/> when the columns were rebuilt (redraw needed); <see langword="false"/> when nothing changed.</returns>
    public bool Update(TelemetryHistory history, string? printerId, DateTimeOffset from, DateTimeOffset to, int columns)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);

        long version = printerId is null ? 0 : history.GetVersion(printerId);
        bool copy = !_initialized
            || !ReferenceEquals(history, _history)
            || !string.Equals(printerId, _printerId, StringComparison.OrdinalIgnoreCase)
            || version != Version
            || from != From
            || to != To;
        if (!copy && columns == Columns)
        {
            return false;
        }

        if (copy)
        {
            _history = history;
            _printerId = printerId;
            From = from;
            To = to;
            CopySamples(history, printerId);
        }

        Columns = columns;
        _initialized = true;
        Decimate();
        return true;
    }

    /// <summary>Non-empty columns of <paramref name="series"/>, left to right. Valid until the next <see cref="Update"/>.</summary>
    public ReadOnlySpan<ChartColumn> GetColumns(TemperatureSeries series)
    {
        int index = (int)series;
        ArgumentOutOfRangeException.ThrowIfNegative(index, nameof(series));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, SeriesCount, nameof(series));
        return _series[index].AsSpan(0, _seriesCounts[index]);
    }

    private static float ValueOf(in TelemetrySample sample, int series) => series switch
    {
        0 => sample.NozzleActual,
        1 => HeatingTarget(sample.NozzleTarget),
        2 => sample.BedActual,
        _ => HeatingTarget(sample.BedTarget),
    };

    /// <summary>0 (heater off) and negative targets are not drawn.</summary>
    private static float HeatingTarget(float target) => target > 0 ? target : float.NaN;

    private void CopySamples(TelemetryHistory history, string? printerId)
    {
        if (printerId is null)
        {
            // Nothing shown: release the buffer (up to ~4 MB) instead of keeping it for a printer that may not come back.
            _samples = [];
            SampleCount = 0;
            Version = 0;
            return;
        }

        int capacity = history.GetCapacity(printerId);
        if (capacity != _samples.Length)
        {
            _samples = capacity == 0 ? [] : new TelemetrySample[capacity];
            if (capacity != 0)
            {
                SampleBufferAllocations++;
            }
        }

        SampleCount = history.CopySamples(printerId, From, To, _samples, out long version);
        Version = version;
    }

    private void EnsureColumnBuffers(int columns)
    {
        for (int s = 0; s < SeriesCount; s++)
        {
            if (_series[s].Length < columns)
            {
                _series[s] = new ChartColumn[columns];
                ColumnBufferAllocations++;
            }
        }
    }

    private void Decimate()
    {
        int columns = Columns;
        EnsureColumnBuffers(columns);
        Array.Clear(_seriesCounts);

        long fromTicks = From.UtcTicks;
        double span = To.UtcTicks - fromTicks;
        float max = float.NaN;
        Span<Accumulator> accumulators = stackalloc Accumulator[SeriesCount];
        for (int s = 0; s < SeriesCount; s++)
        {
            accumulators[s] = Accumulator.Empty;
        }

        ReadOnlySpan<TelemetrySample> samples = _samples.AsSpan(0, SampleCount);
        for (int i = 0; i < samples.Length; i++)
        {
            ref readonly TelemetrySample sample = ref samples[i];
            // Multiply before dividing, so that a sample exactly on a column boundary lands in that column.
            int column = span > 0 ? (int)((sample.TimestampUtcTicks - fromTicks) * (double)columns / span) : 0;
            column = Math.Clamp(column, 0, columns - 1);

            for (int s = 0; s < SeriesCount; s++)
            {
                ref Accumulator accumulator = ref accumulators[s];
                if (sample.StartsSegment)
                {
                    accumulator.PendingBreak = true;
                }

                float value = ValueOf(sample, s);
                if (!float.IsFinite(value))
                {
                    accumulator.PendingBreak = true;
                    continue;
                }

                if (!(value <= max))
                {
                    // Also replaces the initial NaN.
                    max = value;
                }

                if (accumulator.Column == column)
                {
                    accumulator.Add(value);
                }
                else
                {
                    Flush(s, accumulator);
                    accumulator.Start(column, value);
                }
            }
        }

        for (int s = 0; s < SeriesCount; s++)
        {
            Flush(s, accumulators[s]);
        }

        MaxValue = max;
    }

    private void Flush(int series, in Accumulator accumulator)
    {
        if (accumulator.Column >= 0)
        {
            _series[series][_seriesCounts[series]++] = accumulator.ToColumn();
        }
    }

    /// <summary>The column of a series being collected.</summary>
    private struct Accumulator
    {
        public static readonly Accumulator Empty = new() { Column = -1, PendingBreak = true };

        public int Column;
        public float First;
        public float Min;
        public float Max;
        public float Last;
        public bool StartsLine;

        /// <summary>A missing value or a segment start was seen since the last value: the next column starts a new line.</summary>
        public bool PendingBreak;

        public void Start(int column, float value)
        {
            Column = column;
            First = Min = Max = Last = value;
            StartsLine = PendingBreak;
            PendingBreak = false;
        }

        public void Add(float value)
        {
            Min = Math.Min(Min, value);
            Max = Math.Max(Max, value);
            Last = value;
            // A break inside one pixel column is invisible; the values after it continue this column.
            PendingBreak = false;
        }

        public readonly ChartColumn ToColumn() => new(Column, First, Min, Max, Last, StartsLine);
    }
}
