namespace LayerPeek.Core.Printers;

/// <summary>
/// Estimates layer changes of a print from the Z axis reported at every poll (T-159). PrusaLink on Buddy reports neither
/// the layer number nor a layer change event, only <see cref="Telemetry.AxisZ"/> (the target of the last queued move,
/// rounded to 0.1 mm), in which z-hops (Core One: 0.2 mm retract lift, travel lift up to 1.5 mm) show as short jumps up.
/// A pure state machine: no timers, no allocations per sample (four doubles and a few counters).
/// </summary>
/// <remarks>
/// <para>Rules, verified on a real Core One print sampled every 1 s and subsampled to 2 s and 5 s polling (T-159 notes):</para>
/// <list type="bullet">
/// <item><b>Only samples of a <see cref="PrinterState.Printing"/> printer with a known Z count.</b> Another job (a known
/// <see cref="PrintJob.Id"/> that differs) or the end of the print (<see cref="PrinterState.Idle"/>,
/// <see cref="PrinterState.Finished"/>, <see cref="PrinterState.Stopped"/>, <see cref="PrinterState.Error"/>) starts over.
/// A pause, a printer waiting for the user, busy or unreachable (<see cref="PrinterState.Paused"/>,
/// <see cref="PrinterState.Attention"/>, <see cref="PrinterState.Busy"/>, <see cref="PrinterState.Offline"/>, …) only breaks
/// the run of samples: the layer is kept, so the parked head of a pause is never a layer and the layers printed during an
/// outage show as one change after it.</item>
/// <item><b>Plateau:</b> the same Z in <see cref="PlateauSamples"/> samples in a row is a height the nozzle prints at.
/// A z-hop lasts only a travel move (on the real print 1 % of the 1 s samples, 1–3 s each, ramped so that two of them rarely
/// show the same value), so it almost never forms a plateau.</item>
/// <item><b>Steady climb</b> for layers shorter than the plateau (small parts): when the last <see cref="ClimbSamples"/>
/// samples show no plateau but none of their steps rises more than <see cref="MaxLayerRise"/>, their minimum is the height
/// reached (a z-hop only raises single samples). At most one layer per sample: layers shorter than a poll merge.</item>
/// <item><b>Layer change:</b> such a height at least <see cref="MinLayerRise"/> above the current layer and at most
/// <see cref="MaxLayerRise"/> above it. A bigger rise (the start sequence, the lift at the end of the print, a stable
/// travel lift) and a lower plateau (the first layer after the start sequence, the next object of a sequential print) only
/// set the current layer, without a change.</item>
/// </list>
/// <para>
/// With the 0.1 mm rounding of PrusaLink a 0.05 mm layer shows every other layer; a z-hop that stays for a whole plateau
/// (e.g. a tool change parked above the print) is counted as a layer of its own, and the return below it sets the layer back
/// without a change.
/// </para>
/// Not thread-safe: one caller at a time (the recording service calls it under its lock).
/// </remarks>
public sealed class LayerChangeDetector
{
    /// <summary>Samples in a row with the same Z that make a plateau.</summary>
    public const int PlateauSamples = 3;

    /// <summary>Samples of a steady climb (no plateau) whose minimum is taken as the height reached.</summary>
    public const int ClimbSamples = 4;

    /// <summary>The smallest rise that is a new layer (half the 0.1 mm resolution of PrusaLink), in mm.</summary>
    public const double MinLayerRise = 0.05;

    /// <summary>
    /// The largest rise that is still a new layer, in mm: above every layer height of the Prusa profiles (up to 0.55 mm with
    /// a 0.8 mm nozzle) and of a few short layers merged into one poll; the start sequence and the end of a print move more.
    /// </summary>
    public const double MaxLayerRise = 1.0;

    /// <summary>Two values closer than this are the same height (well below the 0.1 mm resolution).</summary>
    private const double SameHeight = 0.025;

