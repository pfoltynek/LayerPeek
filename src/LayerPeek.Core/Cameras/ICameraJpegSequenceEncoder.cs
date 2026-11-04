namespace LayerPeek.Core.Cameras;

/// <summary>
/// Encodes the frames of one sequence as JPEG (T-165: the JPEG export of a timelapse) and keeps its buffers from one frame to
/// the next, unlike <see cref="ICameraImageEncoder"/>, which is for single snapshots. Core has no image codec, so the App
/// implements it with the UI toolkit (Avalonia / Skia). Not thread-safe: one sequence, one thread at a time; dispose it at
/// the end of the sequence.
/// </summary>
public interface ICameraJpegSequenceEncoder : IDisposable
{
    /// <summary>
    /// Writes <paramref name="frame"/> (borrowed; read only during the call) as a JPEG file of the same size to
    /// <paramref name="destination"/>. May throw; the caller ends the sequence.
    /// </summary>
    void Encode(in CameraFrame frame, Stream destination);
}

/// <summary>Creates <see cref="ICameraJpegSequenceEncoder"/>s; thread-safe.</summary>
public interface ICameraJpegSequenceEncoderFactory
{
    /// <param name="quality">JPEG quality, 1–100.</param>
    ICameraJpegSequenceEncoder Create(int quality);
}
