namespace LayerPeek.Tests.Detection;

/// <summary>
/// A print failure painted into the fixture frames (T-155, user decision 2026-11-04: synthetic variants generated in test
/// code). Applied to every sample from the start of the failure on; <c>since</c> is the time since then.
/// </summary>
internal abstract record SyntheticFailure
{
    public abstract string Name { get; }

    public abstract void Apply(byte[] frame, TimeSpan since);

    public override string ToString() => Name;

    /// <summary>Blends <paramref name="luma"/> into a pixel with <paramref name="alpha"/> (0–1); outside the frame: nothing.</summary>
    protected static void Paint(byte[] frame, int x, int y, double luma, double alpha)
    {
        if (x < 0 || y < 0 || x >= SyntheticPrint.Width || y >= SyntheticPrint.Height)
        {
            return;
        }

        int i = (y * SyntheticPrint.Width) + x;
        frame[i] = SyntheticScene.Clamp((frame[i] * (1 - alpha)) + (luma * alpha));
    }
}

/// <summary>
/// Spaghetti: a tangle of thin strands of filament (random walks) in a box on the bed, growing for
/// <paramref name="Growth"/>. At 320×180 a strand (~0.4 mm) covers a fraction of a pixel, so it is blended with
/// <paramref name="Alpha"/>; <paramref name="Thick"/> strands are two pixels wide. <paramref name="Glint"/> (T-163) is the
/// share of the strands' pixels with a highlight of the chamber light (glossy black filament), fixed in place.
/// </summary>
internal sealed record Spaghetti(int Seed, int Strands, double Luma, double Alpha, bool Thick, (int X, int Y, int Width, int Height) Box, TimeSpan Growth, double Glint = 0) : SyntheticFailure
{
    /// <summary>A highlight on glossy filament (blended with the strand's alpha, it stays below saturation).</summary>
    public const double GlintLuma = 170;

    private readonly Lazy<(int X, int Y)[][]> _paths = new(() => Walk(Seed, Strands, Box));

    public override string Name => $"spaghetti({Strands} strands, luma {Luma:0}, alpha {Alpha:0.00}{(Thick ? ", thick" : "")}{(Glint > 0 ? $", glint {Glint:P0}" : "")}, box {Box.Width}×{Box.Height}, growth {Growth.TotalMinutes:0.#} min)";

    public override void Apply(byte[] frame, TimeSpan since)
    {
        (int X, int Y)[][] paths = _paths.Value;
        double progress = Growth <= TimeSpan.Zero ? 1 : Math.Min(1, since / Growth);
        int visible = (int)Math.Ceiling(progress * paths.Length);
        for (int s = 0; s < visible; s++)
        {
            foreach ((int x, int y) in paths[s])
            {
                Paint(frame, x, y, Luma, Alpha);
                if (Thick)
                {
                    Paint(frame, x, y + 1, Luma, Alpha);
                }

                if (Glint > 0 && IsGlint(s, x, y))
                {
                    Paint(frame, x, y, GlintLuma, Alpha * 0.6);
                }
            }
        }
    }

    /// <summary>Whether the strand has a highlight at this pixel: a fixed hash, the same in every sample.</summary>
    private bool IsGlint(int strand, int x, int y)
    {
        uint hash = unchecked(((uint)x * 73856093u) ^ ((uint)y * 19349663u) ^ ((uint)strand * 83492791u) ^ (uint)Seed);
        hash ^= hash >> 13;
        hash *= 0x5bd1e995u;
        hash ^= hash >> 15;
        return (hash % 1000) < Glint * 1000;
    }

    private static (int X, int Y)[][] Walk(int seed, int strands, (int X, int Y, int Width, int Height) box)
    {
        Random random = new(seed);
        (int X, int Y)[][] paths = new (int, int)[strands][];
        for (int s = 0; s < strands; s++)
        {
            double x = box.X + (random.NextDouble() * box.Width);
            double y = box.Y + (random.NextDouble() * box.Height);
            double angle = random.NextDouble() * 2 * Math.PI;
            int length = random.Next(25, 70);
            List<(int, int)> path = new(length);
            for (int step = 0; step < length; step++)
            {
                path.Add(((int)x, (int)y));
                angle += (random.NextDouble() - 0.5) * 0.9;
                x += Math.Cos(angle);
                y += Math.Sin(angle) * 0.6; // seen obliquely from above: flatter
                if (x < box.X || x > box.X + box.Width)
                {
                    angle = Math.PI - angle;
                }

                if (y < box.Y || y > box.Y + box.Height)
                {
                    angle = -angle;
                }
            }

            paths[s] = [.. path];
        }

        return paths;
    }
}

