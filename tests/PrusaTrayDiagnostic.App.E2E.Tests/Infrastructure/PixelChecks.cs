using System.Drawing;
using System.Globalization;
using System.Runtime.Versioning;

using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>How closely a captured area matches an expected picture (per pixel, largest channel difference 0–255).</summary>
/// <param name="Mean">Mean difference over all pixels.</param>
/// <param name="Max">Largest difference of a single pixel.</param>
/// <param name="Offset">Shift of the captured area (pixels) at which the match was best.</param>
internal sealed record PixelMatch(double Mean, int Max, Point Offset)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"mean {Mean:0.0}, max {Max}, offset ({Offset.X},{Offset.Y})");
}

/// <summary>A coarse colour class of the quadrants of the test photo (T-069).</summary>
internal enum QuadrantColor
{
    Other,
    Red,
    Green,
    Blue,
    Yellow,
}

/// <summary>
/// Pixel checks of T-069 on screenshots of the app's own windows: printer illustrations read from the repository's assets
/// (not from the app) composed on the measured background, the quadrants of the rotated test photo, and the access key
/// underlines (difference of a menu opened with the keyboard and with the mouse).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class PixelChecks
{
    /// <summary>The pre-rendered sizes of the illustrations (PrinterIllustrations.PixelSizes in the app).</summary>
    public static readonly int[] IllustrationSizes = [16, 20, 24, 32, 48, 56, 64, 96, 112, 128, 256];

    /// <summary>The illustration id of a simulated model (PrinterIllustrations.IdFor in the app).</summary>
    public static string IllustrationIdOf(string productName) => productName switch
    {
        "Core One" or "Core One L" => "core-one",
        "MK4S" or "MK4" or "MK3.9S" or "MK3.9" or "MK3.5S" or "MK3.5" => "mk4",
        "XL" or "XL+" => "xl",
        "MINI" => "mini",
        _ => "generic",
    };

    /// <summary>Every illustration id (to show that the expected one matches best).</summary>
    public static readonly string[] IllustrationIds = ["core-one", "mk4", "xl", "mini", "generic"];

    /// <summary>The pre-rendered size the app draws <paramref name="sizeInDip"/> with at <paramref name="scaling"/> (PrinterIllustrations.SelectPixelSize).</summary>
    public static int IllustrationPixelSize(double sizeInDip, double scaling)
    {
        int needed = Math.Max(1, (int)Math.Ceiling((sizeInDip * scaling) - 0.01));
        return IllustrationSizes.FirstOrDefault(size => size >= needed, IllustrationSizes[^1]);
    }

    /// <summary>The illustration PNG of the repository (<c>src/PrusaTrayDiagnostic.App/Assets/Printers</c>).</summary>
    public static Bitmap LoadIllustration(string id, int pixelSize)
    {
        string file = Path.Combine(TestWorktree.Root, "src", "PrusaTrayDiagnostic.App", "Assets", "Printers", string.Create(CultureInfo.InvariantCulture, $"{id}-{pixelSize}.png"));
        using Bitmap loaded = new(file);
        // A copy: the file stays free, the format is known.
        return loaded.Clone(new Rectangle(0, 0, loaded.Width, loaded.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    }

    /// <summary>The picture composed on an opaque <paramref name="background"/> (straight alpha), as it appears on screen.</summary>
    public static Bitmap ComposeOn(Bitmap picture, Color background)
    {
        Bitmap composed = new(picture.Width, picture.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        for (int y = 0; y < picture.Height; y++)
        {
            for (int x = 0; x < picture.Width; x++)
            {
                Color c = picture.GetPixel(x, y);
                double a = c.A / 255.0;
                composed.SetPixel(x, y, Color.FromArgb(
                    255,
                    (int)Math.Round((c.R * a) + (background.R * (1 - a))),
                    (int)Math.Round((c.G * a) + (background.G * (1 - a))),
                    (int)Math.Round((c.B * a) + (background.B * (1 - a)))));
            }
        }

        return composed;
    }

    /// <summary>Largest channel difference of two colours (alpha ignored: screenshots carry none that matters).</summary>
    public static int Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    /// <summary><see cref="Distance(Color, Color)"/> of two 32-bit ARGB values (alpha ignored).</summary>
    private static int Distance(int a, int b) => Math.Max(
        Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)),
        Math.Max(Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)), Math.Abs((a & 0xFF) - (b & 0xFF))));

    /// <summary>The ARGB values of <paramref name="area"/> row by row, read in one go (no <c>GetPixel</c> per pixel).</summary>
    private static int[] Pixels(Bitmap bitmap, Rectangle area)
    {
        int[] pixels = new int[area.Width * area.Height];
        if (pixels.Length == 0)
        {
            return pixels;
        }

        System.Drawing.Imaging.BitmapData data = bitmap.LockBits(area, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < area.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * area.Width, area.Width);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return pixels;
    }

    /// <summary>
    /// Compares <paramref name="expected"/> with the area of <paramref name="capture"/> at <paramref name="area"/>'s top left,
    /// also shifted by up to <paramref name="slack"/> pixels (UI Automation rectangles are rounded); the best shift wins.
    /// </summary>
    public static PixelMatch Compare(Bitmap capture, Point topLeft, Bitmap expected, int slack = 1)
    {
        // Every pixel read once (GetPixel per shift made the 48 px header search with slack 12 take seconds).
        Rectangle searched = Rectangle.Intersect(new Rectangle(topLeft.X - slack, topLeft.Y - slack, expected.Width + (2 * slack), expected.Height + (2 * slack)), new Rectangle(Point.Empty, capture.Size));
        int[] captured = Pixels(capture, searched);
        int[] wanted = Pixels(expected, new Rectangle(Point.Empty, expected.Size));
        PixelMatch? best = null;
        for (int dy = -slack; dy <= slack; dy++)
        {
            for (int dx = -slack; dx <= slack; dx++)
            {
                long sum = 0;
                int max = 0;
                int count = 0;
                for (int y = 0; y < expected.Height; y++)
                {
                    for (int x = 0; x < expected.Width; x++)
                    {
                        int cx = topLeft.X + dx + x;
                        int cy = topLeft.Y + dy + y;
                        int d = !searched.Contains(cx, cy) ? 255 : Distance(captured[((cy - searched.Top) * searched.Width) + cx - searched.Left], wanted[(y * expected.Width) + x]);
                        sum += d;
                        max = Math.Max(max, d);
                        count++;
                    }
                }

                PixelMatch match = new((double)sum / count, max, new Point(dx, dy));
                if (best is null || match.Mean < best.Mean)
                {
                    best = match;
                }
            }
        }

        return best!;
    }

    /// <summary>The median colour of the pixels of <paramref name="area"/> (the background of a row, a header).</summary>
    public static Color Median(Bitmap capture, Rectangle area)
    {
        List<Color> colors = [];
        for (int y = Math.Max(0, area.Top); y < Math.Min(capture.Height, area.Bottom); y++)
        {
            for (int x = Math.Max(0, area.Left); x < Math.Min(capture.Width, area.Right); x++)
            {
                colors.Add(capture.GetPixel(x, y));
            }
        }

        if (colors.Count == 0)
        {
            throw new ArgumentException($"The area {area} is outside the capture {capture.Size}.", nameof(area));
        }

        return Color.FromArgb(255, colors.Select(static c => c.R).Order().ElementAt(colors.Count / 2), colors.Select(static c => c.G).Order().ElementAt(colors.Count / 2), colors.Select(static c => c.B).Order().ElementAt(colors.Count / 2));
    }

    /// <summary>The largest difference of a pixel of <paramref name="area"/> from <paramref name="background"/> (0 = empty).</summary>
    public static int LargestDeviation(Bitmap capture, Rectangle area, Color background)
    {
        int max = 0;
        for (int y = Math.Max(0, area.Top); y < Math.Min(capture.Height, area.Bottom); y++)
        {
            for (int x = Math.Max(0, area.Left); x < Math.Min(capture.Width, area.Right); x++)
            {
                max = Math.Max(max, Distance(capture.GetPixel(x, y), background));
            }
        }

        return max;
    }

    public static QuadrantColor Classify(Color c) =>
        c.R > 170 && c.G < 90 && c.B < 90 ? QuadrantColor.Red
        : c.G > 170 && c.R < 90 && c.B < 90 ? QuadrantColor.Green
        : c.B > 170 && c.R < 90 && c.G < 90 ? QuadrantColor.Blue
        : c.R > 170 && c.G > 170 && c.B < 90 ? QuadrantColor.Yellow
        : QuadrantColor.Other;

    /// <summary>
    /// The colours of the four quadrants (top left, top right, bottom left, bottom right) of the test photo shown somewhere in
    /// <paramref name="area"/>: the bounding box of its strongly coloured pixels, sampled at a quarter and three quarters of it.
    /// <see langword="null"/> when there is no such picture.
    /// </summary>
    public static (QuadrantColor[] Colors, Rectangle Bounds)? ReadQuadrants(Bitmap capture, Rectangle area)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        for (int y = Math.Max(0, area.Top); y < Math.Min(capture.Height, area.Bottom); y++)
        {
            for (int x = Math.Max(0, area.Left); x < Math.Min(capture.Width, area.Right); x++)
            {
                if (Classify(capture.GetPixel(x, y)) != QuadrantColor.Other)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        if (left > right || right - left < 3 || bottom - top < 3)
        {
            return null;
        }

        Rectangle bounds = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        QuadrantColor At(double fx, double fy) => Classify(capture.GetPixel(left + (int)((right - left) * fx), top + (int)((bottom - top) * fy)));
        return ([At(0.25, 0.25), At(0.75, 0.25), At(0.25, 0.75), At(0.75, 0.75)], bounds);
    }

    /// <summary>
    /// The photo of a phone held upright: 600 × 400 pixels stored lying (top left red, top right green, bottom left blue,
    /// bottom right yellow) with the EXIF orientation 6 (rotate 90° clockwise to show), as a JPEG.
    /// </summary>
    public static byte[] PhonePhotoJpeg()
    {
        using Bitmap bitmap = new(600, 400);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.FillRectangle(Brushes.Red, 0, 0, 300, 200);
            g.FillRectangle(Brushes.Lime, 300, 0, 300, 200);
            g.FillRectangle(Brushes.Blue, 0, 200, 300, 200);
            g.FillRectangle(Brushes.Yellow, 300, 200, 300, 200);
        }

        using MemoryStream jpeg = new();
        bitmap.Save(jpeg, System.Drawing.Imaging.ImageFormat.Jpeg);
        byte[] data = jpeg.ToArray();
        // APP1 "Exif" with a little-endian TIFF header and one IFD entry: Orientation (0x0112), SHORT, 1 value: 6.
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        return [.. data[..2], .. exif, .. data[2..]];
    }

    /// <summary>The quadrants of <see cref="PhonePhotoJpeg"/> shown upright (rotated 90° clockwise).</summary>
    public static readonly QuadrantColor[] UprightQuadrants = [QuadrantColor.Blue, QuadrantColor.Red, QuadrantColor.Yellow, QuadrantColor.Green];

    /// <summary>The quadrants of <see cref="PhonePhotoJpeg"/> as stored (what a viewer ignoring EXIF shows).</summary>
    public static readonly QuadrantColor[] StoredQuadrants = [QuadrantColor.Red, QuadrantColor.Green, QuadrantColor.Blue, QuadrantColor.Yellow];

    /// <summary>
    /// The access key underlines (T-042 point 2): where <paramref name="withKeys"/> (menu opened with the keyboard, access keys
    /// shown) differs from <paramref name="withoutKeys"/> (opened with the mouse). Every difference must lie in the lower
    /// half of a row in <paramref name="rowsWithKeys"/> and form one thin horizontal line there; every such row must have
    /// one; nothing may differ elsewhere. Returns the problems found (empty = as expected) and a description of the lines.
    /// </summary>
    public static (List<string> Problems, List<string> Lines) CheckUnderlines(Bitmap withKeys, Bitmap withoutKeys, IReadOnlyDictionary<string, Rectangle> rowsWithKeys)
    {
        List<string> problems = [];
        List<string> lines = [];
        if (withKeys.Size != withoutKeys.Size)
        {
            problems.Add($"The two menus differ in size: {withKeys.Size} and {withoutKeys.Size}.");
            return (problems, lines);
        }

        Dictionary<string, List<Point>> inRows = rowsWithKeys.Keys.ToDictionary(static key => key, static _ => new List<Point>());
        List<Point> elsewhere = [];
        for (int y = 0; y < withKeys.Height; y++)
        {
            for (int x = 0; x < withKeys.Width; x++)
            {
                if (Distance(withKeys.GetPixel(x, y), withoutKeys.GetPixel(x, y)) <= 40)
                {
                    continue;
                }

                string? row = rowsWithKeys.FirstOrDefault(pair => pair.Value.Contains(x, y) && y >= pair.Value.Top + (pair.Value.Height / 2)).Key;
                (row is null ? elsewhere : inRows[row]).Add(new Point(x, y));
            }
        }

        if (elsewhere.Count > 0)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"{elsewhere.Count} pixels differ outside the lower half of the rows with access keys, e.g. ({elsewhere[0].X},{elsewhere[0].Y})."));
        }

        foreach ((string row, List<Point> points) in inRows)
        {
            if (points.Count == 0)
            {
                problems.Add($"'{row}': no underline.");
                continue;
            }

            int width = points.Max(static p => p.X) - points.Min(static p => p.X) + 1;
            int height = points.Max(static p => p.Y) - points.Min(static p => p.Y) + 1;
            string line = string.Create(CultureInfo.InvariantCulture, $"'{row}': {width}×{height} px at ({points.Min(static p => p.X)},{points.Min(static p => p.Y)}), {points.Count} pixels");
            lines.Add(line);
            // A line under one letter: wider than tall, at most 3 px thick, at most about two letters wide.
            if (height > 3 || width < 3 || width > 30 || width <= height)
            {
                problems.Add($"{line}: not a thin horizontal line under one letter.");
            }
        }

        return (problems, lines);
    }
}
