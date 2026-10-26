using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.Core.Tests.Settings;

/// <summary>Camera overlay parts of <see cref="AppSettings"/> (T-077): the global card opacity and the layout helpers.</summary>
public class AppSettingsCameraOverlayTests
{
    private static readonly CameraOverlayLayout Changed = CameraOverlayLayout.Default.WithVisible(OverlayWidgetIds.Cooldown, false);

    [Fact]
    public void OpacityDefaultsToSeventyTwoPercent()
    {
        Assert.Equal(72, CameraOverlayOpacity.DefaultPercent);
        Assert.Equal(30, CameraOverlayOpacity.MinPercent);
        Assert.Equal(95, CameraOverlayOpacity.MaxPercent);
        Assert.Equal(72, AppSettings.Default.CameraOverlayOpacityPercent);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(72)]
    [InlineData(95)]
    public void OpacityWithinRangeIsAccepted(int percent)
    {
        Assert.Equal(percent, new AppSettings { CameraOverlayOpacityPercent = percent }.CameraOverlayOpacityPercent);
        Assert.True(CameraOverlayOpacity.IsValid(percent));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(96)]
    [InlineData(0)]
    [InlineData(-72)]
    [InlineData(100)]
    public void OpacityOutsideRangeIsRejected(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppSettings { CameraOverlayOpacityPercent = percent });
        Assert.False(CameraOverlayOpacity.IsValid(percent));
    }

    [Fact]
    public void OpacityIsPartOfEquality()
    {
        AppSettings changed = new() { CameraOverlayOpacityPercent = 50 };

        Assert.NotEqual(AppSettings.Default, changed);
        Assert.Equal(changed, new AppSettings { CameraOverlayOpacityPercent = 50 });
    }

    [Fact]
    public void WithCameraOverlayLayoutChangesOnlyThatPrinter()
    {
        AppSettings settings = new() { PollingInterval = TimeSpan.FromSeconds(9), Printers = [Printer("a"), Printer("b")] };

        AppSettings updated = settings.WithCameraOverlayLayout("A", Changed);

        Assert.Equal(Changed, updated.Printers[0].Camera.Overlay);
        Assert.Same(settings.Printers[1], updated.Printers[1]);
        Assert.Equal(settings with { Printers = [updated.Printers[0], settings.Printers[1]] }, updated);
        Assert.Equal(settings.Printers[0] with { Camera = settings.Printers[0].Camera with { Overlay = Changed } }, updated.Printers[0]);
    }

    [Fact]
    public void WithCameraOverlayLayoutReturnsTheSameInstanceWithoutAChange()
    {
        AppSettings settings = new() { Printers = [Printer("a")] };

        Assert.Same(settings, settings.WithCameraOverlayLayout("a", CameraOverlayLayout.Default));
        Assert.Same(settings, settings.WithCameraOverlayLayout("unknown", Changed));
        Assert.Throws<ArgumentNullException>(() => settings.WithCameraOverlayLayout("a", null!));
        Assert.Throws<ArgumentNullException>(() => settings.WithCameraOverlayLayout(null!, Changed));
    }

    [Fact]
    public void WithCameraOverlayLayoutForAllPrintersCopiesItEverywhere()
    {
        AppSettings settings = new() { Printers = [Printer("a"), Printer("b") with { Camera = new CameraSettings(enabled: true, new Uri("rtsp://10.0.0.9/live")) }] };

        AppSettings updated = settings.WithCameraOverlayLayoutForAllPrinters(Changed);

        Assert.All(updated.Printers, static printer => Assert.Equal(Changed, printer.Camera.Overlay));
        Assert.True(updated.Printers[1].Camera.Enabled);
        Assert.Same(updated, updated.WithCameraOverlayLayoutForAllPrinters(Changed));
    }

    private static PrinterDefinition Printer(string id) =>
        new() { Id = id, Name = id, ConnectionType = ConnectionType.PrusaLink, Address = "192.168.1.20" };
}
