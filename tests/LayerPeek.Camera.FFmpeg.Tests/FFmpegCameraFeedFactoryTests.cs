using System.Diagnostics;

using LayerPeek.Core.Cameras;

namespace LayerPeek.Camera.FFmpeg.Tests;

public sealed class FFmpegCameraFeedFactoryTests
{
    [Fact]
    public void NullAddressIsRejected()
    {
        FFmpegCameraFeedFactory factory = new();

        Assert.Throws<ArgumentNullException>(() => factory.Create(null!));
    }

    [Fact]
    public void RelativeAddressIsRejected()
    {
        FFmpegCameraFeedFactory factory = new();

        Assert.Throws<ArgumentException>(() => factory.Create(new Uri("live", UriKind.Relative)));
    }

    [Theory]
    [InlineData("http://192.168.0.227/live")]
    [InlineData("rtsps://192.168.0.227/live")]
    [InlineData("udp://192.168.0.227:554")]
    public void UnsupportedSchemeThrowsCameraFeedException(string address)
    {
        FFmpegCameraFeedFactory factory = new();

        CameraFeedException error = Assert.Throws<CameraFeedException>(() => factory.Create(new Uri(address)));

        Assert.DoesNotContain("192.168.0.227", error.Message, StringComparison.Ordinal);
    }

    [FFmpegFact]
    public async Task CreateDoesNotConnect()
    {
        await using LoopbackServer server = new(LoopbackBehavior.Silent);
        FFmpegCameraFeedFactory factory = new();

        Stopwatch watch = Stopwatch.StartNew();
        ICameraFeed feed = factory.Create(server.StreamUri);
        _ = feed.ReadFramesAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        await feed.DisposeAsync();

        Assert.True(watch.Elapsed < TestFixtures.Prompt);
        Assert.Equal(0, server.Connections);
    }

    [FFmpegFact]
    public void ReportsTheLoadedFFmpegVersion()
    {
        string version = FFmpegCameraFeedFactory.LibraryVersion;

        Assert.StartsWith("avformat 63.", version, StringComparison.Ordinal);
        // Both builds (BtbN on Windows, build/macos/build-ffmpeg.sh on macOS) are LGPL 3 (T-014, T-026).
        Assert.EndsWith("(LGPL version 3 or later)", version, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("avformat-63", "libavformat.63.dylib")]
    [InlineData("avcodec-63", "libavcodec.63.dylib")]
    [InlineData("avutil-61", "libavutil.61.dylib")]
    [InlineData("swscale-10", "libswscale.10.dylib")]
    [InlineData("swresample-7", "libswresample.7.dylib")]
    [InlineData("avfilter-12", "libavfilter.12.dylib")]
    [InlineData("avdevice-63", "libavdevice.63.dylib")]
    public void MacOSLibraryNamesAreTheInstallNamesOfAnFFmpegBuild(string libraryName, string fileName)
    {
        Assert.Equal(fileName, FFmpegRuntime.MacOSFileName(libraryName));
    }

    [Theory]
    [InlineData("libSkiaSharp")]
    [InlineData("avformat")]
    [InlineData("avformat-")]
    [InlineData("-63")]
    [InlineData("avformat-6x")]
    [InlineData("libavformat-63")]
    [InlineData("other-1")]
    public void OtherLibrariesAreLeftToTheDefaultSearch(string libraryName)
    {
        Assert.Null(FFmpegRuntime.MacOSFileName(libraryName));
    }
}
