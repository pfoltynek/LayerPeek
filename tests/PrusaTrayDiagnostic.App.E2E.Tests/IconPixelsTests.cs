using System.Drawing;
using System.Globalization;

using PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;
using PrusaTrayDiagnostic.Tests.Shared;

namespace PrusaTrayDiagnostic.App.E2E.Tests;

/// <summary>
/// Unit tests of the pixel checks of T-070 (composition, icon search, the size rule of the tray icon, WCAG contrast, the
/// entry of the <c>.ico</c> Windows picks, the screen area guard) on synthetic images and on the real tray icon snapshots
/// of T-025: they run in every <c>dotnet test</c> and read nothing from the screen.
/// </summary>
public sealed class IconPixelsTests
{
    private static readonly Color DarkTaskbar = Color.FromArgb(0x20, 0x20, 0x20);

    /// <summary>The darker end of a light Windows 11 taskbar.</summary>
    private static readonly Color LightTaskbar = Color.FromArgb(0xEE, 0xEE, 0xEE);

    [Fact]
    public void ComposedPixelBlendsStraightAlphaOverTheBackground()
    {
        Assert.Equal(Color.FromArgb(255, 128, 0, 127), IconPixels.Compose(Color.FromArgb(128, 255, 0, 0), Color.FromArgb(0, 0, 255)));
        Assert.Equal(Color.FromArgb(255, 1, 2, 3), IconPixels.Compose(Color.FromArgb(0, 200, 200, 200), Color.FromArgb(1, 2, 3)));
        Assert.Equal(Color.FromArgb(255, 9, 8, 7), IconPixels.Compose(Color.FromArgb(255, 9, 8, 7), Color.White));
    }

    [Fact]
    public void IconIsFoundAtItsPositionOverTheBackground()
    {
        Pixels icon = TrayIcon("error", 16);
        Pixels capture = Capture(icon, DarkTaskbar, 40, 44, new Point(11, 7));

        Color background = IconPixels.Median(capture, new Rectangle(0, 0, capture.Width, capture.Height));
        IconMatch match = IconPixels.FindBest(capture, new Rectangle(0, 0, capture.Width, capture.Height), icon, background);

        Assert.Equal(DarkTaskbar.ToArgb(), background.ToArgb());
        Assert.Equal(new Point(11, 7), match.Location);
        Assert.True(match.Difference < 0.01, $"{match.Difference}");
    }

    [Fact]
    public void BackgroundAloneDoesNotMatchASparseIcon()
    {
        Pixels icon = TrayIcon("idle", 16);
        Pixels capture = Capture(null, LightTaskbar, 32, 40, default);

        IconMatch match = IconPixels.FindBest(capture, new Rectangle(0, 0, capture.Width, capture.Height), icon, LightTaskbar);

        Assert.True(match.Difference > IconPixels.MatchTolerance, $"{match.Difference}");
    }

