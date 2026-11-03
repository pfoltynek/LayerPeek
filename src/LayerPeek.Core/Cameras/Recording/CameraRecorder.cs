using Microsoft.Extensions.Logging;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// <see cref="ICameraRecorder"/> over the shared camera connections of <see cref="CameraStreamHub"/> (T-143): every
/// recording takes its own packet lease, so it runs with or without an open camera window and never decodes.
/// </summary>
public sealed class CameraRecorder : ICameraRecorder
{
    private readonly CameraStreamHub _hub;
    private readonly ICameraRecordingWriterFactory _writers;
    private readonly ILogger<CameraRecorder> _logger;

    public CameraRecorder(CameraStreamHub hub, ICameraRecordingWriterFactory writers, ILogger<CameraRecorder> logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(logger);
        _hub = hub;
        _writers = writers;
        _logger = logger;
    }

    public CameraRecorderAvailability Availability => _writers.Availability;

    public ICameraRecording Start(string printerId, string filePath, Action<ICameraRecording>? changed = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ValidatePath(filePath);
        CameraRecorderAvailability availability = _writers.Availability;
        if (!availability.IsAvailable)
        {
            return CameraRecording.FailedAtOnce(printerId, filePath, CameraRecordingFailure.Unavailable, availability.Reason ?? "Recording is not available.", _logger);
        }

        // An unknown or switched-off camera gives a lease that has already ended: the recording then fails with
        // CameraUnavailable on its own task.
        CameraPacketSubscription packets = _hub.SubscribePackets(printerId);
        return CameraRecording.Start(printerId, filePath, packets, _writers, changed, _logger);
    }

    internal static void ValidatePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException("The recording path must be absolute.", nameof(filePath));
        }

        if (string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(filePath)) || Path.GetDirectoryName(filePath) is null)
        {
            throw new ArgumentException("The recording path needs a folder and a file name.", nameof(filePath));
        }
    }
}
