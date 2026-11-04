namespace LayerPeek.Detection;

/// <summary>
/// Fixed parameters of <see cref="BedChangeDetector"/> (T-155). All durations are in time, never in samples, so that the
/// detector behaves the same at every sampling interval of 2–5 s (and with dropped samples). Tests shorten them.
/// </summary>
internal sealed record BedChangeOptions
{
    public static BedChangeOptions Default { get; } = new();

    /// <summary>
    /// How long after a <see cref="BedChangeDetector.Reset"/> (or the first layers) the detector only learns before it
    /// judges (T-152: 1–2 min): first quickly (<see cref="FastLearning"/>, the head and the X bridge of the first sample
    /// fade out), then at the normal <see cref="Rate"/>, so that what the quick learning caught of the moving head fades
    /// too before the first reference is kept.
    /// </summary>
    public TimeSpan WarmUp { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>The first part of <see cref="WarmUp"/>, learning at <see cref="WarmUpRate"/>.</summary>
    public TimeSpan FastLearning { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How fast the background follows the image during <see cref="FastLearning"/>, in gray levels per second.</summary>
    public double WarmUpRate { get; init; } = 4;

    /// <summary>
    /// How fast the background follows the image afterwards, in gray levels per second (the "±K levels per sample" of the
    /// running median, scaled by the time between samples). Slow enough that the moving head stays out of it, fast enough
    /// that new mass on the bed shows within a minute.
    /// </summary>
    public double Rate { get; init; } = 1;

    /// <summary>
    /// The longest gap between two samples that counts in full; a longer one (dropped samples, a stalled stream) counts as
    /// this long, so one sample never pulls the background far.
    /// </summary>
    public TimeSpan MaxSampleGap { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How often a copy of the background is kept for the comparison ("the background some minutes ago").</summary>
    public TimeSpan ReferenceSpacing { get; init; } = TimeSpan.FromSeconds(75);

    /// <summary>
    /// Copies kept; the oldest is the reference, so it is up to <see cref="ReferenceCount"/> × <see cref="ReferenceSpacing"/>
    /// old (T-152: ~5 min). Slow changes of a good print (the part grows, the bed moves in Z) stay small within that time.
    /// </summary>
    public int ReferenceCount { get; init; } = 4;

    /// <summary>
    /// A reference is also kept when the Z axis rose this much since the last one (when the printer reports it), so that
    /// on a fast-growing print the oldest reference is at most <see cref="ReferenceCount"/> × this lower than the bed now
    /// and the image moved too little to count as a change.
    /// </summary>
    public double ReferenceZSpacing { get; init; } = 0.5;

    /// <summary>
    /// Below this height of the Z axis (mm) the first layers are being laid down: new mass on the bed by design, so the
    /// detector only learns and does not judge (its warm-up starts above). The margin covers a Z hop on the first layer.
    /// Without a reported height only the warm-up applies.
    /// </summary>
    public double FirstLayersHeight { get; init; } = 1.0;

    /// <summary>
    /// How far (pixels, left and right) a structure of the reference may have moved and still match: the bed and the part
    /// move slowly in the image while the bed goes down in Z.
    /// </summary>
    public int ShiftX { get; init; } = 1;

    /// <summary>The same up and down (the bed moving in Z moves the image mostly vertically).</summary>
    public int ShiftY { get; init; } = 2;

    /// <summary>
    /// Gray levels by which the background of a pixel (its 3×3 mean) must lie outside the reference of its neighbourhood
    /// to count as changed (T-152: 25, above the H.264 noise).
    /// </summary>
    public int ChangeLevel { get; init; } = 25;

    /// <summary>
    /// Gray levels by which a pixel must differ from the previous sample to count as moving (the head or the X bridge
    /// passing; an image that is still differs by the noise only).
    /// </summary>
    public int MotionLevel { get; init; } = 20;

    /// <summary>
    /// How long a pixel must have been still before its change counts (0: always): where the head and the X bridge pass
    /// over and over, the running median flips between them and the bed, which is no failure. Mass that stays counts
    /// this long after it came to rest.
    /// </summary>
    public TimeSpan Quiet { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// The area the changed pixels are measured against, as a share of the frame: the score is changed pixels divided by
    /// this area, whatever the size of the region, so a failure of a given size scores the same in a small region and in
    /// the whole frame. 0.315 is the Core One bed region of the T-152 prototype (70 % × 45 %), where its numbers come from.
    /// </summary>
    public double ScoreArea { get; init; } = 0.70 * 0.45;
}
