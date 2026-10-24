using System.Drawing;

using LayerPeek.App.E2E.Tests.Infrastructure;

namespace LayerPeek.App.E2E.Tests;

/// <summary>
/// Unit tests of the pure helpers of T-069 (code points of invisible characters, access keys from the resources, pixel
/// checks, the test photo, the screen area guard): they run in every <c>dotnet test</c> and touch nothing of the system.
/// </summary>
public sealed class TrayMenuHelperTests
{
    [Fact]
    public void CodePointsShowInvisibleCharactersAndKeepVisibleOnes()
    {
        Assert.Equal("MK4<U+2060>_dílna: Ready", TrayMenu.CodePoints("MK4⁠_dílna: Ready"));
        Assert.Equal("a<U+200B>b<U+00AD>c<U+0009>", TrayMenu.CodePoints("a​b­c\t"));
        Assert.Equal("Diagnostics…", TrayMenu.CodePoints("Diagnostics…"));
        Assert.Equal(string.Empty, TrayMenu.CodePoints(null));
    }

    [Fact]
    public void WithoutWordJoinerRemovesOnlyU2060()
    {
        Assert.Equal("MK4_dílna: Ready", TrayMenu.WithoutWordJoiner("MK4⁠_dílna: Ready"));
        Assert.Equal("a​b", TrayMenu.WithoutWordJoiner("a⁠​b"));
    }

    [Theory]
    [InlineData("_Settings…", "S", "Settings…")]
    [InlineData("Add _printer…", "P", "Add printer…")]
    [InlineData("D_iagnose…", "I", "Diagnose…")]
    [InlineData("Show _camera", "C", "Show camera")]
    [InlineData("No key", null, "No key")]
    public void AccessKeyLetterIsTheCharacterAfterTheMarker(string resource, string? key, string shown)
    {
        Assert.Equal(key, TrayMenu.AccessKeyLetter(resource));
        Assert.Equal(shown, TrayMenu.ShownText(resource));
    }

    [Fact]
    public void ResourcesOfTheRepositoryAreRead()
    {
        Assert.Equal("_Settings…", TrayMenu.Resource("MenuSettings"));
        Assert.Equal("_Nastavení…", TrayMenu.Resource("MenuSettings", "cs"));
        Assert.Throws<InvalidOperationException>(() => TrayMenu.Resource("NoSuchKey"));
    }

    [Theory]
    [InlineData(1.0, 16)]
    [InlineData(1.25, 20)]
    [InlineData(1.5, 24)]
    [InlineData(2.0, 32)]
    [InlineData(1.75, 32)]
    public void IllustrationSizeIsTheSmallestWithEnoughPixels(double scaling, int expected) =>
        Assert.Equal(expected, PixelChecks.IllustrationPixelSize(16, scaling));

    [Fact]
    public void ModelsMapToTheIllustrationsOfTheApp()
    {
        Assert.Equal("core-one", PixelChecks.IllustrationIdOf("Core One"));
        Assert.Equal("mk4", PixelChecks.IllustrationIdOf("MK4S"));
        Assert.Equal("xl", PixelChecks.IllustrationIdOf("XL"));
        Assert.Equal("mini", PixelChecks.IllustrationIdOf("MINI"));
        Assert.Equal("generic", PixelChecks.IllustrationIdOf("Unknown"));
    }

    [Fact]
    public void IllustrationsOfTheRepositoryLoadInTheirSize()
    {
        foreach (string id in PixelChecks.IllustrationIds)
        {
            using Bitmap illustration = PixelChecks.LoadIllustration(id, 16);
            Assert.Equal(new Size(16, 16), illustration.Size);
        }
    }

    [Fact]
    public void ComposingUsesStraightAlpha()
    {
        using Bitmap picture = new(2, 1);
        picture.SetPixel(0, 0, Color.FromArgb(128, 255, 0, 0));
        picture.SetPixel(1, 0, Color.FromArgb(0, 255, 255, 255));

        using Bitmap composed = PixelChecks.ComposeOn(picture, Color.FromArgb(255, 0, 0, 100));

        Assert.Equal(Color.FromArgb(255, 128, 0, 50), composed.GetPixel(0, 0));
        Assert.Equal(Color.FromArgb(255, 0, 0, 100), composed.GetPixel(1, 0));
    }

    [Fact]
    public void ComparisonFindsTheShiftedPicture()
    {
        using Bitmap expected = Pattern(4);
        using Bitmap capture = new(10, 10);
        using (Graphics g = Graphics.FromImage(capture))
        {
            g.Clear(Color.Gray);
            g.DrawImage(expected, 4, 3);
        }

        PixelMatch match = PixelChecks.Compare(capture, new Point(3, 3), expected);

        Assert.Equal(0, match.Max);
        Assert.Equal(new Point(1, 0), match.Offset);
        Assert.True(PixelChecks.Compare(capture, new Point(0, 0), expected, slack: 0).Mean > 50);
    }

