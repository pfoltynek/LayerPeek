using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

/// <summary>
/// The app's camera stack (FFmpeg feed under the Core reconnecting feed, as the camera window uses it) against the
/// simulated live camera of tools/PrusaSimulator (T-045): endless loop at 25 fps, several viewers, an outage.
/// </summary>
public sealed class SimulatedCameraTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [FFmpegFact]
    public async Task LiveLoopPlaysSmoothlyAcrossLoopBoundaries()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        ReconnectingCameraFeed feed = CreateReconnectingFeed();
        ConcurrentQueue<CameraConnectionState> states = new();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        stop.CancelAfter(TimeSpan.FromSeconds(30));
        int target = (2 * TestFixtures.Buddy3DFrameCount) + 10;
        int frames = 0;
        TimeSpan longestGap = TimeSpan.Zero;
        Stopwatch total = new();
        Stopwatch gap = new();

        await foreach (CameraFrame frame in feed.ReadFramesAsync(camera.StreamUri, new Status(states), stop.Token))
        {
            Assert.Equal(TestFixtures.Buddy3DWidth, frame.Width);
            Assert.Equal(TestFixtures.Buddy3DHeight, frame.Height);
            if (frames++ == 0)
            {
                total.Start();
            }
            else if (gap.Elapsed > longestGap)
            {
                longestGap = gap.Elapsed;
            }

            gap.Restart();
            if (frames == target)
            {
                await stop.CancelAsync();
            }
        }

        Assert.Equal(target, frames);
        // One connection for all loops: no "no signal" and no reconnect at the end of the clip.
        Assert.Equal([CameraConnectionState.Connecting, CameraConnectionState.Streaming], states);
        Assert.Equal(1, camera.Connections);
        // Paced like a live camera (~6 s for 151 frames at 25 fps), and never a visible stall.
        Assert.True(total.Elapsed > TimeSpan.FromSeconds(4.5), $"Took only {total.Elapsed}.");
        Assert.True(longestGap < TimeSpan.FromMilliseconds(500), $"Longest gap between frames: {longestGap}.");
    }

    [FFmpegFact]
    public async Task SeveralViewersWatchTheSameCamera()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        await using FFmpegCameraFeed first = TestFixtures.CreateFeed(camera.StreamUri);
        await using FFmpegCameraFeed second = TestFixtures.CreateFeed(camera.StreamUri);
        int maxSessions = 0;

        int[] counts = await Task.WhenAll(ReadAsync(first), ReadAsync(second));

        Assert.All(counts, static c => Assert.Equal(TestFixtures.Buddy3DFrameCount + 10, c));
        Assert.Equal(2, maxSessions);

        async Task<int> ReadAsync(FFmpegCameraFeed feed)
        {
            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            stop.CancelAfter(TimeSpan.FromSeconds(30));
            int count = 0;
            await foreach (CameraFrame frame in feed.ReadFramesAsync(stop.Token))
            {
                Assert.Equal(TestFixtures.Buddy3DWidth, frame.Width);
                maxSessions = Math.Max(maxSessions, camera.ActiveSessions);
                if (++count == TestFixtures.Buddy3DFrameCount + 10)
                {
                    break;
                }
            }

            return count;
        }
    }

    [FFmpegFact]
    public async Task ReconnectingFeedRecoversWhenTheCameraComesBack()
    {
        await using RtspCamera camera = TestFixtures.StartCamera(loop: true, realTime: true);
        ReconnectingCameraFeed feed = CreateReconnectingFeed();
        ConcurrentQueue<CameraConnectionState> states = new();
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        stop.CancelAfter(TimeSpan.FromSeconds(40));
        int frames = 0;
        int framesAfterRecovery = 0;
        bool recovered = false;

        await foreach (CameraFrame frame in feed.ReadFramesAsync(camera.StreamUri, new Status(states), stop.Token))
        {
            if (++frames == 10)
            {
                camera.SetOffline(true);
                _ = Task.Run(
                    async () =>
                    {
                        // Back online once the feed has noticed the outage.
                        while (!states.Any(static s => s is CameraConnectionState.NoSignal or CameraConnectionState.Error))
                        {
                            await Task.Delay(20, stop.Token);
                        }

                        camera.SetOffline(false);
                        recovered = true;
                    },
                    stop.Token);
            }

            if (Volatile.Read(ref recovered) && ++framesAfterRecovery == 10)
            {
                await stop.CancelAsync();
            }
        }

        Assert.Equal(10, framesAfterRecovery);
        CameraConnectionState[] seen = [.. states];
        Assert.Equal(CameraConnectionState.Streaming, seen[^1]);
        Assert.Contains(seen, static s => s is CameraConnectionState.NoSignal or CameraConnectionState.Error);
        Assert.True(camera.Connections >= 2);
    }

    private static ReconnectingCameraFeed CreateReconnectingFeed() => new(
        new FFmpegCameraFeedFactory(),
        TimeProvider.System,
        NullLogger<ReconnectingCameraFeed>.Instance,
        new CameraFeedOptions { ReconnectDelay = TimeSpan.FromMilliseconds(200), FrameTimeout = TimeSpan.FromSeconds(2) });

    private sealed class Status(ConcurrentQueue<CameraConnectionState> states) : IProgress<CameraStatus>
    {
        public void Report(CameraStatus value) => states.Enqueue(value.State);
    }
}
