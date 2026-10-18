namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>Creates camera stream connections; implemented outside Core (decoder).</summary>
public interface ICameraFeedFactory
{
    /// <summary>
    /// Creates a not yet connected stream for <paramref name="streamUri"/> (an absolute address, usually
    /// <see cref="CameraSettings.StreamUri"/>). Must not block on I/O; connecting happens during enumeration.
    /// </summary>
    /// <exception cref="CameraFeedException">The stream cannot be created (e.g. the decoder is not available).</exception>
    ICameraFeed Create(Uri streamUri);
}