    // The last samples of the current run, newest first; only the first _samples of them are valid.
    private double _z0;
    private double _z1;
    private double _z2;
    private double _z3;
    private int _samples;

    private long? _jobId;
    private double _layer = double.NaN;

    /// <summary>The Z of the current layer in mm, <see cref="double.NaN"/> before the first plateau of the print.</summary>
    public double LayerZ => _layer;

    /// <summary>Layer changes seen in the current print (since the last start over).</summary>
    public int LayerChanges { get; private set; }

    /// <summary>Takes the sample of a snapshot; see <see cref="Observe(PrinterState, long?, double?)"/>.</summary>
    public bool Observe(PrinterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Observe(snapshot.State, snapshot.Job?.Id, snapshot.Telemetry?.AxisZ);
    }

    /// <summary>Takes one sample (one poll of the printer).</summary>
    /// <param name="state">The printer's state.</param>
    /// <param name="jobId">The job's <see cref="PrintJob.Id"/>; <see langword="null"/> when unknown (not a new job).</param>
    /// <param name="axisZ">The Z axis in mm; <see langword="null"/> when unknown (the sample is skipped).</param>
    /// <returns>Whether this sample shows a new layer (its height is <see cref="LayerZ"/>).</returns>
    public bool Observe(PrinterState state, long? jobId, double? axisZ)
    {
        if (state != PrinterState.Printing)
        {
            if (EndsThePrint(state))
            {
                Reset();
            }
            else
            {
                _samples = 0;
            }

            return false;
        }

        if (jobId is { } id)
        {
            if (_jobId is { } known && known != id)
            {
                Reset();
            }

            _jobId = id;
        }

        if (axisZ is not { } z || !double.IsFinite(z))
        {
            _samples = 0;
            return false;
        }

        _z3 = _z2;
        _z2 = _z1;
        _z1 = _z0;
        _z0 = z;
        if (_samples < ClimbSamples)
        {
            _samples++;
        }

        if (_samples >= PlateauSamples && Same(_z0, _z1) && Same(_z0, _z2))
        {
            return Reached(z, lowerSetsTheLayer: true);
        }

        if (_samples >= ClimbSamples && IsSteadyClimb())
        {
            return Reached(Math.Min(Math.Min(_z0, _z1), Math.Min(_z2, _z3)), lowerSetsTheLayer: false);
        }

        return false;
    }

    /// <summary>Forgets the print: the next plateau is its first layer.</summary>
    public void Reset()
    {
        _samples = 0;
        _jobId = null;
        _layer = double.NaN;
        LayerChanges = 0;
    }

    private static bool EndsThePrint(PrinterState state) =>
        state is PrinterState.Idle or PrinterState.Finished or PrinterState.Stopped or PrinterState.Error;

    private static bool Same(double a, double b) => Math.Abs(a - b) < SameHeight;

    /// <summary>No step of the last <see cref="ClimbSamples"/> samples rises more than a layer can (a hop may fall back).</summary>
    private bool IsSteadyClimb() =>
        _z2 - _z3 <= MaxLayerRise && _z1 - _z2 <= MaxLayerRise && _z0 - _z1 <= MaxLayerRise;

    /// <summary>The nozzle reached <paramref name="height"/>: a new layer, or only a new current layer.</summary>
    private bool Reached(double height, bool lowerSetsTheLayer)
    {
        if (double.IsNaN(_layer))
        {
            _layer = height;
            return false;
        }

        double rise = height - _layer;
        if (rise > MaxLayerRise || (lowerSetsTheLayer && rise <= -SameHeight))
        {
            _layer = height;
            return false;
        }

        // The tolerance keeps a rise of exactly 0.05 mm (e.g. 10.05 - 10.0 in binary) a layer.
        if (rise < MinLayerRise - 1e-9)
        {
            return false;
        }

        _layer = height;
        LayerChanges++;
        return true;
    }
}
