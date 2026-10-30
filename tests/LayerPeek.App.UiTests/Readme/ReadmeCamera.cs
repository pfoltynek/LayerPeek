using System.Runtime.CompilerServices;

using LayerPeek.Camera.FFmpeg;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.UiTests.Readme;

/// <summary>
/// The camera picture of the README screenshots: one frame of the real Buddy3D recording of T-014
/// (<c>tests/LayerPeek.Camera.FFmpeg.Tests/Fixtures/buddy3d.h264</c>, 1280 × 720), decoded by the app's own FFmpeg feed
/// from the local file (no network), once per run.
/// </summary>
internal static class ReadmeCamera
{
    /// <summary>
    /// The shown frame of the 71 of the clip (about 3 s; all show the same moment of the print). A late frame: the decoder
    /// has settled after the key frame, so the picture is as sharp as the stream gets.
    /// </summary>
    public const int FrameIndex = 60;

    private static readonly Lazy<Task<CameraImage>> Frame = new(static () => Task.Run(DecodeAsync));

    /// <summary>The recorded clip next to the tests (linked into the output by the project file).</summary>
    public static string ClipPath { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "buddy3d.h264");

    /// <summary>The decoded frame <see cref="FrameIndex"/> (BGRA, opaque).</summary>
    public static Task<CameraImage> FrameAsync() => Frame.Value;

    private static async Task<CameraImage> DecodeAsync()
    {
        await using ICameraFeed feed = new FFmpegCameraFeedFactory().Create(new Uri(ClipPath));
        int index = 0;
        await foreach (CameraFrame frame in feed.ReadFramesAsync(CancellationToken.None).ConfigureAwait(false))
        {
            if (index++ == FrameIndex)
            {
                // The frame is borrowed from the decoder: copied before the enumeration moves on.
                return frame.ToImage();
            }
        }

        throw new InvalidOperationException($"The clip '{ClipPath}' has only {index} frames.");
    }
}

/// <summary>A camera that shows one still picture: every connection yields it once and then stays connected.</summary>
internal sealed class StillCameraFeedFactory(CameraImage picture) : ICameraFeedFactory
{
    public ICameraFeed Create(Uri streamUri) => new StillCameraFeed(picture);

    private sealed class StillCameraFeed(CameraImage picture) : ICameraFeed
    {
        public async IAsyncEnumerable<CameraFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new CameraFrame(picture.Width, picture.Height, picture.Stride, picture.Pixels);
            // A live stream that has nothing new: the window keeps the last frame until it closes the connection.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
