using LayerPeek.App.Tray.Icons;

namespace LayerPeek.App.Tests.Tray;

/// <summary>Icon set by OS (T-018): Windows keeps the coloured multi-size set of T-007, macOS gets template icons.</summary>
public sealed class TrayIconSetsTests
{
    [Fact]
    public void WindowsKeepsTheColouredMultiSizeSet()
    {
        using ITrayIconSet set = TrayIconSets.ForPlatform(windows: true, macOS: false);

        ColorTrayIconSet colour = Assert.IsType<ColorTrayIconSet>(set);
        Assert.True(colour.MultiSize);
        Assert.False(set.IsTemplate);
    }

    [Fact]
    public void MacOSGetsTheTemplateSet()
    {
        using ITrayIconSet set = TrayIconSets.ForPlatform(windows: false, macOS: true);

        Assert.IsType<TemplateTrayIconSet>(set);
        Assert.True(set.IsTemplate);
    }

    [Fact]
    public void OtherOSesGetTheColouredSingleBitmapSet()
    {
        using ITrayIconSet set = TrayIconSets.ForPlatform(windows: false, macOS: false);

        Assert.False(Assert.IsType<ColorTrayIconSet>(set).MultiSize);
        Assert.False(set.IsTemplate);
    }

    [Fact]
    public void CurrentPlatformMatchesTheOS()
    {
        using ITrayIconSet set = TrayIconSets.ForCurrentPlatform();

        Assert.Equal(OperatingSystem.IsMacOS(), set.IsTemplate);
    }
}
