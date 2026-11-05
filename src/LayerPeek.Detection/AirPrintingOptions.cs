namespace LayerPeek.Detection;

/// <summary>
/// Fixed parameters of <see cref="AirPrintingDetector"/> (T-167). Durations are in time and heights in mm of the Z axis,
/// never in samples, so that the detector behaves the same at every sampling interval of 2–5 s (and with dropped samples).
/// Tests shorten them.
/// </summary>
internal sealed record AirPrintingOptions
{
    public static AirPrintingOptions Default { get; } = new();

    /// <summary>
    /// The row of the nozzle as a share of the frame height (T-167 phase 1: the top edges of the parts of the real Core One
    /// series lie at 0.31–0.56, the head above them): the analysed area reaches up to here even when the printer's bed region
    /// starts lower (user decision 2026-11-04, question 1 c), so that it contains the top edges of tall parts.
    /// </summary>
    public double NozzleRow { get; init; } = 0.28;

    /// <summary>How long after the first sample the background learns quickly (<see cref="WarmUpRate"/>).</summary>
    public TimeSpan FastLearning { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long after the first sample no reference is kept (what the quick learning caught of the head fades first).</summary>
    public TimeSpan WarmUp { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>How fast the background follows the image during <see cref="FastLearning"/>, in gray levels per second.</summary>
    public double WarmUpRate { get; init; } = 4;

    /// <summary>
    /// How fast the background (an approximate running median, as in <see cref="BedChangeDetector"/>) follows the image
    /// afterwards, in gray levels per second: the head passing over the top edge of the part stays out of it.
    /// </summary>
    public double Rate { get; init; } = 1;

    /// <summary>The longest gap between two samples that counts in full (dropped samples, a stalled stream).</summary>
    public TimeSpan MaxSampleGap { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The height of the Z axis is the lowest one reported within this time, and of at least
    /// <see cref="HeightSamples"/> reports, so that a Z hop (0.2–1.5 mm while travelling, seconds long) does not count as growth.
    /// </summary>
    public TimeSpan HeightWindow { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>The fewest reports the height is the lowest of (at a sample every 5 s, 15 s are only three).</summary>
    public int HeightSamples { get; init; } = 4;

    /// <summary>Below this height (mm) the first layers are laid down: the detector only learns (as <see cref="BedChangeOptions.FirstLayersHeight"/>).</summary>
    public double FirstLayersHeight { get; init; } = 1.0;

    /// <summary>
    /// A height this much (mm) below the highest one of the stretch is a new base: the start sequence of the printer (40, 15,
    /// then 0.2 mm), sequential printing. What was learned about the parts is forgotten. More than a Z hop (up to 1.5 mm), in
    /// case one ever gets through <see cref="HeightWindow"/>.
    /// </summary>
    public double NewBaseDrop { get; init; } = 2.0;

    /// <summary>A copy of the background is kept every time the Z axis rose this much (mm), never by time.</summary>
    public double ReferenceZSpacing { get; init; } = 0.5;

    /// <summary>
    /// How much (mm) the Z axis must have risen between the compared backgrounds. T-167 phase 1: the bed moves ~0.5 px/mm in a
    /// 320×180 sample (more at a low Z), the still top edges of the part by ±0.15 px: 3 mm moves the bed ~1.5 px, clearly
    /// apart from a still edge, at the price of an alarm 2–3 mm of Z after the failure.
    /// </summary>
    public double CompareHeight { get; init; } = 3.0;

    /// <summary>Side of the square tiles (pixels) whose vertical shift is measured.</summary>
    public int TileSize { get; init; } = 8;

    /// <summary>The largest shift searched downwards (pixels; the bed moves down when the Z axis rises).</summary>
    public int MaxShiftDown { get; init; } = 6;

    /// <summary>The largest shift searched upwards (pixels): the noise of a still tile is searched on both sides.</summary>
    public int MaxShiftUp { get; init; } = 2;

    /// <summary>
    /// Mean absolute difference of vertically neighbouring pixels (gray levels) from which a tile has enough structure to
    /// measure its shift; the uniform sheet and the side of a part have less.
    /// </summary>
    public int MinTexture { get; init; } = 5;

    /// <summary>
    /// The bed moved at least this much (pixels, the median shift of the moving tiles) between the compared backgrounds; less
    /// (a fixed bed, a camera on the bed, a very far camera) and nothing is judged.
    /// </summary>
    public double MinBedShift { get; init; } = 1.0;

    /// <summary>Tiles that must show the moving bed for its shift to count.</summary>
    public int MinBedTiles { get; init; } = 8;

    /// <summary>A tile counts as still when it moved at most this share of the bed's shift.</summary>
    public double StillShare { get; init; } = 0.25;

    /// <summary>A tile moves with the bed when it moved at least this share of the bed's shift (and at most twice it and a pixel).</summary>
    public double MovingShare { get; init; } = 0.5;

    /// <summary>
    /// How many tiles above a still tile (the same column) are searched for the moving bed: a top edge of a part has the bed
    /// behind the part above it (beyond its top surface), a wall or the frame has more of itself.
    /// </summary>
    public int GrowthReach { get; init; } = 4;

    /// <summary>
    /// How many tiles below a still tile (the same column) are searched for the moving bed: below a top edge is the part's
    /// face (no structure, ~0.7 px per mm of its height) and then the bed in front of it.
    /// </summary>
    public int GrowthReachBelow { get; init; } = 10;

    /// <summary>
    /// Mean difference (gray levels) of a tile of a sample from the background from which the head (or the X gantry) is over
    /// it. A growth tile must have had the head over it within <see cref="GrowthMemory"/>: the head prints the top edges of
    /// the parts (T-167 phase 1: 16–94 % of the samples over the edge tiles of the real series), never the frame beside the bed.
    /// </summary>
    public int HeadLevel { get; init; } = 12;

    /// <summary>
    /// How far (mm of Z) a tile must have stayed still (never moving with the bed meanwhile; unknown does not end it) before it
    /// counts as a growth tile: a tile that mixes a still edge with the moving bed behind it flips between the two.
    /// </summary>
    public double StillStreak { get; init; } = 1.0;

    /// <summary>
    /// How long (mm of Z) a tile counts as a growth tile after it was last seen as one: when the part stops growing, its top
    /// edges move with the bed and count as stopped for this height (a good print sees them as growth tiles again at once).
    /// </summary>
    public double GrowthMemory { get; init; } = 4.0;

    /// <summary>
    /// The score is the lowest one of this height of Z (mm): growth must have stopped over it. Longer than
    /// <see cref="StillStreak"/>, so that the top edge of a part above a lower step that ended is confirmed meanwhile.
    /// </summary>
    public double PersistHeight { get; init; } = 1.5;

    /// <summary>
    /// Fewest growth tiles (the still top edges of the parts) that must be judged; a smaller part (mostly hidden under the head)
    /// is not judged.
    /// </summary>
    public int MinGrowthTiles { get; init; } = 5;
}