    [Fact]
    public void DifferenceCountsOnlyTheIconsOwnPixelsComposedOverTheBackground()
    {
        // A 2×1 reference: one opaque red pixel and one nearly transparent pixel that does not count.
        Pixels reference = new(2, 1);
        reference[0, 0] = Color.FromArgb(255, 255, 0, 0);
        reference[1, 0] = Color.FromArgb(IconPixels.CoverageAlpha - 1, 0, 0, 255);
        Pixels capture = Capture(null, Color.Black, 4, 3, default);
        capture[2, 1] = Color.FromArgb(255, 204, 0, 0);
        capture[3, 1] = Color.White;

        // Only the red pixel counts: |204 - 255| / (3 × 255) = 1/15.
        Assert.Equal(1 / 15.0, IconPixels.DifferenceAt(capture, new Point(2, 1), reference, Color.Black), 6);
        IconMatch best = IconPixels.FindBest(capture, new Rectangle(0, 0, 4, 3), reference, Color.Black);
        Assert.Equal(new Point(2, 1), best.Location);
        Assert.Equal(1 / 15.0, best.Difference, 6);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    public void DifferenceAtAPlaceBeyondTheCaptureIsRefused(int x, int y)
    {
        Pixels reference = new(2, 1);
        Pixels capture = Capture(null, Color.Black, 4, 3, default);

        Assert.Throws<ArgumentOutOfRangeException>(() => IconPixels.DifferenceAt(capture, new Point(x, y), reference, Color.Black));
    }

    [Fact]
    public void SearchAreaSmallerThanTheIconFindsNothing()
    {
        Pixels icon = TrayIcon("idle", 24);
        Pixels capture = Capture(null, LightTaskbar, 20, 20, default);

        IconMatch match = IconPixels.FindBest(capture, new Rectangle(0, 0, 20, 20), icon, LightTaskbar);

        Assert.False(match.Found);
        Assert.Equal(1, match.Difference);
    }

    /// <summary>
    /// The finding of T-070 can be seen: at 150 % the icon Avalonia renders in 24 px is told apart from the 16 px icon the
    /// shell would stretch to 24 px if the app kept the size of the old scale.
    /// </summary>
    [Theory]
    [InlineData("idle")]
    [InlineData("printing-35")]
    [InlineData("error")]
    public void IconOfTheNewSizeIsToldApartFromAStretchedOldOne(string state)
    {
        Pixels expected = TrayIcon(state, 24);
        Pixels stale = IconPixels.Resize(TrayIcon(state, 16), 24, 24);
        foreach (Color taskbar in new[] { DarkTaskbar, LightTaskbar })
        {
            Pixels sharpCapture = Capture(expected, taskbar, 36, 48, new Point(6, 12));
            Pixels staleCapture = Capture(stale, taskbar, 36, 48, new Point(6, 12));
            Rectangle all = new(0, 0, 36, 48);

            TraySizeCheck sharp = TraySizeCheck.Of(sharpCapture, all, expected, stale, taskbar);
            TraySizeCheck stretched = TraySizeCheck.Of(staleCapture, all, expected, stale, taskbar);

            Assert.True(sharp.IsExpectedSize, $"{state} on {taskbar}: {sharp}");
            Assert.False(stretched.IsExpectedSize, $"{state} on {taskbar}: {stretched}");
            Assert.True(stretched.Stale.Difference <= IconPixels.MatchTolerance, $"{state} on {taskbar}: {stretched}");
        }
    }

    [Fact]
    public void ContrastRatioFollowsWcag()
    {
        Assert.Equal(21, IconPixels.ContrastRatio(Color.White, Color.Black), 3);
        Assert.Equal(21, IconPixels.ContrastRatio(Color.Black, Color.White), 3);
        Assert.Equal(1, IconPixels.ContrastRatio(Color.Gray, Color.Gray), 3);
        // Prusa orange #FD5000 on white.
        Assert.Equal(3.32, IconPixels.ContrastRatio(Color.FromArgb(0xFD, 0x50, 0x00), Color.White), 2);
    }

    /// <summary>
    /// The thresholds of the visibility check hold for the approved design on a typical light and dark taskbar, so a
    /// failing run means the taskbar or the icon really is different, not that the thresholds exclude the design.
    /// </summary>
    [Theory]
    [InlineData("idle", 16)]
    [InlineData("printing-35", 16)]
    [InlineData("error", 16)]
    [InlineData("idle", 24)]
    [InlineData("printing-35", 24)]
    [InlineData("error", 24)]
    public void ApprovedTrayIconsAreVisibleOnTypicalTaskbars(string state, int size)
    {
        Pixels icon = TrayIcon(state, size);
        foreach (Color taskbar in new[] { DarkTaskbar, LightTaskbar })
        {
            Pixels capture = Capture(icon, taskbar, size + 12, size + 20, new Point(6, 10));

            TrayVisibility visibility = TrayVisibility.Of(capture, new Point(6, 10), icon, taskbar, hasBadge: state == "error");

            Assert.True(visibility.IsVisible, $"{state}-{size} on {taskbar}: {visibility}");
        }
    }

    [Fact]
    public void IconOfTheTaskbarColourIsNotVisible()
    {
        Pixels icon = new(16, 16);
        for (int i = 0; i < icon.Argb.Length; i++)
        {
            icon.Argb[i] = Color.FromArgb(255, 0x24, 0x24, 0x24).ToArgb();
        }

        TrayVisibility visibility = TrayVisibility.Of(Capture(icon, DarkTaskbar, 28, 36, new Point(6, 10)), new Point(6, 10), icon, DarkTaskbar, hasBadge: false);

        Assert.False(visibility.IsVisible, visibility.ToString());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void BadgeRegionHoldsTheWholeBadgeAndNothingElse(int size)
    {
        // The attention icon is the idle icon plus the badge (T-025).
        Pixels idle = TrayIcon("idle", size);
        Pixels attention = TrayIcon("attention", size);
        Rectangle badge = TrayVisibility.BadgeRegion(size);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (idle[x, y] != attention[x, y])
                {
                    Assert.True(badge.Contains(x, y), $"{size} px: the badge pixel ({x},{y}) is outside {badge}.");
                }
                else if (badge.Contains(x, y))
                {
                    Assert.True(idle[x, y].A == 0, $"{size} px: the motif pixel ({x},{y}) is inside the badge region {badge}.");
                }
            }
        }
    }

