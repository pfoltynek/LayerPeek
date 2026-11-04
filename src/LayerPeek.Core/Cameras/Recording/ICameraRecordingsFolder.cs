namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// The recordings folder as other writers into it see it (T-153: alarm snapshots go next to the recordings and count in
/// their size limit). Implemented by <see cref="CameraRecordingService"/>, which owns the rotation.
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
}
