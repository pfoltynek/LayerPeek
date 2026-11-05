using LayerPeek.Core.Detection;
using LayerPeek.Core.Printers;

namespace LayerPeek.Detection;

/// <summary>
/// "Printing into the air" (<c>air-printing</c>, T-167): the printer goes on printing (the Z axis rises) but nothing grows on
/// the bed any more (filament ran out unnoticed, a slipping extruder, a clogged nozzle, MMU filament that did not load).
/// Only for printers whose bed moves down in Z under a nozzle that stays in place (Core One, XL; see
/// <see cref="Registration"/>). Classic computer vision in pure C#, driven by the reported height of the Z axis:
/// <list type="number">
/// <item><b>Where:</b> the printer's bed region, extended up to <see cref="AirPrintingOptions.NozzleRow"/> (user decision
/// 2026-11-04): the top edges of the parts stand at the nozzle, above the bed region of a Core One.</item>
/// <item><b>Background:</b> an approximate running median as in <see cref="BedChangeDetector"/>, so the moving head fades out.</item>
/// <item><b>Height:</b> the lowest <see cref="PrintFailureContext.AxisZ"/> of the last
/// <see cref="AirPrintingOptions.HeightWindow"/> (a Z hop adds nothing). Without a reported height nothing is judged. A drop
/// of <see cref="AirPrintingOptions.NewBaseDrop"/> starts over (start sequence, sequential printing).</item>
/// <item><b>References:</b> a copy of the background every <see cref="AirPrintingOptions.ReferenceZSpacing"/> of Z (never by
/// time: while the Z axis stands, e.g. an MMU tool change, nothing new is compared).</item>
/// <item><b>Still or moving with the bed:</b> the background now and the reference <see cref="AirPrintingOptions.CompareHeight"/>
/// lower are split into tiles; every tile with structure gets its best vertical shift (sum of absolute differences without
/// the brightness offset, sub-pixel). The bed's shift is the median of the moving tiles (T-167 phase 1: ~0.5 px per mm at
/// 320×180). A tile is still, moving with the bed, or unknown (between, no structure, the bed too still to tell).</item>
/// <item><b>Growth tiles:</b> still tiles with the moving bed above them (behind the part) and below them (in front of its
/// face) that the head was over lately (<see cref="AirPrintingOptions.HeadLevel"/>) and that stayed still for
/// <see cref="AirPrintingOptions.StillStreak"/> of Z: the top edges of the parts, which stand at the nozzle while the bed goes
/// down. The frame beside the bed, the walls and the head itself fail one of these (T-167 phase 1 on the real series).</item>
/// <item><b>Score:</b> of the tiles that were growth tiles within the last <see cref="AirPrintingOptions.GrowthMemory"/> of Z,
/// the share that moves with the bed now, and the lowest such share of the last <see cref="AirPrintingOptions.PersistHeight"/>
/// of Z. So the alarm needs (almost) all of them to stop growing for a while (user decision 2026-11-04): one finished part
/// of several, or the lower step of a part, scores only its share, at least once the top edge above it is confirmed. Fewer
/// than <see cref="AirPrintingOptions.MinGrowthTiles"/> judged growth tiles: not judged.</item>
/// </list>
/// Known limits (T-167 notes, texts in the app): the alarm comes ~3 mm of Z after the failure (minutes at a fast print, half
/// an hour and more at a slow one); nothing is judged during the first ~6 mm of Z after the start (or after a reset: resume,
/// camera reconnect, light back on); a part mostly hidden under the head has too few growth tiles; a wipe tower that keeps
/// growing (MMU) can hide a failure; a part whose wider lower step ends (or several parts of which most end) can raise a
/// false alarm while the growing rest is not confirmed yet; a part that vanished is left to bed-change.
/// </summary>
/// <remarks>
/// One instance per printer and stretch of printing, called on one thread at a time (<see cref="IPrintFailureDetector"/>).
/// The buffers are allocated with the first sample (or when the region changes size) and reused: no allocation per sample.
/// Time is the sample's <see cref="DetectionSample.Time"/> only.
/// </remarks>
public sealed class AirPrintingDetector : IPrintFailureDetector
{
    /// <summary>The settings key and alarm kind.</summary>
    public const string DetectorId = "air-printing";

    private const byte Unknown = 0;
    private const byte Still = 1;
    private const byte Moving = 2;

