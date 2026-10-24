namespace LayerPeek.Core.Cameras;

/// <summary>One-off image from a camera: the first decoded frame of a fresh stream (no reconnecting).</summary>
public static class CameraSnapshot
{
    /// <summary>
    /// Opens the stream, copies its first frame and closes the stream again. There is no built-in timeout; pass a
    /// <paramref name="cancellationToken"/> that limits the wait.
    /// </summary>
    /// <exception cref="CameraFeedException">The stream failed or ended before the first frame.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static Task<CameraImage> CaptureAsync(ICameraFeedFactory factory, Uri streamUri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(streamUri);
        if (!streamUri.IsAbsoluteUri)
        {
            throw new ArgumentException("The stream address must be absolute.", nameof(streamUri));
        }

        return CaptureCoreAsync(factory, streamUri, cancellationToken);
    }

    private static async Task<CameraImage> CaptureCoreAsync(ICameraFeedFactory factory, Uri streamUri, CancellationToken cancellationToken)
    {
        ICameraFeed stream = factory.Create(streamUri) ?? throw new CameraFeedException("No camera stream is available.");
        await using (stream.ConfigureAwait(false))
        {
            await foreach (CameraFrame frame in stream.ReadFramesAsync(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                return frame.ToImage();
            }
        }

        throw new CameraFeedException("The camera ended the stream before sending an image.");
    }
}
