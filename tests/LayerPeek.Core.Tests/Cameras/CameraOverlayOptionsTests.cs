using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Tests.Cameras;

public class CameraOverlayOptionsTests
{
    [Fact]
    public void DefaultsReproduceTodaysOverlay()
    {
        CameraOverlayOptions defaults = CameraOverlayOptions.Default;

        CameraOverlayItems today = CameraOverlayItems.State | CameraOverlayItems.Progress | CameraOverlayItems.RemainingTime;
        Assert.Equal(today, defaults.Normal);
        Assert.Equal(today, defaults.Mini);
        Assert.Equal(CameraOverlayCorner.BottomLeft, defaults.Corner);
        Assert.Equal(CameraOverlayOptions.Default, new CameraOverlayOptions());
    }

    [Fact]
    public void ItemsAreListedOncePrinterDataFirst()
    {
        CameraOverlayItems[] expected =
        [
            CameraOverlayItems.PrinterName, CameraOverlayItems.State, CameraOverlayItems.NozzleTemperature, CameraOverlayItems.BedTemperature,
            CameraOverlayItems.Progress, CameraOverlayItems.RemainingTime, CameraOverlayItems.FinishTime, CameraOverlayItems.PrintTime,
            CameraOverlayItems.FileName,
        ];
        Assert.Equal(expected, CameraOverlayOptions.Items);
        CameraOverlayItems all = CameraOverlayItems.None;
        foreach (CameraOverlayItems item in CameraOverlayOptions.Items)
        {
            Assert.Equal(0, (int)(all & item));
            all |= item;
        }

        Assert.Equal(CameraOverlayOptions.AllItems, all);
    }

    [Fact]
    public void JobItemsAreTheJobData()
    {
        Assert.Equal(
            CameraOverlayItems.Progress | CameraOverlayItems.RemainingTime | CameraOverlayItems.FinishTime | CameraOverlayItems.PrintTime | CameraOverlayItems.FileName,
            CameraOverlayOptions.JobItems);
    }

    [Theory]
    [InlineData(CameraWindowMode.Normal, CameraOverlayItems.PrinterName)]
    [InlineData(CameraWindowMode.FullScreen, CameraOverlayItems.PrinterName)]
    [InlineData(CameraWindowMode.Mini, CameraOverlayItems.FileName)]
    public void FullScreenUsesTheNormalItems(CameraWindowMode mode, CameraOverlayItems expected)
    {
        CameraOverlayOptions options = new() { Normal = CameraOverlayItems.PrinterName, Mini = CameraOverlayItems.FileName };

        Assert.Equal(expected, options.ItemsFor(mode));
    }

    [Fact]
    public void NoItemsIsAllowed()
    {
        CameraOverlayOptions options = new() { Normal = CameraOverlayItems.None, Mini = CameraOverlayItems.None };

        Assert.Equal(CameraOverlayItems.None, options.Normal);
        Assert.Equal(CameraOverlayItems.None, options.Mini);
    }

    [Theory]
    [InlineData(1 << 9)]
    [InlineData(-1)]
    public void UnknownItemsAreRejected(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraOverlayOptions { Normal = (CameraOverlayItems)value });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraOverlayOptions { Mini = (CameraOverlayItems)value });
        Assert.False(CameraOverlayOptions.IsValid((CameraOverlayItems)value));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(-1)]
    public void UnknownCornerIsRejected(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraOverlayOptions { Corner = (CameraOverlayCorner)value });

    [Fact]
    public void EqualityComparesAllValues()
    {
        CameraOverlayOptions options = new() { Normal = CameraOverlayItems.BedTemperature, Mini = CameraOverlayItems.None, Corner = CameraOverlayCorner.TopRight };
        CameraOverlayOptions same = new() { Normal = CameraOverlayItems.BedTemperature, Mini = CameraOverlayItems.None, Corner = CameraOverlayCorner.TopRight };

        Assert.Equal(options, same);
        Assert.Equal(options.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(options, same with { Normal = CameraOverlayItems.None });
        Assert.NotEqual(options, same with { Mini = CameraOverlayItems.State });
        Assert.NotEqual(options, same with { Corner = CameraOverlayCorner.TopLeft });
    }

    [Fact]
    public void CameraSettingsHaveDefaultOverlay()
    {
        Assert.Same(CameraOverlayOptions.Default, CameraSettings.Disabled.Overlay);
        Assert.Same(CameraOverlayOptions.Default, new CameraSettings(enabled: true, new Uri("rtsp://192.168.0.227/live")).Overlay);
        Assert.Same(CameraOverlayOptions.Default, CameraSettings.FromInvalidStreamUrl("http://a/live").Overlay);
    }

    [Fact]
    public void CameraSettingsEqualityIncludesOverlay()
    {
        CameraSettings camera = new(enabled: true, new Uri("rtsp://192.168.0.227/live"));
        CameraSettings changed = camera with { Overlay = new CameraOverlayOptions { Corner = CameraOverlayCorner.TopLeft } };
        CameraSettings same = camera with { Overlay = new CameraOverlayOptions { Corner = CameraOverlayCorner.TopLeft } };

        Assert.NotEqual(camera, changed);
        Assert.Equal(changed, same);
        Assert.Equal(changed.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public void CameraSettingsRejectNullOverlay() =>
        Assert.Throws<ArgumentNullException>(() => CameraSettings.Disabled with { Overlay = null! });

    [Fact]
    public void CameraSettingsToStringShowsOverlayButHidesInvalidAddress()
    {
        string text = (CameraSettings.FromInvalidStreamUrl("http://secret-host/live") with { Overlay = new CameraOverlayOptions { Normal = CameraOverlayItems.FileName } }).ToString();

        Assert.Contains("Overlay = ", text, StringComparison.Ordinal);
        Assert.Contains("FileName", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-host", text, StringComparison.Ordinal);
    }
}
