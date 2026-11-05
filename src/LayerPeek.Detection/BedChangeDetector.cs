using System.Runtime.InteropServices;

using LayerPeek.Core.Detection;

namespace LayerPeek.Detection;

/// <summary>
/// "Change on the bed" (<c>bed-change</c>, T-155, algorithm of the T-152 spike): a part that vanished or moved, or new mass
/// on the bed (spaghetti, a part torn off). Classic computer vision in pure C#:
/// <list type="number">
/// <item><b>Background without moving parts:</b> an approximate running median (McFarlane &amp; Schofield, 1995). Every
/// sample moves each pixel of the background at most <see cref="BedChangeOptions.Rate"/> levels per second towards the
/// image, so the head and the X bridge, which never stay long, fade out; the bed, the part and anything that stays on
/// the bed remain. O(1) per pixel and no history of frames.</item>
/// <item><b>Reference:</b> a few older copies of the background (<see cref="BedChangeOptions.ReferenceCount"/>, one every
/// <see cref="BedChangeOptions.ReferenceSpacing"/> or every <see cref="BedChangeOptions.ReferenceZSpacing"/> of Z), the
/// oldest one (up to ~5 min) is compared. Never a fixed picture of the empty bed: the bed moves in Z during the print.</item>
/// <item><b>Change:</b> a pixel whose background (3×3 mean) lies more than <see cref="BedChangeOptions.ChangeLevel"/>
/// levels outside the range of the reference around it (<see cref="BedChangeOptions.ShiftX"/>,
/// <see cref="BedChangeOptions.ShiftY"/>: the bed moving in Z shifts the image a little), after the reference was scaled
/// to the brightness of the current background (a dimmer or brighter light is no change), and that has been still for
/// <see cref="BedChangeOptions.Quiet"/> (where the head passes over and over, the background flips between it and the
/// bed).</item>
/// <item><b>Score:</b> the changed pixels relative to <see cref="BedChangeOptions.ScoreArea"/> of the frame, the same for
/// a failure of a given size whatever the size of the region.</item>
/// <item><b>When it judges:</b> after <see cref="BedChangeOptions.WarmUp"/> (2 min) and only above the first layers
/// (<see cref="BedChangeOptions.FirstLayersHeight"/>), which add mass to the bed by design.</item>
/// </list>
/// <b>Dark filament mode</b> (T-163, experimental, off by default, <see cref="PrintFailureContext.DarkFilament"/>): a
/// background darker than the reference counts from <see cref="BedChangeOptions.DarkFilamentLevel"/> (10) instead of
/// <see cref="BedChangeOptions.ChangeLevel"/> (14) levels; brightening is unchanged.
/// Known limits (T-152, T-155; texts in T-154): dark filament on a black sheet barely changes the image (T-163); the quality
/// of the first layer and a blob on the nozzle cannot be seen from the camera; no judgement during the first layers; mass
/// right under the moving head counts only where the head leaves it still; a change that builds up slower than the
/// reference age is taken for the print growing; a region with the moving X gantry in it (the whole frame of a Core One)
/// and an abrupt change of the whole picture (the camera switching to night vision, a moved camera) can raise false alarms.
/// </summary>
/// <remarks>
/// One instance per printer and stretch of printing, called on one thread at a time (<see cref="IPrintFailureDetector"/>).
/// The buffers are allocated with the first sample (or when the region changes size) and reused: no allocation per sample.
/// Time is the sample's <see cref="DetectionSample.Time"/> only.
/// </remarks>
public sealed class BedChangeDetector : IPrintFailureDetector
{
    /// <summary>The settings key and alarm kind.</summary>
    public const string DetectorId = "bed-change";

    /// <summary>A level from which a pixel counts as saturated (its true brightness may be higher).</summary>
    private const int SaturatedLevel = 250;

    /// <summary>A darkening weight (sixteenths) that counts a darkening as it is.</summary>
    private const int NeutralWeight = 16;

    private readonly BedChangeOptions _options;
    private readonly long _warmUpTicks;
    private readonly long _fastLearningTicks;
    private readonly long _spacingTicks;
    private readonly long _maxGapTicks;
    private readonly double _ratePerTick;
    private readonly double _warmUpRatePerTick;
    private readonly int _quietSeconds;

