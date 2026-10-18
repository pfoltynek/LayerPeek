namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>
/// One connection to a camera stream, implemented outside Core (decoder). Created by <see cref="ICameraFeedFactory"/>;
/// disposing it closes the connection. For automatic reconnection use <see cref="ReconnectingCameraFeed"/>.
/// </summary>
public interface ICameraFeed : IAsyncDisposable
{
    /// <summary>
    /// Connects (on the first <c>MoveNextAsync</c>) and yields decoded frames. Each frame is borrowed and valid only until
    /// the enumerator advances or is disposed (see <see cref="CameraFrame"/>). Can be enumerated once.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Completes when the camera ends the stream.</item>
    /// <item>Throws <see cref="CameraFeedException"/> when connecting or decoding fails; its message must be a short
    /// technical text without the address or credentials, as it is shown to the user.</item>
    /// <item>Must honour <paramref name="cancellationToken"/> promptly, also while connecting or waiting for data
    /// (it is used for timeouts); then it throws <see cref="OperationCanceledException"/>.</item>
    /// </list>
    /// </remarks>
    IAsyncEnumerable<CameraFrame> ReadFramesAsync(CancellationToken cancellationToken);
}
