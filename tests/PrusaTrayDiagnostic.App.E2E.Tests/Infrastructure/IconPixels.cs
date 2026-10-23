using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>An image as straight (not premultiplied) ARGB pixels, row by row: fast to compare, unlike <c>Bitmap.GetPixel</c>.</summary>
internal sealed class Pixels
{
    public Pixels(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Argb = new int[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The pixels as <see cref="Color.ToArgb"/> values.</summary>
    public int[] Argb { get; }

    public Color this[int x, int y]
    {
        get => Color.FromArgb(Argb[(y * Width) + x]);
        set => Argb[(y * Width) + x] = value.ToArgb();
    }

    public static Pixels From(Bitmap bitmap)
    {
        Pixels pixels = new(bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels.Argb, y * bitmap.Width, bitmap.Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return pixels;
    }

    public static Pixels Load(string file)
    {
        using Bitmap bitmap = new(file);
        return From(bitmap);
    }

    public Bitmap ToBitmap()
    {
        Bitmap bitmap = new(Width, Height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < Height; y++)
            {
                Marshal.Copy(Argb, y * Width, data.Scan0 + (y * data.Stride), Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    /// <summary>The part of the image inside <paramref name="area"/> (cut to the image).</summary>
    public Pixels Crop(Rectangle area)
    {
        area.Intersect(new Rectangle(0, 0, Width, Height));
        Pixels part = new(area.Width, area.Height);
        for (int y = 0; y < area.Height; y++)
        {
            Array.Copy(Argb, ((area.Top + y) * Width) + area.Left, part.Argb, y * area.Width, area.Width);
        }

        return part;
    }

    /// <summary>Saves the image into the results folder as <c>NNN-name.png</c> (see <see cref="Screenshots.Save(Bitmap, string)"/>).</summary>
    public string Save(string name)
    {
        using Bitmap bitmap = ToBitmap();
        return Screenshots.Save(bitmap, name);
    }
}

/// <summary>Where a reference image fits a capture best, and how well (mean colour difference 0..1 of the icon's pixels).</summary>
internal readonly record struct IconMatch(Point Location, double Difference)
{
    public static IconMatch None { get; } = new(new Point(-1, -1), 1);

    public bool Found => Location.X >= 0;

    public bool IsMatch => Found && Difference <= IconPixels.MatchTolerance;

    public override string ToString() =>
        Found ? string.Create(CultureInfo.InvariantCulture, $"{Difference:P1} at {Location.X},{Location.Y}") : "no place for the icon in the area";
}

/// <summary>
/// Pixel checks of icons on the screen (T-070): a reference (an entry of <c>app-icon.ico</c>, a tray icon snapshot of
/// T-025, a printer illustration) is composed over the measured background and searched for in a small capture.
/// </summary>
internal static class IconPixels
{
    /// <summary>
    /// A reference pixel at least this opaque is part of the icon in a comparison (a quarter): the transparent rest would
    /// make an empty background match a sparse tray icon.
    /// </summary>
    public const int CoverageAlpha = 64;

    /// <summary>A reference pixel at least this opaque counts for the contrast (the edges are blended with the background).</summary>
    public const int OpaqueAlpha = 230;

    /// <summary>
    /// The largest mean colour difference of the icon's pixels that still counts as the same icon (10 %, as the icon checks
    /// of T-044): far below a different picture, above the blending and colour management of the shell.
    /// </summary>
    public const double MatchTolerance = 0.10;

    /// <summary><paramref name="top"/> (straight alpha) over an opaque <paramref name="background"/>.</summary>
    public static Color Compose(Color top, Color background)
    {
        double alpha = top.A / 255.0;
        return Color.FromArgb(
            255,
            (int)Math.Round((top.R * alpha) + (background.R * (1 - alpha))),
            (int)Math.Round((top.G * alpha) + (background.G * (1 - alpha))),
            (int)Math.Round((top.B * alpha) + (background.B * (1 - alpha))));
    }

    /// <summary>
    /// The image scaled to <paramref name="width"/>×<paramref name="height"/> with high quality bicubic filtering, as the
    /// shell and Avalonia scale bitmaps (neither exactly: the comparison tolerates the difference).
    /// </summary>
    public static Pixels Resize(Pixels source, int width, int height)
    {
        using Bitmap from = source.ToBitmap();
        using Bitmap to = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(to))
        using (ImageAttributes attributes = new())
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // No dark fringe from outside the source at the edges.
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(from, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        return Pixels.From(to);
    }

    /// <summary>The median colour (per channel) of <paramref name="area"/>: the background when the icon covers less than half of it.</summary>
    public static Color Median(Pixels image, Rectangle area)
    {
        area.Intersect(new Rectangle(0, 0, image.Width, image.Height));
        if (area.IsEmpty)
        {
            throw new ArgumentException("The area is outside the image.", nameof(area));
        }

        int count = area.Width * area.Height;
        byte[] red = new byte[count];
        byte[] green = new byte[count];
        byte[] blue = new byte[count];
        int i = 0;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            for (int x = area.Left; x < area.Right; x++, i++)
            {
                Color c = image[x, y];
                red[i] = c.R;
                green[i] = c.G;
                blue[i] = c.B;
            }
        }

        Array.Sort(red);
        Array.Sort(green);
        Array.Sort(blue);
        return Color.FromArgb(red[count / 2], green[count / 2], blue[count / 2]);
    }

    /// <summary>
    /// Mean colour difference (0..1) between the capture at <paramref name="at"/> and the reference composed over
    /// <paramref name="background"/>, over the reference's own pixels (alpha at least <see cref="CoverageAlpha"/>).
    /// </summary>
    public static double DifferenceAt(Pixels capture, Point at, Pixels reference, Color background)
    {
        double sum = 0;
        int counted = 0;
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                Color top = reference[x, y];
                if (top.A < CoverageAlpha)
                {
                    continue;
                }

                Color expected = Compose(top, background);
                Color actual = capture[at.X + x, at.Y + y];
                sum += (Math.Abs(actual.R - expected.R) + Math.Abs(actual.G - expected.G) + Math.Abs(actual.B - expected.B)) / (3 * 255.0);
                counted++;
            }
        }

        return counted == 0 ? 1 : sum / counted;
    }

    /// <summary>
    /// Mean colour difference (0..1) of two icons of the same size, both composed over mid grey: alpha counts too, and the
    /// colour of fully transparent pixels does not.
    /// </summary>
    public static double WholeDifference(Pixels first, Pixels second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return 1;
        }

        Color grey = Color.FromArgb(128, 128, 128);
        double sum = 0;
        for (int i = 0; i < first.Argb.Length; i++)
        {
            Color a = Compose(Color.FromArgb(first.Argb[i]), grey);
            Color b = Compose(Color.FromArgb(second.Argb[i]), grey);
            sum += (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B)) / (3 * 255.0);
        }

        return sum / first.Argb.Length;
    }

    /// <summary>The place in <paramref name="area"/> of the capture where the reference fits best (<see cref="IconMatch.None"/> when it does not fit in).</summary>
    public static IconMatch FindBest(Pixels capture, Rectangle area, Pixels reference, Color background)
    {
        area.Intersect(new Rectangle(0, 0, capture.Width, capture.Height));
        IconMatch best = IconMatch.None;
        for (int top = area.Top; top + reference.Height <= area.Bottom; top++)
        {
            for (int left = area.Left; left + reference.Width <= area.Right; left++)
            {
                double difference = DifferenceAt(capture, new Point(left, top), reference, background);
                if (!best.Found || difference < best.Difference)
                {
                    best = new IconMatch(new Point(left, top), difference);
                }
            }
        }

        return best;
    }

    /// <summary>WCAG 2.x relative luminance (0..1) of an sRGB colour.</summary>
    public static double Luminance(Color color)
    {
        static double Linear(int channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }

    /// <summary>WCAG 2.x contrast ratio (1..21) of two colours.</summary>
    public static double ContrastRatio(Color first, Color second)
    {
        double a = Luminance(first);
        double b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// <summary>
    /// The largest contrast against <paramref name="background"/> of the captured pixels where the reference (placed at
    /// <paramref name="at"/>) is opaque inside <paramref name="region"/> (reference coordinates), and how many there are.
    /// </summary>
    public static (double Contrast, int Pixels) MaxContrast(Pixels capture, Point at, Pixels reference, Color background, Rectangle region)
    {
        region.Intersect(new Rectangle(0, 0, reference.Width, reference.Height));
        double max = 1;
        int pixels = 0;
        for (int y = region.Top; y < region.Bottom; y++)
        {
            for (int x = region.Left; x < region.Right; x++)
            {
                if (reference[x, y].A < OpaqueAlpha)
                {
                    continue;
                }

                pixels++;
                max = Math.Max(max, ContrastRatio(capture[at.X + x, at.Y + y], background));
            }
        }

        return (max, pixels);
    }

    /// <summary>The image sizes of an <c>.ico</c> file in the order of its directory (a width byte of 0 means 256).</summary>
    public static IReadOnlyList<int> IcoSizes(byte[] ico) => [.. IcoEntries(ico).Select(static entry => entry.Size)];

    /// <summary>The directory of an <c>.ico</c> file: size, offset and length of every image, in file order.</summary>
    public static IReadOnlyList<(int Size, int Offset, int Length)> IcoEntries(byte[] ico)
    {
        if (ico.Length < 6 || ico[0] != 0 || ico[1] != 0 || ico[2] != 1 || ico[3] != 0)
        {
            throw new InvalidDataException("Not an .ico file.");
        }

        int count = BitConverter.ToUInt16(ico, 4);
        if (ico.Length < 6 + (16 * count))
        {
            throw new InvalidDataException("The .ico directory is cut off.");
        }

        return [.. Enumerable.Range(0, count).Select(i =>
        {
            int entry = 6 + (16 * i);
            return (ico[entry] is 0 ? 256 : (int)ico[entry], BitConverter.ToInt32(ico, entry + 12), BitConverter.ToInt32(ico, entry + 8));
        })];
    }

    /// <summary>
    /// The image of the <c>.ico</c> entry <paramref name="index"/>: a PNG entry decoded as PNG (<see cref="Icon"/> skips
    /// those), a bitmap entry through a one-entry <c>.ico</c>.
    /// </summary>
    public static Pixels IcoEntry(byte[] ico, int index)
    {
        (int size, int offset, int length) = IcoEntries(ico)[index];
        if (offset < 0 || length <= 0 || offset + length > ico.Length)
        {
            throw new InvalidDataException($"The .ico entry {index} lies outside the file.");
        }

        ReadOnlySpan<byte> png = [0x89, (byte)'P', (byte)'N', (byte)'G'];
        Bitmap bitmap;
        if (ico.AsSpan(offset, 4).SequenceEqual(png))
        {
            bitmap = new Bitmap(new MemoryStream(ico, offset, length, writable: false));
        }
        else
        {
            byte[] single = new byte[6 + 16 + length];
            ico.AsSpan(0, 4).CopyTo(single);
            single[4] = 1;
            ico.AsSpan(6 + (16 * index), 12).CopyTo(single.AsSpan(6));
            BitConverter.TryWriteBytes(single.AsSpan(6 + 8), length);
            BitConverter.TryWriteBytes(single.AsSpan(6 + 12), 6 + 16);
            ico.AsSpan(offset, length).CopyTo(single.AsSpan(6 + 16));
            using Icon icon = new(new MemoryStream(single));
            bitmap = icon.ToBitmap();
        }

        using (bitmap)
        {
            return bitmap.Width == size && bitmap.Height == size
                ? Pixels.From(bitmap)
                : throw new InvalidDataException($"The .ico entry {index} of {size} px is {bitmap.Width}×{bitmap.Height}.");
        }
    }

    /// <summary>
    /// The entry Avalonia takes from an <c>.ico</c> for <paramref name="wanted"/> pixels (T-025): the nearest size, the first
    /// one in the file on a tie.
    /// </summary>
    public static int Nearest(IReadOnlyList<int> sizes, int wanted)
    {
        int best = sizes[0];
        foreach (int size in sizes)
        {
            if (Math.Abs(size - wanted) < Math.Abs(best - wanted))
            {
                best = size;
            }
        }

        return best;
    }

    /// <summary>
    /// Whether 32-bit pixels from a GDI bitmap of the shell look premultiplied: some pixel is translucent and no colour
    /// channel exceeds its alpha anywhere (a straight image almost always has such a pixel).
    /// </summary>
    public static bool LooksPremultiplied(Pixels image)
    {
        bool translucent = false;
        foreach (int argb in image.Argb)
        {
            Color c = Color.FromArgb(argb);
            if (c.R > c.A || c.G > c.A || c.B > c.A)
            {
                return false;
            }

            translucent |= c.A is > 0 and < 255;
        }

        return translucent;
    }

    /// <summary>Premultiplied pixels made straight.</summary>
    public static Pixels Unpremultiply(Pixels image)
    {
        Pixels straight = new(image.Width, image.Height);
        for (int i = 0; i < image.Argb.Length; i++)
        {
            Color c = Color.FromArgb(image.Argb[i]);
            straight.Argb[i] = c.A switch
            {
                0 => 0,
                255 => c.ToArgb(),
                _ => Color.FromArgb(c.A, Straight(c.R, c.A), Straight(c.G, c.A), Straight(c.B, c.A)).ToArgb(),
            };
        }

        return straight;

        static int Straight(int channel, int alpha) => Math.Min(255, ((channel * 255) + (alpha / 2)) / alpha);
    }
}

/// <summary>The app icon file of T-025 (<c>Assets/AppIcon/app-icon.ico</c>) and its entries.</summary>
internal static class AppIconFile
{
    public static string Path { get; } = System.IO.Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App", "Assets", "AppIcon", "app-icon.ico");

    private static readonly byte[] Bytes = File.ReadAllBytes(Path);

    /// <summary>The sizes of the entries in file order (largest first).</summary>
    public static IReadOnlyList<int> Sizes { get; } = IconPixels.IcoSizes(Bytes);

    /// <summary>The entry of exactly <paramref name="size"/> pixels.</summary>
    public static Pixels Entry(int size)
    {
        int index = Sizes.ToList().IndexOf(size);
        return index >= 0
            ? IconPixels.IcoEntry(Bytes, index)
            : throw new ArgumentOutOfRangeException(nameof(size), size, $"app-icon.ico has no {size} px entry ({string.Join(", ", Sizes)}).");
    }
}

/// <summary>
/// The size check of a captured tray icon (T-070): it shows the icon the app renders for the scale of the taskbar
/// (<see cref="Expected"/>) and not a stretched icon of another size (<see cref="Stale"/>, e.g. the 16 px icon of the old
/// scale that the shell stretches to 24 px).
/// </summary>
internal readonly record struct TraySizeCheck(IconMatch Expected, IconMatch Stale)
{
    public bool IsExpectedSize => Expected.IsMatch && Expected.Difference < Stale.Difference;

    public static TraySizeCheck Of(Pixels capture, Rectangle area, Pixels expected, Pixels? stale, Color background) =>
        new(
            IconPixels.FindBest(capture, area, expected, background),
            stale is null ? IconMatch.None : IconPixels.FindBest(capture, area, stale, background));

    public override string ToString() => $"expected size {Expected}, stretched other size {Stale}";
}

/// <summary>
/// The visibility check of a captured tray icon on the taskbar (T-070): the most contrasting opaque pixel of the motif
/// (WCAG contrast ratio against the measured taskbar) reaches <see cref="MotifMinimum"/>, and of the state badge
/// <see cref="BadgeMinimum"/>.
/// </summary>
internal readonly record struct TrayVisibility(double Motif, double? Badge)
{
    /// <summary>
    /// 2:1 for the motif. The approved Prusa orange of the hotend reaches only about 2.9:1 on a light taskbar, so WCAG's
    /// 3:1 for graphics would reject the approved design (T-025); 2:1 still fails an icon that blends into the taskbar.
    /// </summary>
    public const double MotifMinimum = 2.0;

    /// <summary>3:1 for the badge, which carries the state: WCAG 2.1 SC 1.4.11 (non-text contrast).</summary>
    public const double BadgeMinimum = 3.0;

    public bool IsVisible => Motif >= MotifMinimum && (Badge is not { } badge || badge >= BadgeMinimum);

    /// <summary>
    /// Where the badge of the tray icon of <paramref name="size"/> pixels is (top right, T-025 <c>TrayIconLayout</c>): the
    /// right 7/16 of the top 7/16; the motif never reaches into it.
    /// </summary>
    public static Rectangle BadgeRegion(int size)
    {
        int left = size * 9 / 16;
        return new Rectangle(left, 0, size - left, (int)Math.Ceiling(size * 7 / 16.0));
    }

    /// <summary>Measures the capture with the icon <paramref name="reference"/> found at <paramref name="at"/>.</summary>
    public static TrayVisibility Of(Pixels capture, Point at, Pixels reference, Color background, bool hasBadge)
    {
        Rectangle badge = BadgeRegion(reference.Width);
        Pixels motif = new(reference.Width, reference.Height);
        Array.Copy(reference.Argb, motif.Argb, reference.Argb.Length);
        for (int y = badge.Top; y < badge.Bottom; y++)
        {
            for (int x = badge.Left; x < badge.Right; x++)
            {
                motif[x, y] = Color.Transparent;
            }
        }

        (double motifContrast, _) = IconPixels.MaxContrast(capture, at, motif, background, new Rectangle(0, 0, reference.Width, reference.Height));
        double? badgeContrast = hasBadge ? IconPixels.MaxContrast(capture, at, reference, background, badge).Contrast : null;
        return new TrayVisibility(motifContrast, badgeContrast);
    }

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"motif {Motif:0.00}:1 (>= {MotifMinimum}), badge {(Badge is { } badge ? string.Create(CultureInfo.InvariantCulture, $"{badge:0.00}:1 (>= {BadgeMinimum})") : "none")}");
}
