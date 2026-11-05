using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Detection;

/// <summary>
/// The scores at which a detector raises an alarm at the ends of the sensitivity scale (T-153):
/// <see cref="DetectorSettings.Sensitivity"/> 0 uses <see cref="LeastSensitive"/>, 100 <see cref="MostSensitive"/>, values
/// in between interpolate linearly. E.g. T-152 proposes 6 % – 2 % of the changed pixels for "bed-change".
/// </summary>
public readonly record struct DetectionThresholds
{
    /// <exception cref="ArgumentOutOfRangeException">A score is outside 0–1, or the most sensitive one is higher than the least sensitive.</exception>
    public DetectionThresholds(float leastSensitive, float mostSensitive)
    {
        if (!(leastSensitive is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(leastSensitive), leastSensitive, "A score threshold is within 0–1.");
        }

        if (!(mostSensitive >= 0 && mostSensitive <= leastSensitive))
        {
            throw new ArgumentOutOfRangeException(nameof(mostSensitive), mostSensitive, "The most sensitive threshold is within 0 and the least sensitive one.");
        }

        LeastSensitive = leastSensitive;
        MostSensitive = mostSensitive;
    }

    public float LeastSensitive { get; }

    public float MostSensitive { get; }

    /// <summary>The score threshold of <paramref name="sensitivity"/> (0–100, clamped).</summary>
    public float ThresholdFor(int sensitivity)
    {
        float share = Math.Clamp(sensitivity, DetectorSettings.MinSensitivity, DetectorSettings.MaxSensitivity) / (float)DetectorSettings.MaxSensitivity;
        // Exactly the given thresholds at both ends.
        return (LeastSensitive * (1 - share)) + (MostSensitive * share);
    }
}

/// <summary>
/// A kind of detector the app offers (T-153), registered explicitly by the composition root (no scanning): what the
/// settings (T-154) and the orchestration need to know without creating one, and how to create one per printer and print.
/// </summary>
public sealed record PrintFailureDetectorRegistration
{
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a valid <see cref="DetectorSettings"/> key.</exception>
    public PrintFailureDetectorRegistration(string id, DetectionFrameSpec frameSpec, DetectionThresholds thresholds, Func<IPrintFailureDetector> create)
    {
        if (!DetectorSettings.IsValidId(id))
        {
            throw new ArgumentException("A detector id is 1–40 lower-case ASCII letters, digits and dashes.", nameof(id));
        }

        if (frameSpec == default)
        {
            throw new ArgumentException("The frame spec is empty.", nameof(frameSpec));
        }

        if (thresholds == default)
        {
            throw new ArgumentException("The thresholds are empty.", nameof(thresholds));
        }

        ArgumentNullException.ThrowIfNull(create);
        Id = id;
        FrameSpec = frameSpec;
        Thresholds = thresholds;
        Create = create;
    }

    /// <summary>Stable, e.g. <c>bed-change</c>: the key of its settings and of its alarms; never shown (the UI has texts for it).</summary>
    public string Id { get; }

    /// <summary>What it analyses; every registered detector currently has to use the same one (one sample stream per printer).</summary>
    public DetectionFrameSpec FrameSpec { get; }

    public DetectionThresholds Thresholds { get; }

    /// <summary>A new detector for one printer and one stretch of printing.</summary>
    public Func<IPrintFailureDetector> Create { get; }

    /// <summary>
    /// Whether the detector of a printer without its own settings is on (T-167, user decision 2026-11-04): <see langword="true"/>
    /// (the default) for the detectors of the first version; a new, experimental one is off after an update until the user
    /// switches it on. See <see cref="DefaultSettings"/>.
    /// </summary>
    public bool EnabledByDefault { get; init; } = true;

    /// <summary>The settings shows it as experimental (T-167: "Experimental", as the timelapse of T-161).</summary>
    public bool IsExperimental { get; init; }

    /// <summary>
    /// The printer models it is offered for (T-167: a detector that needs a bed moving in Z); <see langword="null"/> (the
    /// default): every printer, also one of an unknown model. A printer of another model neither shows nor runs it.
    /// </summary>
    public IReadOnlyList<PrinterModel>? Models
    {
        get;
        init => field = value is null ? null : [.. value];
    }

    /// <summary>The settings of a printer that has none of its own for this detector.</summary>
    public DetectorSettings DefaultSettings => EnabledByDefault ? DetectorSettings.Default : DetectorSettings.Disabled;

    /// <summary>Whether a printer of <paramref name="model"/> is offered this detector (see <see cref="Models"/>).</summary>
    public bool IsOfferedFor(PrinterModel model) => Models is not { } models || models.Contains(model);

    /// <summary>Whether <paramref name="printer"/> is offered this detector (its model: <see cref="DetectionRegion.ModelOf(PrinterDefinition)"/>).</summary>
    public bool IsOfferedFor(PrinterDefinition printer) => Models is null || IsOfferedFor(DetectionRegion.ModelOf(printer));
}
