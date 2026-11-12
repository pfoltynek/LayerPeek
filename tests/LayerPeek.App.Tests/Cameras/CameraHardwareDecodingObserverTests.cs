using Microsoft.Extensions.DependencyInjection;

using LayerPeek.App.Cameras;
using LayerPeek.App.Composition;
using LayerPeek.Camera.FFmpeg;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tests.Cameras;

/// <summary>T-203: the "Hardware decoding" setting reaches the FFmpeg decoder after loading and every save.</summary>
public sealed class CameraHardwareDecodingObserverTests
{
    [Fact]
    public void TheLoadedAndEverySavedSettingSetTheDecoder()
    {
        FFmpegCameraFeedFactory decoder = new();
        CameraHardwareDecodingObserver observer = new(decoder);

        observer.ApplySettings(new AppSettings { CameraHardwareDecoding = CameraHardwareDecoding.Off });
        Assert.Equal(CameraHardwareDecoding.Off, decoder.HardwareDecoding);
        observer.UpdateSettings(new AppSettings());
        Assert.Equal(CameraHardwareDecoding.Automatic, decoder.HardwareDecoding);
        // A failed monitoring run changes nothing.
        observer.ReportMonitoringFailed();
        Assert.Equal(CameraHardwareDecoding.Automatic, decoder.HardwareDecoding);
    }

    [Fact]
    public void AnotherDecoderIsLeftAlone()
    {
        CameraHardwareDecodingObserver observer = new(UnavailableCameraFeedFactory.Instance);

        observer.ApplySettings(new AppSettings { CameraHardwareDecoding = CameraHardwareDecoding.Off });
        observer.UpdateSettings(new AppSettings());
    }

    [Fact]
    public async Task TheAppRegistersItForItsDecoder()
    {
        await using ServiceProvider services = AppServices.Create();

        CameraHardwareDecodingObserver observer = services.GetRequiredService<CameraHardwareDecodingObserver>();
        observer.ApplySettings(new AppSettings { CameraHardwareDecoding = CameraHardwareDecoding.Off });
        Assert.Equal(CameraHardwareDecoding.Off, Assert.IsType<FFmpegCameraFeedFactory>(services.GetRequiredService<ICameraFeedFactory>()).HardwareDecoding);
    }
}
