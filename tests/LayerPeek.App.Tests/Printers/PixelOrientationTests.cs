using LayerPeek.App.Printers;

namespace LayerPeek.App.Tests.Printers;

/// <summary>EXIF orientations 1–8 turned into upright pixels (T-039: phone photos store the pixels rotated).</summary>
public sealed class PixelOrientationTests
{
    // 3 × 2 image, stored:   1 2 3
    //                        4 5 6
    private static readonly uint[] Stored = [1, 2, 3, 4, 5, 6];

    [Theory]
    [InlineData(1, 3, new uint[] { 1, 2, 3, 4, 5, 6 })]
    [InlineData(2, 3, new uint[] { 3, 2, 1, 6, 5, 4 })] // mirrored horizontally
    [InlineData(3, 3, new uint[] { 6, 5, 4, 3, 2, 1 })] // rotated 180°
    [InlineData(4, 3, new uint[] { 4, 5, 6, 1, 2, 3 })] // mirrored vertically
    [InlineData(5, 2, new uint[] { 1, 4, 2, 5, 3, 6 })] // transposed
    [InlineData(6, 2, new uint[] { 4, 1, 5, 2, 6, 3 })] // rotated 90° clockwise
    [InlineData(7, 2, new uint[] { 6, 3, 5, 2, 4, 1 })] // transversed
    [InlineData(8, 2, new uint[] { 3, 6, 2, 5, 1, 4 })] // rotated 90° counter-clockwise
    public void EveryOrientationIsTurnedUpright(int orientation, int uprightWidth, uint[] expected)
    {
        uint[] upright = PixelOrientation.Apply(Stored, 3, 2, orientation);

        Assert.Equal(expected, upright);
        Assert.Equal(uprightWidth == 2, PixelOrientation.SwapsAxes(orientation));
    }

    [Fact]
    public void UnknownOrientationKeepsThePixels()
    {
        Assert.Equal(Stored, PixelOrientation.Apply(Stored, 3, 2, 0));
        Assert.Equal(Stored, PixelOrientation.Apply(Stored, 3, 2, 9));
    }

    [Fact]
    public void WrongPixelCountIsRejected()
    {
        Assert.Throws<ArgumentException>(() => PixelOrientation.Apply(Stored, 2, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PixelOrientation.Apply(Stored, 0, 2, 1));
    }
}
