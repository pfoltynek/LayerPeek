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
}
