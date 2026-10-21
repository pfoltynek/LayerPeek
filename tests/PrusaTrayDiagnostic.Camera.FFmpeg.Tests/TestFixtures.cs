using PrusaTrayDiagnostic.Core.Cameras;
using PrusaTrayDiagnostic.Simulator.Camera;

namespace PrusaTrayDiagnostic.Camera.FFmpeg.Tests;

internal static class TestFixtures
{
    /// <summary>~3 s recorded from a Buddy3D camera: raw H.264 (Annex B), Constrained Baseline, 1280×720, 71 frames.</summary>
    public static Uri Buddy3D { get; } = new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "buddy3d.h264"));

    public const int Buddy3DFrameCount = 71;

    public const int Buddy3DWidth = 1280;

    public const int Buddy3DHeight = 720;

    /// <summary>0.1 s of silence as WAV (8 kHz, mono, 16 bit): a valid media file in a container other than raw H.264.</summary>
    public static Uri SilenceWav { get; } = new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "silence.wav"));

    /// <summary>A file that does not exist (nothing is created).</summary>
    public static Uri MissingFile { get; } = new Uri(Path.Combine(AppContext.BaseDirectory, "Fixtures", "does-not-exist.h264"));

    /// <summary>Generous upper bound for things that should take well under a second.</summary>
    public static readonly TimeSpan Prompt = TimeSpan.FromSeconds(5);

    /// <summary>The fixture as a parsed clip, for <see cref="RtspCamera"/>.</summary>
    public static H264Clip Buddy3DClip { get; } = H264Clip.Load(Buddy3D.LocalPath);

    /// <summary>
    /// The simulated Buddy3D camera (tools/PrusaSimulator, T-045) on a free loopback port. By default it plays the fixture
    /// once as fast as the client reads and then closes the connection (T-014 tests count the frames); with
    /// <paramref name="loop"/> and <paramref name="realTime"/> it is a live camera. <paramref name="rtpMap"/> announces
    /// another encoding in the session description, <paramref name="media"/> another media type (the payload stays H.264).
    /// </summary>
    public static RtspCamera StartCamera(bool loop = false, bool realTime = false, string? rtpMap = null, string? media = null)
    {
        RtspCameraOptions options = new() { Clip = Buddy3DClip, Loop = loop, RealTime = realTime };
        options = rtpMap is null ? options : options with { RtpMap = rtpMap };
        return new(media is null ? options : options with { Media = media });
    }

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
