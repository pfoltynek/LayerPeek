namespace LayerPeek.Tests.Detection;

/// <summary>
/// A synthetic print on a Core One for the "printing into the air" detector (T-167, made of the committed fixtures only, like
/// <see cref="SyntheticPrint"/>): the bed (and everything on it) moves down in the image as the Z axis rises, the parts' top
/// edges stand at the nozzle, the head and the X gantry move over them (the key frames of the clip), and the Z axis follows a
/// course modelled on job 354 (start sequence, layers, Z hops, MMU tool changes). Geometry of T-167 phase 1: the bed moves
/// <c>k(Z) = 0.9 / (1 + Z / 62.5)</c> px per mm at 320×180 (0.9 at the nozzle, 0.5 at 50 mm: the measured 0.48 px/mm of the
/// real series, and its part face of 35 px at 50 mm), times <see cref="KScale"/> and a little more at the front than at the
/// back. A failure stops all growth at <see cref="FailureHeight"/>: the parts then ride on the bed.
/// </summary>
internal sealed record SyntheticGrowingPrint
{
    public const int Width = SyntheticPrint.Width;

    public const int Height = SyntheticPrint.Height;

    /// <summary>The height the fixture's bed is at (its part's face is ~35 px tall).</summary>
    private const double FixtureHeight = 50;

    /// <summary>The sampling interval of the orchestration (T-153: 2–5 s).</summary>
    public required TimeSpan Interval { get; init; }

    /// <summary>Growth of the Z axis while printing (mm per minute; job 354: ~0.2–0.4).</summary>
    public double Rate { get; init; } = 0.4;

    /// <summary>Layer height (mm).</summary>
    public double Layer { get; init; } = 0.2;

    /// <summary>The last layer's height (mm): the print ends there.</summary>
    public double FinalHeight { get; init; } = 30;

    /// <summary>The printer's start sequence before the first layer (Z at 40, 15, then near 0 for the bed mesh).</summary>
    public bool StartSequence { get; init; } = true;

    /// <summary>Height of a Z hop while travelling (mm; 0: none). About once a minute for 1–3 s.</summary>
    public double ZHop { get; init; } = 0.2;

    /// <summary>MMU tool changes: every this many mm of Z the Z axis stands for <see cref="ToolChange"/> (0: none).</summary>
    public double ToolChangeEvery { get; init; }

