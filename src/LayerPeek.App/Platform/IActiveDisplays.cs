namespace LayerPeek.App.Platform;

/// <summary>
/// The displays the system can draw on right now (T-096): connected, awake and not mirrored away. On macOS
/// <see cref="MacOS.MacActiveDisplays"/>; tests use fakes.
/// </summary>
internal interface IActiveDisplays
{
    /// <summary>Counts the active displays.</summary>
    /// <param name="count">The number of active displays (0 while every display sleeps); 0 when the count failed.</param>
    /// <param name="error">0, or the error code of the system when the count failed.</param>
    /// <returns><see langword="true"/> when <paramref name="count"/> is known.</returns>
    bool TryCount(out uint count, out int error);
}
