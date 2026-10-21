using System.Diagnostics;

using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

/// <summary>RTSP failure paths against a fake server on the loopback interface (never the real camera).</summary>
public sealed class FFmpegCameraFeedRtspTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [FFmpegFact]
    public async Task StreamsTheRecordingFromAFakeRtspCamera()
    {
        // Exercises the low-latency path: the decoder starts from the session description without probing.
        await using RtspCamera camera = TestFixtures.StartCamera();
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(camera.StreamUri);
        using CancellationTokenSource guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(20));

        int frames = await TestFixtures.CountFramesAsync(feed, guard.Token);

        Assert.Equal(TestFixtures.Buddy3DFrameCount, frames);
        Assert.Equal(["OPTIONS", "DESCRIBE", "SETUP", "PLAY"], camera.Requests.Take(4));
        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task FragmentIsNotSentToTheCamera()
    {
        await using RtspCamera camera = TestFixtures.StartCamera();
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(new Uri(camera.StreamUri, "#secret"));
        using CancellationTokenSource guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(20));

        Assert.Equal(TestFixtures.Buddy3DFrameCount, await TestFixtures.CountFramesAsync(feed, guard.Token));

        Assert.NotEmpty(camera.RequestLines);
        Assert.All(camera.RequestLines, static line => Assert.DoesNotContain("secret", line, StringComparison.Ordinal));
    }

    [FFmpegFact]
    public async Task RefusedConnectionThrowsCameraFeedExceptionWithoutTheAddress()
    {
        using RefusingEndpoint refusing = new();
        Uri uri = refusing.StreamUri;
        // The Windows FFmpeg build does not notice a refused connect and fails only at the I/O timeout.
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(uri, ioTimeout: TimeSpan.FromSeconds(2));
        using CancellationTokenSource guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(20));

        Stopwatch watch = Stopwatch.StartNew();
        CameraFeedException error = await Assert.ThrowsAsync<CameraFeedException>(() => TestFixtures.CountFramesAsync(feed, guard.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Failing took {watch.Elapsed}.");
        Assert.Matches("refused|timed out", error.Message);
        Assert.DoesNotContain("127.0.0.1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("rtsp", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task CancellationWhileConnectingIsPrompt()
    {
        await using LoopbackServer server = new(LoopbackBehavior.Silent);
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(server.StreamUri);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        ValueTask<bool> pending = frames.MoveNextAsync();
        await WaitForConnectionAsync(server);
        Assert.False(pending.IsCompleted);

        Stopwatch watch = Stopwatch.StartNew();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);

        Assert.True(watch.Elapsed < TestFixtures.Prompt, $"Cancelling took {watch.Elapsed}.");
        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task DisposingWhileConnectingIsPrompt()
    {
        await using LoopbackServer server = new(LoopbackBehavior.Silent);
        FFmpegCameraFeed feed = TestFixtures.CreateFeed(server.StreamUri);
        IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(Ct).GetAsyncEnumerator(Ct);
        try
        {
            ValueTask<bool> pending = frames.MoveNextAsync();
            await WaitForConnectionAsync(server);

            Stopwatch watch = Stopwatch.StartNew();
            await feed.DisposeAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await pending);

            Assert.True(watch.Elapsed < TestFixtures.Prompt, $"Disposing took {watch.Elapsed}.");
            Assert.False(feed.HoldsNativeResources);
        }
        finally
        {
            await frames.DisposeAsync();
            await feed.DisposeAsync();
        }
    }

    [FFmpegFact]
    public async Task ConcurrentMoveNextIsRejected()
    {
        await using LoopbackServer server = new(LoopbackBehavior.Silent);
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(server.StreamUri);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await using IAsyncEnumerator<CameraFrame> frames = feed.ReadFramesAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Task<bool> pending = frames.MoveNextAsync().AsTask();
        await WaitForConnectionAsync(server);

        await Assert.ThrowsAsync<InvalidOperationException>(() => frames.MoveNextAsync().AsTask());
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [FFmpegFact]
    public async Task SilentServerTimesOutWithCameraFeedException()
    {
        await using LoopbackServer server = new(LoopbackBehavior.Silent);
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(server.StreamUri, ioTimeout: TimeSpan.FromSeconds(1));
        using CancellationTokenSource guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(20));

        Stopwatch watch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<CameraFeedException>(() => TestFixtures.CountFramesAsync(feed, guard.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"The timeout took {watch.Elapsed}.");
        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task ServerClosingTheConnectionThrowsCameraFeedException()
    {
        await using LoopbackServer server = new(LoopbackBehavior.CloseImmediately);
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(server.StreamUri);
        using CancellationTokenSource guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(20));

        CameraFeedException error = await Assert.ThrowsAsync<CameraFeedException>(() => TestFixtures.CountFramesAsync(feed, guard.Token));

        Assert.DoesNotContain("127.0.0.1", error.Message, StringComparison.Ordinal);
        Assert.False(feed.HoldsNativeResources);
    }

    [FFmpegFact]
    public async Task UnauthorizedAnswerThrowsCameraFeedException()
    {
        await using LoopbackServer server = new(LoopbackBehavior.Unauthorized);
        await using FFmpegCameraFeed feed = TestFixtures.CreateFeed(server.StreamUri);
        using CancellationTokenSource guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        guard.CancelAfter(TimeSpan.FromSeconds(20));

        CameraFeedException error = await Assert.ThrowsAsync<CameraFeedException>(() => TestFixtures.CountFramesAsync(feed, guard.Token));

        Assert.Contains("401", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", error.Message, StringComparison.Ordinal);
    }

    private static async Task WaitForConnectionAsync(LoopbackServer server)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (server.Connections == 0)
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "The feed never connected to the loopback server.");
            await Task.Delay(10, Ct);
        }

        // Give FFmpeg a moment to send its request and block in the read.
        await Task.Delay(100, Ct);
    }
}