    [Fact]
    public void QuadrantsOfAPictureOnABackgroundAreRead()
    {
        using Bitmap capture = new(40, 50);
        using (Graphics g = Graphics.FromImage(capture))
        {
            g.Clear(Color.FromArgb(40, 40, 40));
            g.FillRectangle(Brushes.Blue, 10, 5, 10, 15);
            g.FillRectangle(Brushes.Red, 20, 5, 10, 15);
            g.FillRectangle(Brushes.Yellow, 10, 20, 10, 15);
            g.FillRectangle(Brushes.Lime, 20, 20, 10, 15);
        }

        (QuadrantColor[] colors, Rectangle bounds) = PixelChecks.ReadQuadrants(capture, new Rectangle(0, 0, 40, 50))!.Value;

        Assert.Equal(PixelChecks.UprightQuadrants, colors);
        Assert.Equal(new Rectangle(10, 5, 20, 30), bounds);
        Assert.Null(PixelChecks.ReadQuadrants(capture, new Rectangle(0, 40, 40, 10)));
    }

    [Fact]
    public void PhonePhotoIsStoredLyingWithExifOrientationSix()
    {
        byte[] jpeg = PixelChecks.PhonePhotoJpeg();

        Assert.Equal([0xFF, 0xD8, 0xFF, 0xE1], jpeg[..4]);
        Assert.Equal("Exif\0\0"u8.ToArray(), jpeg[6..12]);
        using MemoryStream stream = new(jpeg);
        using Bitmap decoded = new(stream);
        // System.Drawing ignores the orientation: the pixels as stored.
        Assert.Equal(new Size(600, 400), decoded.Size);
        Assert.Equal(6, (int)BitConverter.ToUInt16(decoded.GetPropertyItem(0x0112)!.Value!, 0));
        (QuadrantColor[] colors, _) = PixelChecks.ReadQuadrants(decoded, new Rectangle(0, 0, 600, 400))!.Value;
        Assert.Equal(PixelChecks.StoredQuadrants, colors);
    }

    [Fact]
    public void UnderlinesAreThinLinesInTheLowerHalfOfTheirRows()
    {
        Dictionary<string, Rectangle> rows = new() { ["Settings…"] = new Rectangle(0, 0, 60, 20), ["About"] = new Rectangle(0, 20, 60, 20) };
        using Bitmap plain = Solid(60, 40);
        using Bitmap keys = Solid(60, 40);
        Line(keys, 10, 16, 7);
        Line(keys, 12, 36, 6);

        (List<string> problems, List<string> lines) = PixelChecks.CheckUnderlines(keys, plain, rows);

        Assert.Empty(problems);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void UnderlineChecksRejectMissingLinesAndChangesElsewhere()
    {
        Dictionary<string, Rectangle> rows = new() { ["Settings…"] = new Rectangle(0, 0, 60, 20), ["About"] = new Rectangle(0, 20, 60, 20) };
        using Bitmap plain = Solid(60, 40);
        using Bitmap keys = Solid(60, 40);
        // An underline only under Settings…, and a change in the upper half of About (the text, not a line).
        Line(keys, 10, 16, 7);
        keys.SetPixel(30, 22, Color.White);

        (List<string> problems, _) = PixelChecks.CheckUnderlines(keys, plain, rows);

        Assert.Contains(problems, static p => p.Contains("outside the lower half", StringComparison.Ordinal));
        Assert.Contains(problems, static p => p.Contains("'About': no underline", StringComparison.Ordinal));
    }

    [Fact]
    public void UnderlineChecksRejectABlockInsteadOfALine()
    {
        Dictionary<string, Rectangle> rows = new() { ["Quit"] = new Rectangle(0, 0, 60, 20) };
        using Bitmap plain = Solid(60, 20);
        using Bitmap keys = Solid(60, 20);
        for (int y = 12; y < 19; y++)
        {
            Line(keys, 10, y, 8);
        }

        Assert.Contains(PixelChecks.CheckUnderlines(keys, plain, rows).Problems, static p => p.Contains("not a thin horizontal line", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 0, 2560, 1440, false)]
    [InlineData(0, 0, 1800, 1000, false)]
    [InlineData(100, 100, 800, 550, true)]
    [InlineData(0, 1392, 2560, 48, true)]
    [InlineData(2500, 1400, 40, 40, true)]
    [InlineData(-10, -10, 3000, 1500, false)]
    [InlineData(10, 10, 0, 5, false)]
    public void ScreenAreaGuardAllowsOnlyElementsNotTheDesktop(int x, int y, int width, int height, bool allowed) =>
        Assert.Equal(allowed, ScreenArea.Refusal(new Rectangle(x, y, width, height), new Rectangle(0, 0, 2560, 1440)) is null);

    private static Bitmap Pattern(int size)
    {
        Bitmap bitmap = new(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bitmap.SetPixel(x, y, (x + y) % 2 == 0 ? Color.White : Color.Black);
            }
        }

        return bitmap;
    }

    private static Bitmap Solid(int width, int height)
    {
        Bitmap bitmap = new(width, height);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.FromArgb(43, 43, 43));
        return bitmap;
    }

    private static void Line(Bitmap bitmap, int x, int y, int width)
    {
        for (int i = 0; i < width; i++)
        {
            bitmap.SetPixel(x + i, y, Color.White);
        }
    }
}
