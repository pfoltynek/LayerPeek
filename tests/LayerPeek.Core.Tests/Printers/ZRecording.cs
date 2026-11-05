using System.Globalization;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Tests.Printers;

/// <summary>
/// A recorded print of <c>Fixtures/z-recordings</c> (T-159): the printer's state and Z axis over time, run-length encoded
/// (a row whenever a value changes, the value holds until the next row), so that it can be sampled at any poll time.
/// </summary>
internal sealed class ZRecording
{
    private readonly int[] _seconds;
    private readonly PrinterState[] _states;
    private readonly double[] _z;

    private ZRecording(int[] seconds, PrinterState[] states, double[] z)
    {
        _seconds = seconds;
        _states = states;
        _z = z;
    }

    /// <summary>Seconds from the first to the last sample.</summary>
    public int Length => _seconds[^1];

    public static ZRecording Load(string fileName)
    {
        List<int> seconds = [];
        List<PrinterState> states = [];
        List<double> z = [];
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "z-recordings", fileName)))
        {
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("seconds,", StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = line.Split(',');
            seconds.Add(int.Parse(parts[0], CultureInfo.InvariantCulture));
            states.Add(parts[1] switch
            {
                "PRINTING" => PrinterState.Printing,
                "PAUSED" => PrinterState.Paused,
                "FINISHED" => PrinterState.Finished,
                "STOPPED" => PrinterState.Stopped,
                "ATTENTION" => PrinterState.Attention,
                "BUSY" => PrinterState.Busy,
                "IDLE" => PrinterState.Idle,
                _ => PrinterState.Offline,
            });
            z.Add(double.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        return new ZRecording([.. seconds], [.. states], [.. z]);
    }

    /// <summary>What a poll at <paramref name="second"/> saw: the last recorded value at or before it.</summary>
    public (PrinterState State, double Z) At(int second)
    {
        int index = Array.BinarySearch(_seconds, second);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return (_states[index], _z[index]);
    }

    /// <summary>The polls of a monitor with <paramref name="interval"/> seconds, the first at <paramref name="offset"/>.</summary>
    public IEnumerable<(int Second, PrinterState State, double Z)> Poll(int interval, int offset)
    {
        for (int second = offset; second <= Length; second += interval)
        {
            (PrinterState state, double z) = At(second);
            yield return (second, state, z);
        }
    }
}
