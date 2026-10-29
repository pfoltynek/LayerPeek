namespace LayerPeek.Core.Cameras;

/// <summary>
/// A camera stream could not be opened or failed while streaming. <see cref="Exception.Message"/> is a short technical
/// English text shown to the user (as <see cref="CameraStatus.Reason"/>); it must never contain the address or credentials.
/// </summary>
public sealed class CameraFeedException : Exception
{
    public CameraFeedException()
        : base("The camera stream failed.")
    {
    }

    public CameraFeedException(string message)
        : base(message)
    {
    }

    public CameraFeedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The connection failed because the operating system has no route to the camera's address ("host unreachable");
    /// a platform may know a cause of its own (T-097). Reaches the UI as <see cref="CameraStatus.IsHostUnreachable"/>.
    /// </summary>
    public bool IsHostUnreachable { get; init; }
}
