namespace PrusaTrayDiagnostic.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Loads the settings. Never fails because of a missing, corrupted or unreadable file: it returns
    /// <see cref="AppSettings.Default"/> and describes the problem in <see cref="SettingsLoadResult"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves the settings atomically: on failure or cancellation the previously saved settings stay intact.
    /// </summary>
    /// <exception cref="IOException">The settings could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
