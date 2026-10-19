using System.Diagnostics;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

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
        Assert.Contains("LGPL", version, StringComparison.Ordinal);
    }
}