    /// <summary>Heights remembered for <see cref="AirPrintingOptions.HeightWindow"/> (a sample every 2 s fills 8 of 15 s).</summary>
    private const int HeightCapacity = 32;

    /// <summary>Scores remembered for <see cref="AirPrintingOptions.PersistHeight"/> (1.5 mm at 0.1 mm/min and a sample every 2 s: 450).</summary>
    private const int ScoreCapacity = 512;

    private static readonly DetectionResult NotWarm = new(IsWarmedUp: false, 0);

    private readonly AirPrintingOptions _options;
    private readonly long _fastLearningTicks;
    private readonly long _warmUpTicks;
    private readonly long _maxGapTicks;
    private readonly long _heightWindowTicks;
    private readonly double _ratePerTick;
    private readonly double _warmUpRatePerTick;
    private readonly int _referenceCount;
    private readonly long[] _heightTimes = new long[HeightCapacity];
    private readonly double[] _heights = new double[HeightCapacity];
    private readonly double[] _referenceHeights;
    private readonly double[] _scoreLevels = new double[ScoreCapacity];
    private readonly float[] _scores = new float[ScoreCapacity];

    // The analysed area (the region extended up to the nozzle row); every buffer is laid out as it, row after row.
    private DetectionPixelRegion _region;
    private DetectionPixelRegion _area;
    private int _tilesX;
    private int _tiles;
    private byte[] _background = [];
    private byte[] _references = [];
    private double[] _growthAt = [];
    private double[] _stillFrom = [];
    private double[] _headAt = [];
    private int[] _tileDifferences = [];
    private byte[] _classes = [];
    private float[] _shifts = [];
    private float[] _sorted = [];
    private int[] _rowSums = [];

    private bool _started;
    private long _start;
    private long _last;
    private double _pendingStep;
    private int _heightCount;
    private int _heightNext;
    private double _maxLevel;
    private int _referencesTaken;
    private int _scoreCount;
    private int _scoreNext;
    private int _newest;

    /// <summary>The analysed area of the last sample in pixels: the region extended up to the nozzle row (tests).</summary>
    internal DetectionPixelRegion Area => _area;

    /// <summary>The height the detector works with after the last sample (tests); <see langword="null"/>: none reported.</summary>
    internal double? Level { get; private set; }

    /// <summary>How often a new base was found in this stretch (tests: the start sequence, sequential printing).</summary>
    internal int NewBases { get; private set; }

    /// <summary>The bed's shift of the last comparison in pixels (tests); NaN: none.</summary>
    internal double BedShift { get; private set; } = double.NaN;

    /// <summary>The growth tiles judged in the last sample (tests).</summary>
    internal int GrowthTiles { get; private set; }

    public AirPrintingDetector()
        : this(AirPrintingOptions.Default)
    {
    }

