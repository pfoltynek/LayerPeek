using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

/// <summary>The FFmpeg feed under the Core camera helpers, over the recorded fixture.</summary>
public sealed class CoreIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReconnectingFeedReopensTheStreamAfterItEnds()
    {
        ReconnectingCameraFeed camera = new(
            new FFmpegCameraFeedFactory(),
            TimeProvider.System,
            NullLogger<ReconnectingCameraFeed>.Instance,
            new CameraFeedOptions { ReconnectDelay = TimeSpan.FromMilliseconds(10) });
        List<CameraStatus> statuses = [];
        SynchronousProgress status = new(statuses);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        int frames = 0;

        // One pass over the fixture, the end ("no signal"), a reconnect and some frames of the second pass.
        await foreach (CameraFrame frame in camera.ReadFramesAsync(TestFixtures.Buddy3D, status, cancellation.Token))
        {
            Assert.Equal(TestFixtures.Buddy3DWidth, frame.Width);
            if (++frames == TestFixtures.Buddy3DFrameCount + 10)
            {
                await cancellation.CancelAsync();
            }
        }

        Assert.Equal(TestFixtures.Buddy3DFrameCount + 10, frames);
        Assert.Equal(
            [
                CameraConnectionState.Connecting,
                CameraConnectionState.Streaming,
                CameraConnectionState.NoSignal,
                CameraConnectionState.Connecting,
                CameraConnectionState.Streaming,
            ],
            statuses.Select(static s => s.State));
    }

    [Fact]
    public async Task ReconnectingFeedReportsAMissingFileAsError()
    {
        ReconnectingCameraFeed camera = new(
            new FFmpegCameraFeedFactory(),
            TimeProvider.System,
            NullLogger<ReconnectingCameraFeed>.Instance,
            new CameraFeedOptions { ReconnectDelay = TimeSpan.FromMinutes(1) });
        TaskCompletionSource<CameraStatus> failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Progress status = new(s =>
        {
            if (s.State == CameraConnectionState.Error)
            {
                failed.TrySetResult(s);
            }
        });
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        Task reading = Task.Run(
            async () =>
            {
                await foreach (CameraFrame frame in camera.ReadFramesAsync(TestFixtures.MissingFile, status, cancellation.Token))
                {
                    Assert.Fail("A missing file has no frames.");
                }
            },
            Ct);
        CameraStatus error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await cancellation.CancelAsync();
        await reading;

        Assert.False(string.IsNullOrWhiteSpace(error.Reason));
        Assert.DoesNotContain("does-not-exist", error.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotIsTheFirstFrame()
    {
        CameraImage image = await CameraSnapshot.CaptureAsync(new FFmpegCameraFeedFactory(), TestFixtures.Buddy3D, Ct);

        Assert.Equal(TestFixtures.Buddy3DWidth, image.Width);
        Assert.Equal(TestFixtures.Buddy3DHeight, image.Height);
        Assert.Contains(image.Pixels.ToArray(), static b => b != 0);
    }

    private sealed class SynchronousProgress(List<CameraStatus> statuses) : IProgress<CameraStatus>
    {
        public void Report(CameraStatus value) => statuses.Add(value);
    }

    private sealed class Progress(Action<CameraStatus> report) : IProgress<CameraStatus>
    {
        public void Report(CameraStatus value) => report(value);
    }
}
