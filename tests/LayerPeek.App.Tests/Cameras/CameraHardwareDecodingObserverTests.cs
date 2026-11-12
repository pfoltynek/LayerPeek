using Microsoft.Extensions.DependencyInjection;

using LayerPeek.App.Cameras;
using LayerPeek.App.Composition;
using LayerPeek.App.Tray;
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
        // T-204 (applied right after saving): settings saved by the settings window reach it through the monitoring session
        // without a restart (only the polled printers restart monitoring), before the hub starts a connection for them.
        List<IMonitoringObserver> observers = [.. services.GetRequiredService<MonitoringSession>().Observers];
        Assert.Contains(observer, observers);
        Assert.True(observers.IndexOf(observer) < observers.IndexOf(services.GetRequiredService<CameraStreamHubObserver>()));
    }
}
