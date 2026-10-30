using System.Reflection;

using LayerPeek.App.Tray.Menu;

namespace LayerPeek.App.Tests.Tray.Menu;

/// <summary>
/// Size of the printer icons in the tray menu: 16 DIP on Windows (T-039, unchanged) and the NSMenu image size of
/// Avalonia.Native 12.1.4 on macOS (T-116): <c>floor(menu font × 1.333333)</c> points, 18 pt for the default 14 pt menu font.
/// </summary>
public sealed class PrinterMenuIconSizeTests
{
    [Theory]
    [InlineData(1.0, 16)]
    [InlineData(1.25, 20)]
    [InlineData(1.5, 24)]
    [InlineData(2.0, 32)]
    [InlineData(0.0, 16)]
    [InlineData(double.NaN, 16)]
    public void WindowsKeeps16DipAtTheSystemScaling(double scaling, int pixels)
    {
        PrinterMenuIconSize size = PrinterMenuIconSize.Windows(scaling);

        Assert.Equal(16, size.Size);
        Assert.Equal(pixels, size.PixelSize);
        Assert.Equal(pixels, new PrinterMenuIcons(scaling).PixelSize);
    }

    [Theory]
    [InlineData(13, 17)]
    [InlineData(14, 18)]
    [InlineData(15, 19)]
    [InlineData(16, 21)]
    public void MacIconPointsFollowAvaloniaNative(double menuFont, double points) =>
        Assert.Equal(points, PrinterMenuIconSize.MacIconPoints(menuFont));

    [Theory]
    // @1x: 18 px needed, the next pre-rendered size is 20 px (simplified art); @2x (Retina): 36 px needed, 48 px.
    [InlineData(1.0, 18, 20)]
    [InlineData(2.0, 36, 48)]
    [InlineData(3.0, 54, 56)]
    public void MacOsGets18PointsAtTheHighestDisplayScaling(double scale, int neededPixels, int pixels)
    {
        PrinterMenuIconSize size = PrinterMenuIconSize.MacOS(14, scale);

        Assert.Equal(18, size.Size);
        Assert.Equal(scale, size.Scaling);
        Assert.Equal(neededPixels, size.Size * size.Scaling);
        // Never smaller than the image AppKit draws (the bitmap is only scaled down).
        Assert.Equal(pixels, size.PixelSize);
        Assert.True(size.PixelSize >= neededPixels);
        Assert.Equal(pixels, new PrinterMenuIcons(size).PixelSize);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0.0, 0.0)]
    [InlineData(-1.0, double.NaN)]
    [InlineData(double.PositiveInfinity, -2.0)]
    public void MacOsFallsBackToTheDefaultMenuFontOnRetina(double? menuFont, double? scale)
    {
        // macOS cannot tell (no AppKit, no active display at start, T-099): 14 pt menu font, @2x.
        PrinterMenuIconSize size = PrinterMenuIconSize.MacOS(menuFont, scale);

        Assert.Equal(18, size.Size);
        Assert.Equal(2, size.Scaling);
        Assert.Equal(48, size.PixelSize);
    }

    [Fact]
    public void TheMacOsSizingIsCheckedAgainstAvaloniaNative1214()
    {
        // The NSMenu image size is taken from AvnAppMenuItem::SetIcon of libAvaloniaNative.dylib 12.1.4: check it again
        // (and the factor in PrinterMenuIconSize) when Avalonia is updated.
        string? version = typeof(Avalonia.Controls.NativeMenuItem).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.StartsWith("12.1.4", version, StringComparison.Ordinal);
        Assert.Equal(1.333333, PrinterMenuIconSize.AvaloniaNativeIconToMenuFont);
    }
}
