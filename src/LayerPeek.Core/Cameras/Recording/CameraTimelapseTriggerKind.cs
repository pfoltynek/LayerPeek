namespace LayerPeek.Core.Cameras.Recording;

/// <summary>What takes the frames of a timelapse (<see cref="CameraRecordingSettings.TimelapseTrigger"/>).</summary>
public enum CameraTimelapseTriggerKind
{
    /// <summary>A frame every <see cref="CameraRecordingSettings.TimelapseIntervalSeconds"/> (T-160, the default).</summary>
    Time = 0,

    /// <summary>
    /// A frame every <see cref="CameraRecordingSettings.TimelapseEveryNthLayer"/>-th layer change, estimated from the Z axis
    /// (T-159, <see cref="Printers.LayerChangeDetector"/>).
    /// </summary>
    Layer = 1,
}