    [Fact]
    public void AppIconFileHasTheSizesOfT025LargestFirst() =>
        Assert.Equal([256, 64, 48, 32, 24, 20, 16], AppIconFile.Sizes);

    [Fact]
    public void EveryEntryOfTheAppIconDecodesAtItsSize()
    {
        foreach (int size in AppIconFile.Sizes)
        {
            Pixels entry = AppIconFile.Entry(size);

            Assert.Equal((size, size), (entry.Width, entry.Height));
            // The dark tile is opaque in the middle of every entry.
            Assert.Equal(255, entry[size / 2, size / 2].A);
        }
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(24, 24)]
    [InlineData(36, 32)]
    // 175 %: 28 px is as near to 24 as to 32; the first entry (the larger one) wins (T-025).
    [InlineData(28, 32)]
    [InlineData(40, 48)]
    [InlineData(200, 256)]
    public void NearestIcoEntryWinsAndTheFirstOnATie(int wanted, int expected) =>
        Assert.Equal(expected, IconPixels.Nearest(AppIconFile.Sizes, wanted));

    [Fact]
    public void IcoDirectoryIsRead()
    {
        // Header (reserved 0, type 1, 2 entries) and two 16-byte entries: 256 px (stored as 0) and 16 px.
        byte[] ico = new byte[6 + 32];
        ico[2] = 1;
        ico[4] = 2;
        ico[6] = 0;
        ico[7] = 0;
        ico[22] = 16;
        ico[23] = 16;

        Assert.Equal([256, 16], IconPixels.IcoSizes(ico));
        Assert.Throws<InvalidDataException>(() => IconPixels.IcoSizes([0, 0, 2, 0, 1, 0]));
    }

    [Fact]
    public void WholeIconComparisonCountsAlphaButNotTheColourOfTransparentPixels()
    {
        Pixels entry = AppIconFile.Entry(32);
        Pixels transparentElsewhere = new(32, 32);
        Array.Copy(entry.Argb, transparentElsewhere.Argb, entry.Argb.Length);
        for (int i = 0; i < transparentElsewhere.Argb.Length; i++)
        {
            if (Color.FromArgb(transparentElsewhere.Argb[i]).A == 0)
            {
                // Another colour behind alpha 0 (GDI often leaves black or white there).
                transparentElsewhere.Argb[i] = Color.FromArgb(0, 255, 255, 255).ToArgb();
            }
        }

        Assert.Equal(0, IconPixels.WholeDifference(entry, transparentElsewhere), 6);
        Assert.True(IconPixels.WholeDifference(entry, new Pixels(32, 32)) > IconPixels.MatchTolerance);
        Assert.True(IconPixels.WholeDifference(entry, IconPixels.Resize(AppIconFile.Entry(16), 32, 32)) < IconPixels.WholeDifference(entry, new Pixels(32, 32)));
        Assert.Equal(1, IconPixels.WholeDifference(entry, AppIconFile.Entry(16)));
    }

