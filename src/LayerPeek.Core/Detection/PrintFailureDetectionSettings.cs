using System.Collections.ObjectModel;

namespace LayerPeek.Core.Detection;

/// <summary>How often a lasting print failure is announced (T-153, user decision 2026-11-04).</summary>
public enum PrintFailureAlarmRepeat
{
    /// <summary>One alarm per print and kind of failure (the default).</summary>
    OncePerPrint,

    /// <summary>Again every <see cref="PrintFailureDetectionOptions.RepeatInterval"/> (10 min) while the failure is still confirmed.</summary>
    WhilePersisting,
}

/// <summary>
/// Global settings of the print failure detection (T-153, user decisions 2026-11-04; edited on the "Recording" settings tab,
/// T-154). Whether a printer is watched, with which sensitivity and region is per printer
/// (<see cref="PrinterDetectionSettings"/>). Persisted as the <c>detection</c> section of <c>settings.json</c>, written only
/// while something differs from the defaults.
/// </summary>
public sealed record PrintFailureDetectionSettings
{
    /// <summary>Default of <see cref="SampleInterval"/> (proposal of the architect: the T-152 prototype, about every key frame of a Buddy3D).</summary>
    public static readonly TimeSpan DefaultSampleInterval = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan MinSampleInterval = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan MaxSampleInterval = TimeSpan.FromSeconds(5);

    public static PrintFailureDetectionSettings Default { get; } = new();

    /// <summary>The master switch: off (the default after installation, user decision 2026-11-04), nothing is watched.</summary>
    public bool Enabled { get; init; }

    /// <summary>One alarm per print and kind (the default), or repeated while the failure lasts.</summary>
    public PrintFailureAlarmRepeat AlarmRepeat
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown alarm repeat mode.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Save a JPEG of the camera at every alarm into the printer's recording folder (on by default, user decision
    /// 2026-11-04): next to the recordings of T-145 and counted in their size limit (the rotation deletes the oldest files).
    /// </summary>
    public bool SaveSnapshots { get; init; } = true;

    /// <summary>
    /// How often a printer is sampled while it prints: the first key frame after this interval. Clamped into
    /// <see cref="MinSampleInterval"/>–<see cref="MaxSampleInterval"/> (user decision 2026-11-04).
    /// </summary>
    public TimeSpan SampleInterval
    {
        get;
        init => field = ClampSampleInterval(value);
    } = DefaultSampleInterval;

    /// <summary><paramref name="value"/> clamped into the allowed interval (whole milliseconds).</summary>
    public static TimeSpan ClampSampleInterval(TimeSpan value) =>
        TimeSpan.FromMilliseconds(Math.Round((value < MinSampleInterval ? MinSampleInterval : value > MaxSampleInterval ? MaxSampleInterval : value).TotalMilliseconds));
}

/// <summary>Settings of one detector at one printer (T-153; edited in T-154).</summary>
public sealed record DetectorSettings
{
    public const int MinSensitivity = 0;

    public const int MaxSensitivity = 100;

    public const int DefaultSensitivity = 50;

    /// <summary>Longest detector id.</summary>
    public const int MaxIdLength = 40;

    /// <summary>
    /// A detector of a printer whose detection is on is on itself unless switched off (the gate after installation is the
    /// master switch and the printer's switch, both off).
    /// </summary>
    public static DetectorSettings Default { get; } = new();

    public bool Enabled { get; init; } = true;

    /// <summary>0 (fewest alarms) – 100 (most); see <see cref="DetectionThresholds"/>. Clamped into the range.</summary>
    public int Sensitivity
    {
        get;
        init => field = Math.Clamp(value, MinSensitivity, MaxSensitivity);
    } = DefaultSensitivity;

    /// <summary>1–<see cref="MaxIdLength"/> characters <c>a-z</c>, <c>0-9</c> and <c>-</c> (also a part of file names).</summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return false;
        }

        foreach (char c in id)
        {
            if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The print failure detection of one printer (T-153, user decisions 2026-11-04; edited next to its camera in T-154). Off
/// by default. Kept while the camera is disabled. Persisted as <c>printers[*].detection</c>, written only while something
/// differs from the defaults.
/// </summary>
public sealed record PrinterDetectionSettings
{
    public static PrinterDetectionSettings Default { get; } = new();

    /// <summary>Watch this printer's prints (with <see cref="PrintFailureDetectionSettings.Enabled"/> and an enabled camera).</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// The bed region set by the user; <see langword="null"/> while never set: the printer then uses
    /// <see cref="DetectionRegion.DefaultFor"/> (the Core One region or the whole frame), see <see cref="RegionFor"/>.
    /// </summary>
    public DetectionRegion? Region { get; init; }

    /// <summary>
    /// Settings of the detectors that differ from <see cref="DetectorSettings.Default"/>, by detector id (case-sensitive,
    /// valid ids only). A read-only copy; ids of detectors this version does not know are kept (a newer version wrote them).
    /// </summary>
    /// <exception cref="ArgumentException">An id is not valid or a value is <see langword="null"/>.</exception>
    public IReadOnlyDictionary<string, DetectorSettings> Detectors
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            Dictionary<string, DetectorSettings> copy = new(value.Count, StringComparer.Ordinal);
            foreach ((string id, DetectorSettings settings) in value)
            {
                if (!DetectorSettings.IsValidId(id) || settings is null)
                {
                    throw new ArgumentException("Detector settings need valid ids and values.", nameof(value));
                }

                copy[id] = settings;
            }

            field = copy.AsReadOnly();
        }
    } = ReadOnlyDictionary<string, DetectorSettings>.Empty;

    /// <summary>The settings of detector <paramref name="id"/>: its own, or <see cref="DetectorSettings.Default"/>.</summary>
    public DetectorSettings For(string id) => Detectors.GetValueOrDefault(id) ?? DetectorSettings.Default;

    /// <summary>The region detection uses for <paramref name="printer"/>: <see cref="Region"/>, or its default.</summary>
    public DetectionRegion RegionFor(Printers.PrinterDefinition printer) => Region ?? DetectionRegion.DefaultFor(printer);

    /// <summary>Value equality including <see cref="Detectors"/>' content.</summary>
    public bool Equals(PrinterDetectionSettings? other) =>
        other is not null && Enabled == other.Enabled && Region == other.Region && SameDetectors(Detectors, other.Detectors);

    public override int GetHashCode() => HashCode.Combine(Enabled, Region, Detectors.Count);

    private static bool SameDetectors(IReadOnlyDictionary<string, DetectorSettings> a, IReadOnlyDictionary<string, DetectorSettings> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach ((string id, DetectorSettings settings) in a)
        {
            if (!b.TryGetValue(id, out DetectorSettings? other) || settings != other)
            {
                return false;
            }
        }

        return true;
    }
}