    internal AirPrintingDetector(AirPrintingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!(options.NozzleRow is >= 0 and < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.NozzleRow, "The nozzle row is a share of the frame height, 0 to 1 (excluded).");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.FastLearning, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.WarmUp, options.FastLearning);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.WarmUpRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Rate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxSampleGap, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.HeightWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.HeightSamples, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.HeightSamples, HeightCapacity);
        ArgumentOutOfRangeException.ThrowIfNegative(options.FirstLayersHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.NewBaseDrop);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ReferenceZSpacing);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CompareHeight, options.ReferenceZSpacing);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.TileSize, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.TileSize, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxShiftDown, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxShiftDown, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxShiftUp, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.MaxShiftUp, 32);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MinTexture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MinBedShift);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MinBedTiles, 1);
        if (!(options.StillShare > 0 && options.StillShare < options.MovingShare && options.MovingShare < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.StillShare, "0 < still share < moving share < 1.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.GrowthReach, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.GrowthReachBelow, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.StillStreak);
        ArgumentOutOfRangeException.ThrowIfNegative(options.PersistHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.HeadLevel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.GrowthMemory);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MinGrowthTiles, 1);
        _options = options;
        _fastLearningTicks = options.FastLearning.Ticks;
        _warmUpTicks = options.WarmUp.Ticks;
        _maxGapTicks = options.MaxSampleGap.Ticks;
        _heightWindowTicks = options.HeightWindow.Ticks;
        _ratePerTick = options.Rate / TimeSpan.TicksPerSecond;
        _warmUpRatePerTick = options.WarmUpRate / TimeSpan.TicksPerSecond;
        // References between the compared height and now, the compared one and the newest (heights at least a spacing apart).
        _referenceCount = (int)Math.Ceiling(options.CompareHeight / options.ReferenceZSpacing) + 2;
        _referenceHeights = new double[_referenceCount];
    }

    /// <summary>
    /// The detector the app offers (user decisions 2026-11-04): off until the user switches it on (experimental), only for
    /// printers whose bed moves in Z under a fixed nozzle. Thresholds in the share of the growth tiles that move with the bed
    /// (T-167 tuning, see the task notes).
    /// </summary>
    public static PrintFailureDetectorRegistration Registration { get; } = new(
        DetectorId,
        DetectionFrameSpec.Default,
        new DetectionThresholds(leastSensitive: 0.8f, mostSensitive: 0.2f),
        static () => new AirPrintingDetector())
    {
        EnabledByDefault = false,
        IsExperimental = true,
        Models = [PrinterModel.CoreOne, PrinterModel.CoreOneL, PrinterModel.Xl, PrinterModel.XlPlus],
    };

    /// <inheritdoc/>
    public void Reset(in PrintFailureContext context) => _started = false;

    /// <inheritdoc/>
    public DetectionResult Analyze(in DetectionSample sample)
    {
        long now = sample.Time.Ticks;
        if (!_started || sample.Region != _region)
        {
            Start(sample, now);
            return NotWarm;
        }

        long gap = Math.Clamp(now - _last, 0, _maxGapTicks);
        _last = Math.Max(_last, now);
        bool fast = now - _start < _fastLearningTicks;
        _pendingStep = Math.Min(_pendingStep + (gap * (fast ? _warmUpRatePerTick : _ratePerTick)), byte.MaxValue);
        int step = (int)_pendingStep;
        _pendingStep -= step;
        GrowthTiles = 0;
        BedShift = double.NaN;
        Level = HeightOf(now, sample.Context.AxisZ);
        if (Level is not { } level || _tiles == 0)
        {
            Learn(sample, step, double.NaN);
            return NotWarm;
        }

        if (level < _maxLevel - _options.NewBaseDrop)
        {
            // The start sequence (40, 15, then 0.2 mm) or the next object of a sequential print: a new base.
            ForgetReferences();
            _maxLevel = level;
            NewBases++;
        }

        _maxLevel = Math.Max(_maxLevel, level);
        bool warm = now - _start >= _warmUpTicks && level >= _options.FirstLayersHeight;
        Learn(sample, step, warm ? level : double.NaN);
        if (!warm)
        {
            return NotWarm;
        }

        if (_referencesTaken == 0 || level >= _referenceHeights[_newest] + _options.ReferenceZSpacing)
        {
            KeepReference(level);
        }

        int reference = ReferenceBelow(level - _options.CompareHeight);
        if (reference < 0 || Measure(Reference(reference), _background, _classes) is not { } bed)
        {
            return NotWarm;
        }

        BedShift = bed;
        MarkGrowth(level);

        // The growth tiles of the last GrowthMemory of Z: still ones grow on, moving ones stopped growing.
        double recent = level - _options.GrowthMemory;
        int judged = 0;
        int moving = 0;
        for (int t = 0; t < _tiles; t++)
        {
            if (_growthAt[t] >= recent && _classes[t] != Unknown)
            {
                judged++;
                moving += _classes[t] == Moving ? 1 : 0;
            }
        }

        GrowthTiles = judged;
        if (judged < _options.MinGrowthTiles)
        {
            _scoreCount = 0;
            return NotWarm;
        }

        return new DetectionResult(IsWarmedUp: true, Persisting(level, (float)moving / judged));
    }

    /// <summary>
    /// The lowest score of the last <see cref="AirPrintingOptions.PersistHeight"/> of Z (0 until the scores reach back that
    /// far): growth must have stopped over that height. A lower step of a part that ends looks like stopped growth until the
    /// top edge of the part above it is confirmed (<see cref="AirPrintingOptions.StillStreak"/>); a failure leaves nothing.
    /// </summary>
    private float Persisting(double level, float score)
    {
        _scoreLevels[_scoreNext] = level;
        _scores[_scoreNext] = score;
        _scoreNext = (_scoreNext + 1) % ScoreCapacity;
        _scoreCount = Math.Min(_scoreCount + 1, ScoreCapacity);
        double from = level - _options.PersistHeight;
        float lowest = score;
        bool reached = false;
        for (int k = 1; k < _scoreCount; k++)
        {
            int i = (_scoreNext - 1 - k + ScoreCapacity) % ScoreCapacity;
            lowest = Math.Min(lowest, _scores[i]);
            if (_scoreLevels[i] <= from)
            {
                reached = true;
                break;
            }
        }

        // A full ring (a very slow print) reaches back as far as it can.
        return reached || _scoreCount == ScoreCapacity ? lowest : 0;
    }

    private void Start(in DetectionSample sample, long now)
    {
        DetectionPixelRegion region = sample.Region;
        int top = Math.Min(region.Y, (int)Math.Round(_options.NozzleRow * sample.Height, MidpointRounding.AwayFromZero));
        DetectionPixelRegion area = new(region.X, top, region.Width, region.Bottom - top);
        int size = _options.TileSize;
        if (area.Width != _area.Width || area.Height != _area.Height || _background.Length == 0)
        {
            _tilesX = area.Width / size;
            _tiles = _tilesX * (area.Height / size);
            _background = new byte[area.Area];
            _references = new byte[area.Area * _referenceCount];
            _growthAt = new double[_tiles];
            _stillFrom = new double[_tiles];
            _headAt = new double[_tiles];
            _tileDifferences = new int[_tilesX];
            _classes = new byte[_tiles];
            _shifts = new float[_tiles];
            _sorted = new float[_tiles];
            _rowSums = new int[area.Height * _tilesX];
        }

        _region = region;
        _area = area;
        for (int y = 0; y < area.Height; y++)
        {
            sample.Row(area.Y + y).Slice(area.X, area.Width).CopyTo(_background.AsSpan(y * area.Width, area.Width));
        }

        _started = true;
        _start = now;
        _last = now;
        _pendingStep = 0;
        _heightCount = 0;
        _heightNext = 0;
        NewBases = 0;
        ForgetReferences();
        GrowthTiles = 0;
        BedShift = double.NaN;
        Level = HeightOf(now, sample.Context.AxisZ);
        _maxLevel = Level ?? double.NegativeInfinity;
    }

    private void ForgetReferences()
    {
        _referencesTaken = 0;
        _newest = 0;
        _scoreCount = 0;
        _growthAt.AsSpan().Fill(double.NegativeInfinity);
        _stillFrom.AsSpan().Fill(double.NaN);
        _headAt.AsSpan().Fill(double.NegativeInfinity);
    }

    /// <summary>
    /// One pass over the area: moves every background pixel at most <paramref name="step"/> levels towards the sample and,
    /// unless <paramref name="headLevel"/> is NaN, marks the tiles the head (or the X gantry) is over now at that height.
    /// </summary>
    private void Learn(in DetectionSample sample, int step, double headLevel)
    {
        DetectionPixelRegion area = _area;
        int size = _options.TileSize;
        int tilesX = _tilesX;
        int tileRows = _tiles / Math.Max(1, tilesX);
        bool markHead = !double.IsNaN(headLevel);
        int headLimit = _options.HeadLevel * size * size;
        Span<int> differences = _tileDifferences;
        differences.Clear();
        for (int y = 0; y < area.Height; y++)
        {
            ReadOnlySpan<byte> image = sample.Row(area.Y + y).Slice(area.X, area.Width);
            Span<byte> background = _background.AsSpan(y * area.Width, area.Width);
            int tileRow = y / size;
            bool inTiles = markHead && tileRow < tileRows;
            for (int x = 0; x < image.Length; x++)
            {
                int target = image[x];
                int current = background[x];
                if (inTiles && x < tilesX * size)
                {
                    differences[x / size] += Math.Abs(target - current);
                }

                background[x] = (byte)(target > current ? Math.Min(target, current + step) : Math.Max(target, current - step));
            }

            if (inTiles && y % size == size - 1)
            {
                // A tile row is complete: the tiles that differ much from the background have the head (or the X gantry)
                // over them now.
                for (int c = 0; c < tilesX; c++)
                {
                    if (differences[c] > headLimit)
                    {
                        _headAt[(tileRow * tilesX) + c] = headLevel;
                    }
                }

                differences.Clear();
            }
        }
    }

    /// <summary>
    /// Remembers <paramref name="height"/> (when reported) and returns the lowest height of the last
    /// <see cref="AirPrintingOptions.HeightWindow"/>; <see langword="null"/> when none was reported within it.
    /// </summary>
    private double? HeightOf(long now, double? height)
    {
        if (height is { } z && double.IsFinite(z))
        {
            _heightTimes[_heightNext] = now;
            _heights[_heightNext] = z;
            _heightNext = (_heightNext + 1) % HeightCapacity;
            _heightCount = Math.Min(_heightCount + 1, HeightCapacity);
        }

        // The reports of the window, and at least the latest HeightSamples ones (newest first).
        double lowest = double.PositiveInfinity;
        for (int k = 0; k < _heightCount; k++)
        {
            int i = (_heightNext - 1 - k + HeightCapacity) % HeightCapacity;
            if (k >= _options.HeightSamples && now - _heightTimes[i] > _heightWindowTicks)
            {
                break;
            }

            lowest = Math.Min(lowest, _heights[i]);
        }

        return double.IsPositiveInfinity(lowest) || now - _heightTimes[(_heightNext - 1 + HeightCapacity) % HeightCapacity] > _heightWindowTicks
            ? null
            : lowest;
    }

    /// <summary>Copies the background into the next slot of the ring (over the oldest reference).</summary>
    private void KeepReference(double height)
    {
        int slot = _referencesTaken == 0 ? 0 : (_newest + 1) % _referenceCount;
        _background.CopyTo(_references.AsSpan(slot * _background.Length, _background.Length));
        _referenceHeights[slot] = height;
        _newest = slot;
        _referencesTaken = Math.Min(_referencesTaken + 1, _referenceCount);
    }

    /// <summary>
    /// Marks the growth tiles of the comparison at <paramref name="level"/>: still tiles the head was over lately, with a tile
    /// moving with the bed above them (within <see cref="AirPrintingOptions.GrowthReach"/>, the same column: the bed behind
    /// the part) and below them (within <see cref="AirPrintingOptions.GrowthReachBelow"/>: the bed in front of the part's
    /// face). Those are the top edges of the parts, which stand at the nozzle while the bed goes down; the frame beside the
    /// bed has more frame above or below it, the head and the X gantry have no bed above them.
    /// </summary>
    private void MarkGrowth(double level)
    {
        double recent = level - _options.GrowthMemory;
        for (int t = _tilesX; t < _tiles; t++)
        {
            if (_classes[t] == Moving)
            {
                // A still streak ends (a tile that mixes a still edge with the moving bed flips between the two).
                _stillFrom[t] = double.NaN;
            }
            else if (_classes[t] == Still && _headAt[t] >= recent
                && MovingWithin(t, -_tilesX, _options.GrowthReach) && MovingWithin(t, _tilesX, _options.GrowthReachBelow))
            {
                if (double.IsNaN(_stillFrom[t]))
                {
                    _stillFrom[t] = level;
                }

                if (level - _stillFrom[t] >= _options.StillStreak)
                {
                    _growthAt[t] = level;
                }
            }
        }
    }

    /// <summary>Whether a tile moving with the bed is within <paramref name="reach"/> tiles of <paramref name="tile"/> in steps of <paramref name="step"/>.</summary>
    private bool MovingWithin(int tile, int step, int reach)
    {
        for (int other = tile + step, k = 0; other >= 0 && other < _tiles && k < reach; other += step, k++)
        {
            if (_classes[other] == Moving)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The newest kept reference at or below <paramref name="height"/>; -1: none.</summary>
    private int ReferenceBelow(double height)
    {
        int found = -1;
        for (int i = 0; i < _referencesTaken; i++)
        {
            if (_referenceHeights[i] <= height && (found < 0 || _referenceHeights[i] > _referenceHeights[found]))
            {
                found = i;
            }
        }

        return found;
    }

    private ReadOnlySpan<byte> Reference(int slot) => _references.AsSpan(slot * _background.Length, _background.Length);

    /// <summary>
    /// The vertical shift of every tile from <paramref name="before"/> to <paramref name="after"/>, the bed's shift (median of
    /// the tiles that moved down) and the class of every tile into <paramref name="classes"/>. <see langword="null"/> (and
    /// the classes unknown) when too few tiles show a moving bed.
    /// </summary>
    private double? Measure(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after, Span<byte> classes)
    {
        MeasureShifts(before, after);
        double minimum = _options.MinBedShift * 0.5;
        int moved = 0;
        foreach (float shift in _shifts)
        {
            if (shift >= minimum)
            {
                _sorted[moved++] = shift;
            }
        }

        if (moved < _options.MinBedTiles)
        {
            classes.Clear();
            return null;
        }

        Span<float> sorted = _sorted.AsSpan(0, moved);
        sorted.Sort();
        double bed = sorted[moved / 2];
        if (bed < _options.MinBedShift)
        {
            classes.Clear();
            return null;
        }
        double still = _options.StillShare * bed;
        double moving = _options.MovingShare * bed;
        double most = (2 * bed) + 1;
        for (int t = 0; t < _tiles; t++)
        {
            float shift = _shifts[t];
            classes[t] = float.IsNaN(shift) ? Unknown
                : Math.Abs(shift) <= still ? Still
                : shift >= moving && shift <= most ? Moving
                : Unknown;
        }

        return bed;
    }

    /// <summary>
    /// For every tile of <paramref name="before"/> with structure: the shift (pixels, positive down, sub-pixel) at which
    /// <paramref name="after"/> matches it best, by the sum of absolute differences without the tiles' brightness offset;
    /// NaN for a tile without structure.
    /// </summary>
    private void MeasureShifts(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        int width = _area.Width;
        int rows = _area.Height;
        int size = _options.TileSize;
        int tilesX = _tilesX;
        int up = _options.MaxShiftUp;
        int down = _options.MaxShiftDown;
        int pixels = size * size;
        Span<int> sads = stackalloc int[up + down + 1];

        // Sums of every row of every tile column of the later background (for the brightness offset at each shift).
        for (int y = 0; y < rows; y++)
        {
            ReadOnlySpan<byte> row = after.Slice(y * width, width);
            Span<int> sums = _rowSums.AsSpan(y * tilesX, tilesX);
            for (int c = 0; c < tilesX; c++)
            {
                int sum = 0;
                foreach (byte value in row.Slice(c * size, size))
                {
                    sum += value;
                }

                sums[c] = sum;
            }
        }

        for (int t = 0; t < _tiles; t++)
        {
            int column = t % tilesX;
            int left = column * size;
            int top = t / tilesX * size;
            int texture = 0;
            int sumBefore = 0;
            for (int y = top; y < top + size; y++)
            {
                ReadOnlySpan<byte> row = before.Slice((y * width) + left, size);
                ReadOnlySpan<byte> next = y + 1 < top + size ? before.Slice(((y + 1) * width) + left, size) : default;
                for (int x = 0; x < size; x++)
                {
                    sumBefore += row[x];
                    if (!next.IsEmpty)
                    {
                        texture += Math.Abs(next[x] - row[x]);
                    }
                }
            }

            if (texture < _options.MinTexture * size * (size - 1))
            {
                _shifts[t] = float.NaN;
                continue;
            }

            int best = -1;
            for (int s = 0; s < sads.Length; s++)
            {
                int dy = s - up;
                if (top + dy < 0 || top + dy + size > rows)
                {
                    sads[s] = int.MaxValue;
                    continue;
                }

                int sumAfter = 0;
                for (int y = top + dy; y < top + dy + size; y++)
                {
                    sumAfter += _rowSums[(y * tilesX) + column];
                }

                int offset = (int)Math.Round((sumAfter - sumBefore) / (double)pixels, MidpointRounding.AwayFromZero);
                int sad = 0;
                for (int y = 0; y < size; y++)
                {
                    ReadOnlySpan<byte> a = before.Slice(((top + y) * width) + left, size);
                    ReadOnlySpan<byte> b = after.Slice(((top + dy + y) * width) + left, size);
                    for (int x = 0; x < size; x++)
                    {
                        sad += Math.Abs(a[x] + offset - b[x]);
                    }
                }

                sads[s] = sad;
                if (best < 0 || sad < sads[best])
                {
                    best = s;
                }
            }

            if (best < 0)
            {
                _shifts[t] = float.NaN;
                continue;
            }

            double refined = best - up;
            if (best > 0 && best < sads.Length - 1 && sads[best - 1] != int.MaxValue && sads[best + 1] != int.MaxValue)
            {
                // The parabola through the best shift and its neighbours.
                double curvature = sads[best - 1] - (2.0 * sads[best]) + sads[best + 1];
                if (curvature > 0)
                {
                    refined += 0.5 * (sads[best - 1] - sads[best + 1]) / curvature;
                }
            }

            _shifts[t] = (float)refined;
        }
    }
}