    /// <summary>
    /// How much a darkening counts against <see cref="BedChangeOptions.ChangeLevel"/> in the "dark filament" mode, in
    /// sixteenths: <see cref="BedChangeOptions.DarkFilamentLevel"/> becomes the effective level for darkening.
    /// </summary>
    private readonly int _darkFilamentWeight;
    private readonly byte[] _gainLow = new byte[256];
    private readonly byte[] _gainHigh = new byte[256];

    // Every buffer is laid out as the region only (row after row, Width × Height), whatever the size of the frame.
    private DetectionPixelRegion _region;
    private byte[] _background = [];
    private byte[] _previous = [];
    private byte[] _stillSeconds = [];
    private byte[] _references = [];
    private long[] _referenceSums = [];
    private byte[] _low = [];
    private byte[] _high = [];
    private ushort[] _rowSums = [];

    /// <summary>The darkening weight of the stretch: <see cref="NeutralWeight"/> or, in the dark filament mode, <see cref="_darkFilamentWeight"/>.</summary>
    private int _darkeningWeight = NeutralWeight;
    private bool _darkFilament;
    private bool _started;
    private bool _aboveFirstLayers;
    private bool _heightKnown;
    private long _start;
    private long _last;
    private double _pendingStep;
    private double _pendingSeconds;
    private long _backgroundSum;
    private long _nextReference;
    private double _lastReferenceHeight;
    private int _referencesTaken;
    private int _oldest;
    private int _rangeOf = -1;

    public BedChangeDetector()
        : this(BedChangeOptions.Default)
    {
    }

