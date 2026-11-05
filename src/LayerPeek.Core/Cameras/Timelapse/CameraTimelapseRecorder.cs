using Microsoft.Extensions.Logging;

using LayerPeek.Core.Cameras.Recording;

namespace LayerPeek.Core.Cameras.Timelapse;

/// <summary>
/// <see cref="ICameraTimelapseRecorder"/> over the shared camera connections of <see cref="CameraStreamHub"/> (T-143): every
/// timelapse takes its own packet lease of key frames only, so it runs with or without an open camera window or a recording
/// and never decodes.
/// </summary>
public sealed class CameraTimelapseRecorder : ICameraTimelapseRecorder
{
    /// <summary>
    /// Key frames only: the other packets are not even copied for the timelapse. 16 items are ~35 s of the Buddy3D's key
    /// frames (one per ~2.2 s); a disk that stalls longer fails the timelapse (<see cref="CameraRecordingFailure.TooSlow"/>)
    /// instead of holding more memory.
    /// </summary>
    private static readonly CameraPacketSubscriptionOptions PacketOptions = new() { KeyFramesOnly = true, QueueCapacity = 16 };

    private readonly CameraStreamHub _hub;
    private readonly ICameraRecordingWriterFactory _writers;
    private readonly ILogger<CameraTimelapseRecorder> _logger;

    public CameraTimelapseRecorder(CameraStreamHub hub, ICameraRecordingWriterFactory writers, ILogger<CameraTimelapseRecorder> logger)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(logger);
        _hub = hub;
        _writers = writers;
        _logger = logger;
    }

    public CameraRecorderAvailability Availability => _writers.Availability;

    public ICameraTimelapse Start(string printerId, string filePath, Action<ICameraTimelapse>? changed = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        CameraRecorder.ValidatePath(filePath);
        CameraRecorderAvailability availability = _writers.Availability;
        if (!availability.IsAvailable)
        {
            return CameraTimelapse.FailedAtOnce(printerId, CameraRecordingFailure.Unavailable, availability.Reason ?? "Recording is not available.", _logger);
        }

        // Like a recording: an unknown or switched-off camera gives a lease that has already ended (CameraUnavailable).
        CameraPacketSubscription packets = _hub.SubscribePackets(printerId, options: PacketOptions);
        return CameraTimelapse.Start(printerId, filePath, packets, _writers, changed, _logger);
    }
}