    [Fact]
    public void CropKeepsThePixelsOfTheArea()
    {
        Pixels image = new(4, 3);
        image[2, 1] = Color.Red;

        Pixels part = image.Crop(new Rectangle(1, 1, 10, 10));

        Assert.Equal((3, 2), (part.Width, part.Height));
        Assert.Equal(Color.Red.ToArgb(), part[1, 0].ToArgb());
    }

    [Fact]
    public void VisibilityReportIsCultureInvariant()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");
            Assert.Equal("motif 2.50:1 (>= 2), badge 3.25:1 (>= 3)", new TrayVisibility(2.5, 3.25).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void PremultipliedPixelsAreRecognisedAndRestored()
    {
        Pixels premultiplied = new(2, 1);
        premultiplied[0, 0] = Color.FromArgb(128, 64, 32, 0);
        premultiplied[1, 0] = Color.FromArgb(255, 10, 20, 30);
        Pixels straight = new(2, 1);
        straight[0, 0] = Color.FromArgb(128, 200, 32, 0);
        straight[1, 0] = Color.FromArgb(255, 10, 20, 30);

        Assert.True(IconPixels.LooksPremultiplied(premultiplied));
        Assert.False(IconPixels.LooksPremultiplied(straight));
        Pixels restored = IconPixels.Unpremultiply(premultiplied);
        Assert.Equal(Color.FromArgb(128, 128, 64, 0), restored[0, 0]);
        Assert.Equal(Color.FromArgb(255, 10, 20, 30), restored[1, 0]);
    }

    [Fact]
    public void ScreenAreaOfAnElementIsAllowed()
    {
        Rectangle monitor = new(0, 0, 2560, 1440);

        Assert.Null(ScreenArea.Refusal(new Rectangle(2300, 1392, 40, 48), monitor));
        // Exactly a quarter is still allowed.
        Assert.Null(ScreenArea.Refusal(new Rectangle(0, 0, 1280, 720), monitor));
    }

    [Theory]
    [InlineData(0, 0, 2560, 1440)]
    [InlineData(0, 0, 1281, 720)]
    [InlineData(-100, -100, 5000, 3000)]
    [InlineData(10, 10, 0, 20)]
    // Partly on another monitor: only areas inside one monitor are measured, so it is refused.
    [InlineData(2540, 100, 40, 40)]
    public void ScreenAreaOfTheDesktopOrBeyondTheMonitorIsRefused(int left, int top, int width, int height) =>
        Assert.NotNull(ScreenArea.Refusal(new Rectangle(left, top, width, height), new Rectangle(0, 0, 2560, 1440)));

    private static Pixels TrayIcon(string state, int size) =>
        Pixels.Load(Path.Combine(TestWorktree.Root, "tests", "PrusaTrayDiagnostic.App.UiTests", "Snapshots", "TrayIcons", $"{state}-{size}.png"));

    /// <summary>A capture of <paramref name="width"/>×<paramref name="height"/> of <paramref name="background"/> with the icon drawn at <paramref name="at"/>.</summary>
    private static Pixels Capture(Pixels? icon, Color background, int width, int height, Point at)
    {
        Pixels capture = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                capture[x, y] = background;
            }
        }

        if (icon is not null)
        {
            for (int y = 0; y < icon.Height; y++)
            {
                for (int x = 0; x < icon.Width; x++)
                {
                    capture[at.X + x, at.Y + y] = IconPixels.Compose(icon[x, y], background);
                }
            }
        }

        return capture;
    }
}