    internal BedChangeDetector(BedChangeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.WarmUp, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.FastLearning, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.FastLearning, options.WarmUp);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.WarmUpRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Rate);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.MaxSampleGap, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ReferenceSpacing, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ReferenceCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ReferenceZSpacing);
        ArgumentOutOfRangeException.ThrowIfNegative(options.FirstLayersHeight);
        ArgumentOutOfRangeException.ThrowIfNegative(options.ShiftX);
        ArgumentOutOfRangeException.ThrowIfNegative(options.ShiftY);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ChangeLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.ChangeLevel, 254);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DarkFilamentLevel);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.DarkFilamentLevel, options.ChangeLevel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MotionLevel);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Quiet, TimeSpan.Zero);
        // Stillness is counted in whole seconds up to a byte.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.Quiet, TimeSpan.FromSeconds(byte.MaxValue));
        if (!(options.ScoreArea is > 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.ScoreArea, "The score area is a share of the frame, 0 (excluded) to 1.");
        }

        _options = options;
        _warmUpTicks = options.WarmUp.Ticks;
        _fastLearningTicks = options.FastLearning.Ticks;
        _spacingTicks = options.ReferenceSpacing.Ticks;
        _maxGapTicks = options.MaxSampleGap.Ticks;
        _ratePerTick = options.Rate / TimeSpan.TicksPerSecond;
        _warmUpRatePerTick = options.WarmUpRate / TimeSpan.TicksPerSecond;
        _quietSeconds = (int)Math.Ceiling(options.Quiet.TotalSeconds);
        _darkFilamentWeight = (int)Math.Round((double)NeutralWeight * options.ChangeLevel / options.DarkFilamentLevel);
    }

    /// <summary>
    /// The detector the app offers. Thresholds in changed pixels relative to <see cref="BedChangeOptions.ScoreArea"/>
    /// (T-155 tuning on the synthetic failures and the successful print, kept by T-166 over many seeds, see the task
    /// notes): sensitivity 0 = 0.5 % (~90 pixels of 320×180), 100 = 0.1 % (~18), the default 50 = 0.3 % (~54 pixels, a
    /// lump of filament about 1 cm across at the front of a Core One bed).
    /// </summary>
    public static PrintFailureDetectorRegistration Registration { get; } = new(
        DetectorId,
        DetectionFrameSpec.Default,
        new DetectionThresholds(leastSensitive: 0.005f, mostSensitive: 0.001f),
        static () => new BedChangeDetector());

    /// <inheritdoc/>
    public void Reset(in PrintFailureContext context) => _started = false;

    /// <inheritdoc/>
    public DetectionResult Analyze(in DetectionSample sample)
    {
        long now = sample.Time.Ticks;
        if (!_started || sample.Region != _region || sample.Context.DarkFilament != _darkFilament)
        {
            // The first sample of a stretch, another region or mode: everything is learned from it anew.
            Start(sample, now);
            return new DetectionResult(IsWarmedUp: false, 0);
        }

        double? height = sample.Context.AxisZ;
        if (!_aboveFirstLayers)
        {
            // The first layers are new mass on the bed by design: learn them, and warm up from the first sample above.
            _aboveFirstLayers = IsAboveFirstLayers(height);
            _start = now;
        }
        else if (!_heightKnown && height is { } z && z < _options.FirstLayersHeight)
        {
            // The stretch began without a height (no telemetry yet), and the first one says the first layers are still
            // being laid down: back to learning them, and what was kept meanwhile shows the bed before them.
            _aboveFirstLayers = false;
            _start = now;
            ForgetReferences();
        }

        _heightKnown |= height is not null;

        bool warm = now - _start >= _warmUpTicks;
        bool fast = now - _start < _fastLearningTicks;
        long gap = Math.Clamp(now - _last, 0, _maxGapTicks);
        _last = Math.Max(_last, now);
        _pendingStep = Math.Min(_pendingStep + (gap * (fast ? _warmUpRatePerTick : _ratePerTick)), byte.MaxValue);
        int step = (int)_pendingStep;
        _pendingStep -= step;
        _pendingSeconds = Math.Min(_pendingSeconds + ((double)gap / TimeSpan.TicksPerSecond), byte.MaxValue);
        int seconds = (int)_pendingSeconds;
        _pendingSeconds -= seconds;
        Learn(sample, step, seconds);
        if (!warm)
        {
            return new DetectionResult(IsWarmedUp: false, 0);
        }

        if (_referencesTaken == 0 || now >= _nextReference
            || (height is { } rose && rose - _lastReferenceHeight >= _options.ReferenceZSpacing))
        {
            KeepReference(now, height ?? double.NaN);
        }

        if (_rangeOf != _oldest)
        {
            ComputeRange();
        }

        int changed = CountChanged();
        float score = (float)Math.Min(1, changed / (_options.ScoreArea * sample.Width * sample.Height));
        return new DetectionResult(IsWarmedUp: true, score);
    }

    private void Start(in DetectionSample sample, long now)
    {
        DetectionPixelRegion region = sample.Region;
        int area = region.Area;
        if (_background.Length != area)
        {
            _background = new byte[area];
            _previous = new byte[area];
            _stillSeconds = new byte[area];
            _references = new byte[area * _options.ReferenceCount];
            _referenceSums = new long[_options.ReferenceCount];
            _low = new byte[area];
            _high = new byte[area];
            _rowSums = new ushort[area];
        }

        _region = region;
        _darkFilament = sample.Context.DarkFilament;
        _darkeningWeight = _darkFilament ? _darkFilamentWeight : NeutralWeight;
        long sum = 0;
        for (int y = 0; y < region.Height; y++)
        {
            ReadOnlySpan<byte> row = sample.Row(region.Y + y).Slice(region.X, region.Width);
            row.CopyTo(_background.AsSpan(y * region.Width, region.Width));
            foreach (byte value in row)
            {
                sum += value;
            }
        }

        _background.CopyTo(_previous, 0);
        Array.Clear(_stillSeconds);
        _backgroundSum = sum;
        _started = true;
        _aboveFirstLayers = IsAboveFirstLayers(sample.Context.AxisZ);
        _heightKnown = sample.Context.AxisZ is not null;
        _start = now;
        _last = now;
        _pendingStep = 0;
        _pendingSeconds = 0;
        ForgetReferences();
    }

    private void ForgetReferences()
    {
        _referencesTaken = 0;
        _lastReferenceHeight = double.NaN;
        _oldest = 0;
        _rangeOf = -1;
    }

    /// <summary>Whether <paramref name="height"/> is above the first layers; an unknown height is (only the warm-up applies).</summary>
    private bool IsAboveFirstLayers(double? height) => height is not { } z || z >= _options.FirstLayersHeight;

    /// <summary>
    /// One pass over the region: moves every background pixel at most <paramref name="step"/> levels towards the sample,
    /// and counts how long each pixel has been still (<paramref name="seconds"/> more, or 0 when it moved).
    /// </summary>
    private void Learn(in DetectionSample sample, int step, int seconds)
    {
        DetectionPixelRegion region = _region;
        int motion = _options.MotionLevel;
        long sum = 0;
        for (int y = 0; y < region.Height; y++)
        {
            int offset = y * region.Width;
            ReadOnlySpan<byte> image = sample.Row(region.Y + y).Slice(region.X, region.Width);
            Span<byte> background = _background.AsSpan(offset, region.Width);
            Span<byte> previous = _previous.AsSpan(offset, region.Width);
            Span<byte> still = _stillSeconds.AsSpan(offset, region.Width);
            for (int x = 0; x < image.Length; x++)
            {
                int target = image[x];
                still[x] = Math.Abs(target - previous[x]) > motion ? (byte)0 : (byte)Math.Min(byte.MaxValue, still[x] + seconds);
                previous[x] = (byte)target;
                int current = background[x];
                int moved = target > current ? Math.Min(target, current + step) : Math.Max(target, current - step);
                background[x] = (byte)moved;
                sum += moved;
            }
        }

        _backgroundSum = sum;
    }

    /// <summary>
    /// Copies the background into the next free slot or over the oldest reference, every
    /// <see cref="BedChangeOptions.ReferenceSpacing"/> or <see cref="BedChangeOptions.ReferenceZSpacing"/> higher.
    /// </summary>
    private void KeepReference(long now, double height)
    {
        int count = _options.ReferenceCount;
        int slot = _referencesTaken < count ? _referencesTaken : _oldest;
        _background.CopyTo(_references.AsSpan(slot * _background.Length, _background.Length));
        _referenceSums[slot] = _backgroundSum;
        _lastReferenceHeight = height;
        if (_referencesTaken < count)
        {
            _referencesTaken++;
        }
        else
        {
            _oldest = (_oldest + 1) % count;
            // The slot was overwritten: when there is only one, its range is stale.
            _rangeOf = count == 1 ? -1 : _rangeOf;
        }

        _nextReference = now + _spacingTicks;
    }

    /// <summary>
    /// The lowest and highest level of the oldest reference within ±<see cref="BedChangeOptions.ShiftX"/>,
    /// ±<see cref="BedChangeOptions.ShiftY"/> of every pixel (separable: rows, then columns). Once per reference.
    /// </summary>
    private void ComputeRange()
    {
        ReadOnlySpan<byte> reference = _references.AsSpan(_oldest * _background.Length, _background.Length);
        Extremes(reference, _low, lowest: true);
        Extremes(reference, _high, lowest: false);
        _rangeOf = _oldest;
    }

    private void Extremes(ReadOnlySpan<byte> reference, Span<byte> result, bool lowest)
    {
        int width = _region.Width;
        int height = _region.Height;
        int shiftX = _options.ShiftX;
        int shiftY = _options.ShiftY;
        // The row sums are not in use meanwhile: their memory holds the row pass.
        Span<byte> rows = MemoryMarshal.AsBytes(_rowSums.AsSpan())[..reference.Length];
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> source = reference.Slice(y * width, width);
            Span<byte> target = rows.Slice(y * width, width);
            for (int x = 0; x < width; x++)
            {
                ReadOnlySpan<byte> window = source[Math.Max(0, x - shiftX)..Math.Min(width, x + shiftX + 1)];
                target[x] = lowest ? MinOf(window) : MaxOf(window);
            }
        }

        for (int y = 0; y < height; y++)
        {
            int first = Math.Max(0, y - shiftY);
            int last = Math.Min(height - 1, y + shiftY);
            Span<byte> target = result.Slice(y * width, width);
            rows.Slice(first * width, width).CopyTo(target);
            for (int other = first + 1; other <= last; other++)
            {
                ReadOnlySpan<byte> source = rows.Slice(other * width, width);
                for (int x = 0; x < width; x++)
                {
                    target[x] = lowest ? Math.Min(target[x], source[x]) : Math.Max(target[x], source[x]);
                }
            }
        }
    }

    private static byte MinOf(ReadOnlySpan<byte> values)
    {
        byte min = byte.MaxValue;
        foreach (byte value in values)
        {
            min = Math.Min(min, value);
        }

        return min;
    }

    private static byte MaxOf(ReadOnlySpan<byte> values)
    {
        byte max = 0;
        foreach (byte value in values)
        {
            max = Math.Max(max, value);
        }

        return max;
    }

    /// <summary>
    /// The still pixels whose 3×3 mean of the background's distance outside the (brightness-scaled) reference range
    /// exceeds the change level; the outermost row and column of the region have no full neighbourhood and never count.
    /// </summary>
    private int CountChanged()
    {
        int width = _region.Width;
        int height = _region.Height;
        if (width < 3 || height < 3)
        {
            return 0;
        }

        FillGain(LightRatio());
        ReadOnlySpan<byte> gainLow = _gainLow;
        ReadOnlySpan<byte> gainHigh = _gainHigh;

        // Horizontal sums of three distances per pixel (at most 3 × 255).
        for (int y = 0; y < height; y++)
        {
            int offset = y * width;
            ReadOnlySpan<byte> background = _background.AsSpan(offset, width);
            ReadOnlySpan<byte> low = _low.AsSpan(offset, width);
            ReadOnlySpan<byte> high = _high.AsSpan(offset, width);
            Span<ushort> sums = _rowSums.AsSpan(offset, width);
            int weight = _darkeningWeight;
            int left = Outside(background[0], gainLow[low[0]], gainHigh[high[0]], weight);
            int middle = Outside(background[1], gainLow[low[1]], gainHigh[high[1]], weight);
            for (int x = 1; x < width - 1; x++)
            {
                int right = Outside(background[x + 1], gainLow[low[x + 1]], gainHigh[high[x + 1]], weight);
                sums[x] = (ushort)(left + middle + right);
                left = middle;
                middle = right;
            }
        }

        // Vertical sums of three rows: the 3×3 sum, compared with 9 × the level.
        int limit = 9 * _options.ChangeLevel;
        int quiet = _quietSeconds;
        int changed = 0;
        for (int y = 1; y < height - 1; y++)
        {
            ReadOnlySpan<ushort> above = _rowSums.AsSpan((y - 1) * width, width);
            ReadOnlySpan<ushort> row = _rowSums.AsSpan(y * width, width);
            ReadOnlySpan<ushort> below = _rowSums.AsSpan((y + 1) * width, width);
            ReadOnlySpan<byte> still = _stillSeconds.AsSpan(y * width, width);
            for (int x = 1; x < width - 1; x++)
            {
                if (above[x] + row[x] + below[x] > limit && still[x] >= quiet)
                {
                    changed++;
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// How far <paramref name="value"/> lies outside <paramref name="low"/>–<paramref name="high"/> (0 inside), below it
    /// weighted by <paramref name="darkening"/> sixteenths (16: as it is; at most 255 either way, the row sums are 16 bit).
    /// </summary>
    private static int Outside(int value, int low, int high, int darkening) =>
        Math.Max(0, Math.Max(value - high, Math.Min(byte.MaxValue, ((low - value) * darkening) >> 4)));

    /// <summary>
    /// How much brighter the background is than the reference (a dimmer or brighter chamber light is no change): the
    /// ratio of the region's sums, then again over only the pixels that agree under that ratio, so that the mass of a
    /// failure does not bias it. Clamped to 0.5–2: beyond that it is no light change.
    /// </summary>
    private double LightRatio()
    {
        long referenceSum = _referenceSums[_oldest];
        double ratio = referenceSum > 0 ? Math.Clamp((double)_backgroundSum / referenceSum, 0.5, 2) : 1;
        ReadOnlySpan<byte> background = _background;
        ReadOnlySpan<byte> reference = _references.AsSpan(_oldest * background.Length, background.Length);
        int scale = (int)(ratio * 65536);
        int agree = 2 * _options.ChangeLevel;
        long backgroundAgreeing = 0;
        long referenceAgreeing = 0;
        for (int i = 0; i < background.Length; i++)
        {
            int before = reference[i];
            if (Math.Abs(background[i] - ((before * scale) >> 16)) <= agree)
            {
                backgroundAgreeing += background[i];
                referenceAgreeing += before;
            }
        }

        return referenceAgreeing > 0 ? Math.Clamp((double)backgroundAgreeing / referenceAgreeing, 0.5, 2) : ratio;
    }

    /// <summary>
    /// The lookups that scale the reference range by <paramref name="ratio"/>. A saturated reference level (highlights of
    /// the part, the LED strip) stands for "this bright or brighter", so its upper end stays white.
    /// </summary>
    private void FillGain(double ratio)
    {
        for (int level = 0; level < _gainLow.Length; level++)
        {
            byte scaled = (byte)Math.Min(byte.MaxValue, (int)((level * ratio) + 0.5));
            _gainLow[level] = scaled;
            _gainHigh[level] = level >= SaturatedLevel ? byte.MaxValue : scaled;
        }
    }
}
