using System.Runtime.InteropServices;

using LayerPeek.Core.Printers;

namespace LayerPeek.Core.History;

/// <summary>
/// One recorded poll of a printer in <see cref="TelemetryHistory"/>: a compact (48 bytes), reference-free value,
/// so that a day of samples is a single array without per-sample heap objects.
/// </summary>
/// <remarks>
/// <para>
/// Values the data source did not provide, and every value of a snapshot without telemetry (offline, unauthorized...),
/// are <see cref="float.NaN"/>. A chart breaks a line at <see cref="float.NaN"/> and before a sample with
/// <see cref="StartsSegment"/>, so it never draws across an outage.
/// </para>
/// <para>
/// Precision: temperatures, fans and Z are stored as <see cref="float"/> (~7 significant digits). Speed, flow and
/// progress are stored as <see cref="Half"/>: whole percentages up to 2048 are exact, progress has a resolution of at
/// least 0.0625 percentage points. A value the storage type cannot represent (it would become infinite, e.g. a speed
/// above <see cref="Half.MaxValue"/> = 65 504 %) is stored as NaN, like an unknown value.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct TelemetrySample
{
    private const byte HasTelemetryFlag = 1;
    private const byte StartsSegmentFlag = 2;

    // Ordered from the largest to the smallest field to avoid padding: 8 + 7 × 4 + 3 × 2 + 2 × 1 = 44, aligned to 48.
    private readonly long _timestampUtcTicks;
    private readonly float _nozzleActual;
    private readonly float _nozzleTarget;
    private readonly float _bedActual;
    private readonly float _bedTarget;
    private readonly float _hotendFanRpm;
    private readonly float _printFanRpm;
    private readonly float _axisZ;
    private readonly Half _speedPercent;
    private readonly Half _flowPercent;
    private readonly Half _progressPercent;
    private readonly byte _state;
    // Init-only so that WithStartsSegment can use a with expression; still a plain byte field.
    private byte Flags { get; init; }

    private TelemetrySample(PrinterSnapshot snapshot)
    {
        _timestampUtcTicks = snapshot.Timestamp.UtcTicks;
        _state = ToByte(snapshot.State);
        _progressPercent = snapshot.Job is { } job ? ToHalf(job.ProgressPercent) : Half.NaN;

        if (snapshot.Telemetry is not { } telemetry)
        {
            _nozzleActual = _nozzleTarget = _bedActual = _bedTarget = float.NaN;
            _hotendFanRpm = _printFanRpm = _axisZ = float.NaN;
            _speedPercent = _flowPercent = Half.NaN;
            return;
        }

        Flags = HasTelemetryFlag;
        (_nozzleActual, _nozzleTarget) = ToFloats(telemetry.Nozzle);
        (_bedActual, _bedTarget) = ToFloats(telemetry.Bed);
        _hotendFanRpm = telemetry.HotendFanRpm ?? float.NaN;
        _printFanRpm = telemetry.PrintFanRpm ?? float.NaN;
        _axisZ = telemetry.AxisZ is { } z ? ToFloat(z) : float.NaN;
        _speedPercent = telemetry.SpeedPercent is { } speed ? ToHalf(speed) : Half.NaN;
        _flowPercent = telemetry.FlowPercent is { } flow ? ToHalf(flow) : Half.NaN;
    }

    /// <summary>When the snapshot was taken, in UTC.</summary>
    public DateTimeOffset Timestamp => new(_timestampUtcTicks, TimeSpan.Zero);

    /// <summary><see cref="Timestamp"/> as UTC ticks; convenient for computing chart coordinates.</summary>
    public long TimestampUtcTicks => _timestampUtcTicks;

    /// <summary>State of the printer; undefined values are stored as <see cref="PrinterState.Unknown"/>.</summary>
    public PrinterState State => (PrinterState)_state;

    /// <summary>The snapshot had telemetry; <see langword="false"/> for gap samples (offline, unauthorized...), whose values are all NaN.</summary>
    public bool HasTelemetry => (Flags & HasTelemetryFlag) != 0;

    /// <summary>
    /// The sample must not be connected to the previous one: it is the first sample of the printer, or no sample was
    /// recorded for longer than <see cref="TelemetryHistory.GapThreshold"/> of the polling interval (e.g. the computer slept).
    /// </summary>
    public bool StartsSegment => (Flags & StartsSegmentFlag) != 0;

    /// <summary>Nozzle temperature in °C, NaN when unknown.</summary>
    public float NozzleActual => _nozzleActual;

    /// <summary>Nozzle target temperature in °C (0 = heater off), NaN when unknown.</summary>
    public float NozzleTarget => _nozzleTarget;

    /// <summary>Bed temperature in °C, NaN when unknown.</summary>
    public float BedActual => _bedActual;

    /// <summary>Bed target temperature in °C (0 = heater off), NaN when unknown.</summary>
    public float BedTarget => _bedTarget;

    /// <summary>Hotend fan speed in RPM, NaN when unknown.</summary>
    public float HotendFanRpm => _hotendFanRpm;

    /// <summary>Print fan speed in RPM, NaN when unknown.</summary>
    public float PrintFanRpm => _printFanRpm;

    /// <summary>Z axis position in millimetres, NaN when unknown.</summary>
    public float AxisZ => _axisZ;

    /// <summary>Print speed multiplier in percent, NaN when unknown.</summary>
    public float SpeedPercent => (float)_speedPercent;

    /// <summary>Flow multiplier in percent, NaN when unknown.</summary>
    public float FlowPercent => (float)_flowPercent;

    /// <summary>Job progress in percent, NaN when there is no job.</summary>
    public float ProgressPercent => (float)_progressPercent;

    internal static TelemetrySample FromSnapshot(PrinterSnapshot snapshot) => new(snapshot);

    /// <summary>Copy of this sample that starts (or does not start) a new line segment.</summary>
    internal TelemetrySample WithStartsSegment(bool startsSegment) =>
        this with { Flags = (byte)(startsSegment ? Flags | StartsSegmentFlag : Flags & ~StartsSegmentFlag) };

    /// <summary>Values a <see cref="float"/> cannot represent (they would become ±∞) and non-finite input become NaN ("unknown").</summary>
    private static float ToFloat(double value)
    {
        float result = (float)value;
        return float.IsFinite(result) ? result : float.NaN;
    }

    /// <summary>Values beyond ±<see cref="Half.MaxValue"/> (they would become ±∞) and non-finite input become NaN ("unknown").</summary>
    private static Half ToHalf(double value)
    {
        Half result = (Half)value;
        return Half.IsFinite(result) ? result : Half.NaN;
    }

    private static (float Actual, float Target) ToFloats(Temperature? temperature) =>
        temperature is { } t ? (ToFloat(t.Actual), t.Target is { } target ? ToFloat(target) : float.NaN) : (float.NaN, float.NaN);

    private static byte ToByte(PrinterState state) =>
        state is >= 0 and <= (PrinterState)byte.MaxValue && Enum.IsDefined(state) ? (byte)state : (byte)PrinterState.Unknown;
}
