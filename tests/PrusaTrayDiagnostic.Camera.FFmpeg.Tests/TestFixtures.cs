using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

internal static class TestFixtures
{
    /// <summary>~3 s recorded from a Buddy3D camera: raw H.264 (Annex B), Constrained Baseline, 1280×720, 71 frames.</summary>
    public static Uri Buddy3D { get; } = new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "buddy3d.h264"));

    public const int Buddy3DFrameCount = 71;

    public const int Buddy3DWidth = 1280;

    public const int Buddy3DHeight = 720;

    /// <summary>A file that does not exist (nothing is created).</summary>
    public static Uri MissingFile { get; } = new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist.h264"));

    /// <summary>Generous upper bound for things that should take well under a second.</summary>
    public static readonly TimeSpan Prompt = TimeSpan.FromSeconds(5);

    public static FFmpegCameraFeed CreateFeed(Uri uri, TimeSpan? ioTimeout = null) =>
        (FFmpegCameraFeed)(ioTimeout is { } timeout ? new FFmpegCameraFeedFactory(timeout) : new FFmpegCameraFeedFactory()).Create(uri);

    /// <summary>Reads all frames and checks the shape of each; returns the count.</summary>
    public static async Task<int> CountFramesAsync(ICameraFeed feed, CancellationToken cancellationToken)
    {
        int count = 0;
        await foreach (CameraFrame frame in feed.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
        {
            Assert.Equal(Buddy3DWidth, frame.Width);
            Assert.Equal(Buddy3DHeight, frame.Height);
            count++;
        }

        return count;
    }
}
