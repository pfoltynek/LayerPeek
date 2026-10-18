namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>Connection state of a camera stream (see <see cref="ReconnectingCameraFeed"/>).</summary>
public enum CameraConnectionState
{
    /// <summary>Opening the stream; no frame received yet in this attempt.</summary>
    Connecting,

    /// <summary>Frames are arriving.</summary>
    Streaming,

    /// <summary>
    /// The camera sends no images: no frame arrived in time, or the camera ended the stream. A reconnect follows.
    /// </summary>
    NoSignal,

    /// <summary>Opening or reading the stream failed (see <see cref="CameraStatus.Reason"/>). A reconnect follows.</summary>
    Error,
}

/// <summary>
/// Connection status with an optional reason: a short technical English text (for logs and tooltips) without the
/// address or credentials; set for <see cref="CameraConnectionState.NoSignal"/> and <see cref="CameraConnectionState.Error"/>.
/// </summary>
public sealed record CameraStatus
{
    public static CameraStatus Connecting { get; } = new(CameraConnectionState.Connecting);

    public static CameraStatus Streaming { get; } = new(CameraConnectionState.Streaming);

    public CameraStatus(CameraConnectionState state, string? reason = null)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown camera connection state.");
        }

        State = state;
        Reason = reason;
    }

    public CameraConnectionState State { get; }

    public string? Reason { get; }
}