/// <summary>
/// Mass on the bed: an irregular lump (a part torn off, a blob of filament) of radius <paramref name="Radius"/> px,
/// growing to full size in <paramref name="Growth"/>.
/// </summary>
internal sealed record Lump(int Seed, int CenterX, int CenterY, double Radius, double Luma, TimeSpan Growth) : SyntheticFailure
{
    public override string Name => $"lump(r {Radius:0} px at {CenterX},{CenterY}, luma {Luma:0}, growth {Growth.TotalMinutes:0.#} min)";

    public override void Apply(byte[] frame, TimeSpan since)
    {
        double progress = Growth <= TimeSpan.Zero ? 1 : Math.Min(1, since / Growth);
        double radius = Radius * Math.Max(0.2, progress);
        Random random = new(Seed);
        // A wobbly outline: the radius varies with the angle.
        double a1 = random.NextDouble() * 0.35;
        double p1 = random.NextDouble() * Math.PI * 2;
        double a2 = random.NextDouble() * 0.2;
        double p2 = random.NextDouble() * Math.PI * 2;
        int reach = (int)Math.Ceiling(radius * 1.6);
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                double angle = Math.Atan2(dy, dx);
                double edge = radius * (1 + (a1 * Math.Sin((2 * angle) + p1)) + (a2 * Math.Sin((3 * angle) + p2)));
                // Flattened like the bed seen obliquely; a little shading towards the edge.
                double distance = Math.Sqrt((dx * dx) + (dy * dy / 0.36));
                if (distance <= edge)
                {
                    Paint(frame, CenterX + dx, CenterY + dy, Luma * (0.85 + (0.15 * (1 - (distance / edge)))), 1);
                }
            }
        }
    }
}

/// <summary>
/// The part came loose: it vanishes from its place (the sheet shows through) and, unless <paramref name="Gone"/>, lies
/// <paramref name="Dx"/>, <paramref name="Dy"/> px away. At once, as when the nozzle knocks it off. The part looks as in
/// <paramref name="Backdrop"/> (null: the silver part of the fixture; T-163: the black one of <see cref="DarkScene"/>).
/// </summary>
internal sealed record MovedPart(int Dx, int Dy, bool Gone, SyntheticBackdrop? Backdrop = null) : SyntheticFailure
{
    public override string Name => Gone ? "part gone" : $"part moved by {Dx},{Dy} px";

    public override void Apply(byte[] frame, TimeSpan since)
    {
        bool[] part = Backdrop?.Part ?? SyntheticScene.Part;
        byte[] median = Backdrop?.Median ?? SyntheticScene.Median;
        int width = SyntheticPrint.Width;
        // The part box with the 2 px edge of a dark backdrop's part (the fixture's part lies within the box).
        (int bx, int by, int bw, int bh) = SyntheticScene.PartBox;
        (bx, by, bw, bh) = (bx - 2, by - 2, bw + 4, bh + 4);

        // The sheet where the part was: per row, interpolated between the pixels left and right of the part.
        for (int y = by; y < by + bh; y++)
        {
            int x = bx;
            while (x < bx + bw)
            {
                if (!part[(y * width) + x])
                {
                    x++;
                    continue;
                }

                int start = x;
                while (x < bx + bw && part[(y * width) + x])
                {
                    x++;
                }

                byte left = frame[(y * width) + Math.Max(0, start - 1)];
                byte right = frame[(y * width) + Math.Min(width - 1, x)];
                for (int i = start; i < x; i++)
                {
                    double t = (i - start + 1) / (double)(x - start + 1);
                    frame[(y * width) + i] = SyntheticScene.Clamp(left + ((right - left) * t));
                }
            }
        }

        if (Gone)
        {
            return;
        }

        for (int y = by; y < by + bh; y++)
        {
            for (int x = bx; x < bx + bw; x++)
            {
                if (part[(y * width) + x])
                {
                    Paint(frame, x + Dx, y + Dy, median[(y * width) + x], 1);
                }
            }
        }
    }
}