    public TimeSpan ToolChange { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The parts; their top edges at the nozzle are where their footprint was at Z = 0.</summary>
    public IReadOnlyList<SyntheticPart> Parts { get; init; } = [SyntheticPart.Box()];

    /// <summary>All growth stops at this height (null: a successful print).</summary>
    public double? FailureHeight { get; init; }

    /// <summary>The parts vanish at the failure (knocked off) instead of riding on the bed.</summary>
    public bool PartsGone { get; init; }

    /// <summary>The bed's motion in the image relative to the model (0.8–1.2: other camera mounts).</summary>
    public double KScale { get; init; } = 1;

    /// <summary>Brightness factor over time (null: constant light).</summary>
    public Func<TimeSpan, double>? Brightness { get; init; }

    /// <summary>Noise of the stream's compression, standard deviation in gray levels.</summary>
    public double Noise { get; init; } = 2.5;

    /// <summary>The telemetry reports the height this late (a poll every 2 s and the snapshot's age).</summary>
    public TimeSpan TelemetryLag { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The run stops this high (mm) above the failure, or at <see cref="FinalHeight"/>.</summary>
    public double AfterFailure { get; init; } = 8;

    public int Seed { get; init; } = 1;

    /// <summary>The image's shift of the bed at <paramref name="z"/> (px, down), for the model k at row <paramref name="y"/>.</summary>
    public double BedShift(double z, double y) => KScale * RowFactor(y) * Integral(z);

    /// <summary>When the failure starts (null: no failure, or it is above the end).</summary>
    public TimeSpan? FailureTime => FailureHeight is { } height ? Course().FirstOrDefault(c => c.Z >= height && c.Printing).Time : null;

    /// <summary>
    /// The samples in time order with the height the telemetry reports (the frame is reused, valid until the next one).
    /// </summary>
    public IEnumerable<(TimeSpan Time, byte[] Pixels, double? Z)> Samples()
    {
        Random random = new(Seed);
        byte[] frame = new byte[Width * Height];
        (TimeSpan Time, double Z, bool Printing, int Head)[] course = [.. Course()];
        int index = 0;
        int key = random.Next(SyntheticGrowingScene.Heads.Count);
        TimeSpan time = TimeSpan.Zero;
        TimeSpan end = course[^1].Time;
        while (time <= end)
        {
            while (index + 1 < course.Length && course[index + 1].Time <= time)
            {
                index++;
            }

            (_, double z, _, int head) = course[index];
            Render(frame, z, key, head, random);
            if (Brightness is { } brightness)
            {
                SyntheticScene.Scale(frame, brightness(time));
            }

            if (Noise > 0)
            {
                SyntheticScene.AddCompressionNoise(frame, Noise, random);
            }

            TimeSpan polled = time - TelemetryLag;
            double reported = course[Math.Max(0, Array.FindLastIndex(course, index, c => c.Time <= polled))].Z;
            yield return (time, frame, polled < TimeSpan.Zero ? null : reported);

            time += Interval + TimeSpan.FromMilliseconds(random.Next(0, 600));
            key = (key + 1 + random.Next(2)) % SyntheticGrowingScene.Heads.Count;
        }
    }

    /// <summary>
    /// The course of the Z axis in steps of a second: the start sequence, then the layers with Z hops and tool changes, until
    /// <see cref="FinalHeight"/> (or <see cref="AfterFailure"/> above the failure). <c>Head</c>: where the head is (0: over
    /// the parts, 1: at the wipe tower of a tool change).
    /// </summary>
    public IEnumerable<(TimeSpan Time, double Z, bool Printing, int Head)> Course()
    {
        Random random = new(Seed * 7919);
        int second = 0;
        if (StartSequence)
        {
            // Job 354: 40 mm while heating, then 15 mm, the bed mesh and the nozzle cleaning near 0.
            for (; second < 90; second++)
            {
                yield return (TimeSpan.FromSeconds(second), 40, false, 0);
            }

            for (; second < 120; second++)
            {
                yield return (TimeSpan.FromSeconds(second), 15, false, 0);
            }

            for (; second < 180; second++)
            {
                yield return (TimeSpan.FromSeconds(second), random.Next(4) == 0 ? 2 + random.Next(4) : random.NextDouble() * 0.7, false, 0);
            }
        }

        double end = FailureHeight is { } failure ? Math.Min(FinalHeight, failure + AfterFailure) : FinalHeight;
        double layerSeconds = Layer / Rate * 60;
        double z = Layer;
        double nextToolChange = ToolChangeEvery > 0 ? ToolChangeEvery : double.PositiveInfinity;
        while (z <= end + 1e-9)
        {
            int layerEnd = second + (int)Math.Round(layerSeconds);
            if (z >= nextToolChange)
            {
                // The Z axis stands while the MMU changes the filament; the head is at the wipe tower.
                nextToolChange += ToolChangeEvery;
                for (int stop = second + (int)ToolChange.TotalSeconds; second < stop; second++)
                {
                    yield return (TimeSpan.FromSeconds(second), z, true, 1);
                }

                layerEnd = second + (int)Math.Round(layerSeconds);
            }

            while (second < layerEnd)
            {
                // Job 354: 52 hops of more than 0.25 mm in ~6 900 s, i.e. about one every two minutes; here one a minute.
                if (ZHop > 0 && random.Next(60) == 0)
                {
                    for (int hop = 1 + random.Next(3); hop > 0 && second < layerEnd; hop--, second++)
                    {
                        yield return (TimeSpan.FromSeconds(second), z + ZHop, true, 0);
                    }
                }
                else
                {
                    yield return (TimeSpan.FromSeconds(second), z, true, 0);
                    second++;
                }
            }

            z = Math.Round(z + Layer, 3);
        }
    }

    /// <summary>One frame at bed height <paramref name="z"/> with the head of key frame <paramref name="key"/>.</summary>
    private void Render(byte[] frame, double z, int key, int headPlace, Random random)
    {
        SyntheticGrowingScene.RenderBed(frame, this, z);
        foreach (SyntheticPart part in Parts.OrderBy(static p => p.FrontY))
        {
            double printed = Math.Clamp(Math.Min(part.TopHeight, FailureHeight ?? double.PositiveInfinity), part.BaseHeight, double.PositiveInfinity);
            if (PartsGone && FailureHeight is { } failure && z >= failure)
            {
                continue;
            }

            printed = Math.Min(printed, z);
            if (printed <= part.BaseHeight)
            {
                continue;
            }

            part.Render(frame, this, z, printed);
        }

        // The head and the X gantry over the parts (the first part's place) or at the wipe tower.
        SyntheticPart anchor = headPlace == 1 && Parts.FirstOrDefault(static p => p.IsWipeTower) is { } tower ? tower : Parts[0];
        int dx = (int)Math.Round(anchor.CenterX - SyntheticGrowingScene.HeadCenterX) + random.Next(-8, 9);
        int dy = (int)Math.Round(anchor.FrontY - SyntheticGrowingScene.HeadFrontY);
        SyntheticGrowingScene.PasteHead(frame, key, dx, dy);
    }

    private static double RowFactor(double y) => Math.Clamp(0.85 + (0.3 * (y - 60) / 120), 0.8, 1.15);

    /// <summary>∫k dZ from 0 to <paramref name="z"/> of the model k (px at 320×180).</summary>
    internal static double Integral(double z) => 0.9 * 62.5 * Math.Log(1 + (Math.Max(z, -30) / 62.5));

    /// <summary>The bed's shift relative to the fixture (where the bed is at <see cref="FixtureHeight"/>).</summary>
    internal double FixtureShift(double z, double y) => BedShift(z, y) - BedShift(FixtureHeight, y);
}

/// <summary>
/// A part of <see cref="SyntheticGrowingPrint"/>: its front edge (a sloped line, as the parts of the real series) where it
/// is at the nozzle, a top surface behind it and a face that grows downwards. Grows from <see cref="BaseHeight"/> (0: the bed;
/// more: a column on a lower part) up to <see cref="TopHeight"/>.
/// </summary>
internal sealed record SyntheticPart
{
    /// <summary>Left end of the front edge at the nozzle (px of 320×180).</summary>
    public double FrontX { get; init; } = 125;

    public double FrontY { get; init; } = 100;

    /// <summary>Length of the front edge (px; the head is ~60 px wide).</summary>
    public double Length { get; init; } = 75;

    /// <summary>Rows per column of the front edge (the real part: −0.5).</summary>
    public double Slope { get; init; } = -0.5;

    /// <summary>The top surface behind the front edge (px).</summary>
    public double Depth { get; init; } = 4;

    public double FaceLuma { get; init; } = 200;

    public double TopLuma { get; init; } = 150;

    public double RimLuma { get; init; } = 235;

    public double BaseHeight { get; init; }

    public double TopHeight { get; init; } = double.PositiveInfinity;

    /// <summary>The MMU's wipe tower (the head goes there at a tool change).</summary>
    public bool IsWipeTower { get; init; }

    public double CenterX => FrontX + (Length / 2);

    /// <summary>The fixture's part: a box about as long as the head is wide.</summary>
    public static SyntheticPart Box() => new();

    /// <summary>Draws the part at bed height <paramref name="z"/> once printed up to <paramref name="printed"/>.</summary>
    public void Render(byte[] frame, SyntheticGrowingPrint print, double z, double printed)
    {
        int left = (int)Math.Floor(FrontX);
        int right = (int)Math.Ceiling(FrontX + Length);
        for (int x = Math.Max(0, left); x < Math.Min(SyntheticGrowingPrint.Width, right); x++)
        {
            double edge = FrontY + (Slope * (x + 0.5 - FrontX));
            // The top is (z − printed) below the nozzle, the bottom (z − base) below it.
            double top = edge + print.BedShift(z - printed, edge);
            double bottom = edge + print.BedShift(z - BaseHeight, edge);
            double coverX = Math.Min(1, Math.Min(x + 1 - FrontX, FrontX + Length - x));
            if (coverX <= 0)
            {
                continue;
            }

            int first = Math.Max(0, (int)Math.Floor(top - Depth - 1));
            int last = Math.Min(SyntheticGrowingPrint.Height - 1, (int)Math.Ceiling(bottom));
            for (int y = first; y <= last; y++)
            {
                double value = 0;
                double covered = 0;
                Add(y, top - Depth, top - 0.5, TopLuma, ref value, ref covered);
                Add(y, top - 0.5, top + 0.7, RimLuma, ref value, ref covered);
                // Faint horizontal layer lines give the face a little structure, as the real parts have.
                Add(y, top + 0.7, bottom - 0.8, FaceLuma - (6 * Math.Sin(y * 0.9)), ref value, ref covered);
                Add(y, bottom - 0.8, bottom, FaceLuma * 0.6, ref value, ref covered);
                covered *= coverX;
                if (covered <= 0)
                {
                    continue;
                }

                int i = (y * SyntheticGrowingPrint.Width) + x;
                frame[i] = SyntheticScene.Clamp((frame[i] * (1 - covered)) + (value * coverX));
            }
        }
    }

    private static void Add(int y, double from, double to, double luma, ref double value, ref double covered)
    {
        double overlap = Math.Min(y + 1, to) - Math.Max(y, from);
        if (overlap > 0)
        {
            value += overlap * luma;
            covered += overlap;
        }
    }
}

/// <summary>The fixture frames <see cref="SyntheticGrowingPrint"/> is made of.</summary>
internal static class SyntheticGrowingScene
{
    /// <summary>The bed as the fixture shows it (320×180): the sheet between the frame and the rails.</summary>
    private static readonly (double X, double Y)[] BedOutline = [(50, 97), (150, 60), (205, 60), (292, 100), (232, 180), (105, 180)];

    private static readonly Lazy<byte[]> LazyBed = new(ComputeBed);
    private static readonly Lazy<bool[]> LazyBedMask = new(ComputeBedMask);
    private static readonly Lazy<byte[]> LazyWall = new(ComputeWall);
    private static readonly Lazy<(int Index, byte Value)[][]> LazyHeads = new(ComputeHeads);

    /// <summary>Where the head of the key frames hovers: over the fixture's part.</summary>
    public const double HeadCenterX = 162;

    public const double HeadFrontY = 100;

    /// <summary>Per key frame of the clip: the pixels of the head and the X gantry (those far from the median).</summary>
    public static IReadOnlyList<(int Index, byte Value)[]> Heads => LazyHeads.Value;

    /// <summary>The scene without the parts, the bed shifted to height <paramref name="z"/>.</summary>
    public static void RenderBed(byte[] frame, SyntheticGrowingPrint print, double z)
    {
        byte[] still = SyntheticScene.Median;
        byte[] bed = LazyBed.Value;
        bool[] mask = LazyBedMask.Value;
        byte[] wall = LazyWall.Value;
        int width = SyntheticGrowingPrint.Width;
        int height = SyntheticGrowingPrint.Height;
        for (int y = 0; y < height; y++)
        {
            double shift = print.FixtureShift(z, y);
            double source = y - shift;
            int s0 = (int)Math.Floor(source);
            double t = source - s0;
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                if (s0 >= 0 && s0 + 1 < height && mask[(s0 * width) + x] && mask[((s0 + 1) * width) + x])
                {
                    frame[i] = SyntheticScene.Clamp((bed[(s0 * width) + x] * (1 - t)) + (bed[((s0 + 1) * width) + x] * t));
                }
                else if (s0 >= height - 1 && mask[((height - 1) * width) + x])
                {
                    // Below the frame's bottom row (the bed moved up): its last row.
                    frame[i] = bed[((height - 1) * width) + x];
                }
                else
                {
                    // Off the bed: the frame, or where the bed was, the chamber behind it.
                    frame[i] = mask[i] ? wall[i] : still[i];
                }
            }
        }
    }

    /// <summary>Pastes the head and the X gantry of key frame <paramref name="key"/>, moved by <paramref name="dx"/>, <paramref name="dy"/>.</summary>
    public static void PasteHead(byte[] frame, int key, int dx, int dy)
    {
        int width = SyntheticGrowingPrint.Width;
        foreach ((int index, byte value) in Heads[key])
        {
            int x = (index % width) + dx;
            int y = (index / width) + dy;
            if (x >= 0 && x < width && y >= 0 && y < SyntheticGrowingPrint.Height)
            {
                frame[(y * width) + x] = value;
            }
        }
    }

    private static bool Inside(double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = BedOutline.Length - 1; i < BedOutline.Length; j = i++)
        {
            (double xi, double yi) = BedOutline[i];
            (double xj, double yj) = BedOutline[j];
            if ((yi > y) != (yj > y) && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool[] ComputeBedMask()
    {
        bool[] mask = new bool[SyntheticGrowingPrint.Width * SyntheticGrowingPrint.Height];
        for (int y = 0; y < SyntheticGrowingPrint.Height; y++)
        {
            for (int x = 0; x < SyntheticGrowingPrint.Width; x++)
            {
                mask[(y * SyntheticGrowingPrint.Width) + x] = Inside(x + 0.5, y + 0.5);
            }
        }

        return mask;
    }

    /// <summary>The median with the fixture's part painted over by the sheet around it.</summary>
    private static byte[] ComputeBed()
    {
        byte[] bed = (byte[])SyntheticScene.Median.Clone();
        new MovedPart(0, 0, Gone: true).Apply(bed, TimeSpan.Zero);
        // The part's top behind the head and its shadow are a little wider than the bright pixels: the band around them too.
        int width = SyntheticGrowingPrint.Width;
        (int bx, int by, int bw, int bh) = SyntheticScene.PartBox;
        for (int y = by; y < by + bh; y++)
        {
            for (int x = bx; x < bx + bw; x++)
            {
                if (bed[(y * width) + x] > 120)
                {
                    bed[(y * width) + x] = 48;
                }
            }
        }

        return bed;
    }

    /// <summary>The chamber behind the bed: dark gray with a fine static speckle.</summary>
    private static byte[] ComputeWall()
    {
        Random random = new(99);
        byte[] wall = new byte[SyntheticGrowingPrint.Width * SyntheticGrowingPrint.Height];
        for (int i = 0; i < wall.Length; i++)
        {
            wall[i] = (byte)(38 + random.Next(-6, 7));
        }

        return wall;
    }

    private static (int Index, byte Value)[][] ComputeHeads()
    {
        byte[] median = SyntheticScene.Median;
        return
        [
            .. SyntheticScene.KeyFrames.Select(key => Enumerable.Range(0, key.Length)
                .Where(i => Math.Abs(key[i] - median[i]) > 25)
                .Select(i => (i, key[i]))
                .ToArray()),
        ];
    }
}
