namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// The recordings as the print failure detection sees them (T-153: alarm snapshots go next to the recordings and count in
/// their size limit; T-155: a confirmed failure saves the dashcam). Implemented by <see cref="CameraRecordingService"/>,
/// which owns the rotation and the dashcam.
/// </summary>
public interface ICameraRecordingsFolder
{
    /// <summary>The root of the recordings (every printer has a subfolder in it, <see cref="CameraRecordingFolders.PrinterFolderNames"/>).</summary>
    string RecordingsFolder { get; }

    /// <summary>
    /// Files were added to the printer folders: the rotation runs soon on the thread pool (once, however often it is asked
    /// meanwhile) and deletes the oldest files when the size limit is exceeded. Never blocks and never throws.
    /// </summary>
    void RequestRotation();

    /// <summary>
    /// Saves the last minutes of the printer's dashcam and the time after the event (T-148,
    /// <see cref="CameraRecordingService.SaveDashcam"/>); for the print failure detection with
    /// <see cref="CameraDashcamTrigger.FailureDetected"/>. Never blocks on the save and never throws.
    /// </summary>
    /// <returns><see langword="false"/> when nothing is saved (no dashcam buffering, the trigger switched off).</returns>
    bool SaveDashcam(string printerId, CameraDashcamTrigger trigger);
}
