using Microsoft.Extensions.Logging;

namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// <see cref="ICameraRecorder"/> over the shared camera connections of <see cref="CameraStreamHub"/> (T-143): every
/// recording (and every dashcam, T-148) takes its own packet lease, so it runs with or without an open camera window and
/// never decodes.
/// </summary>
public sealed class CameraRecorder : ICameraRecorder
{
    private readonly CameraStreamHub _hub;
    private readonly ICameraRecordingWriterFactory _writers;
    private readonly TimeProvider _time;
    private readonly ILogger<CameraRecorder> _logger;

    /// <param name="timeProvider">The clock of the dashcam buffers and their saves (T-148); default: the system's.</param>
    public CameraRecorder(CameraStreamHub hub, ICameraRecordingWriterFactory writers, ILogger<CameraRecorder> logger, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(logger);
        _hub = hub;
        _writers = writers;
        _time = timeProvider ?? TimeProvider.System;
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

    public ICameraDashcam StartDashcam(string printerId, CameraDashcamOptions options, Action<ICameraDashcam>? changed = null, Action<CameraDashcamSave>? saved = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentNullException.ThrowIfNull(options);
        CameraRecorderAvailability availability = _writers.Availability;
        if (!availability.IsAvailable)
        {
            return CameraDashcam.FailedAtOnce(printerId, options, CameraRecordingFailure.Unavailable, availability.Reason ?? "Recording is not available.", _logger);
        }

        // Like a recording: an unknown or switched-off camera gives a lease that has already ended (CameraUnavailable).
        CameraPacketSubscription packets = _hub.SubscribePackets(printerId);
        return CameraDashcam.Start(printerId, packets, _writers, _time, options, changed, saved, _logger);
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
