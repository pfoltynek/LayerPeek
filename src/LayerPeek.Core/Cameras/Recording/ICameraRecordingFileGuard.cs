namespace LayerPeek.Core.Cameras.Recording;

/// <summary>
/// Keeps files and folders that are read or written outside the recorders (T-165: the timelapse a JPEG export reads and the
/// folder it writes) away from the rotation. Implemented by <see cref="CameraRecordingService"/>, which owns the rotation.
/// </summary>
public interface ICameraRecordingFileGuard
{
    /// <summary>
    /// The rotation deletes none of <paramref name="paths"/> (full paths, case-insensitive) until the returned handle is
    /// disposed; disposing it (once is enough, more is harmless) requests a rotation, so that what was written meanwhile
    /// counts in the size limit. Thread-safe; never blocks and never throws.
    /// </summary>
    IDisposable Hold(IReadOnlyList<string> paths);
}
