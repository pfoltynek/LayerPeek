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

    /// <summary>
    /// Removes credentials from the camera addresses in the stored settings and changes <b>nothing else</b>
    /// (T-014 decision): each <c>camera.streamUrl</c> containing an <c>@</c> loses everything up to its last <c>@</c>
    /// (as <see cref="Cameras.CameraSettings"/> does on load) and its camera gets <c>enabled: false</c>. Other values,
    /// unknown fields, skipped entries and formatting stay as they are; the write is atomic.
    /// <para>
    /// It works on any syntactically valid JSON file, also one that <see cref="LoadAsync"/> reports as
    /// <see cref="SettingsLoadStatus.Corrupted"/> because of a value of the wrong type (T-014 decision): the password
    /// must not stay on disk just because another value is broken. A missing, unreadable or too large file, and one
    /// that is not syntactically valid JSON (including invalid UTF-8), is left alone. Duplicate properties are all
    /// cleaned (see the implementation remarks).
    /// </para>
    /// </summary>
    /// <returns>
    /// Ids of the printers whose camera address was changed (empty when nothing changed). Entries without an id are
    /// cleaned as well but cannot be listed.
    /// </returns>
    /// <remarks>The default implementation is for stores without a file (e.g. in memory): there is nothing to clean.</remarks>
    /// <exception cref="IOException">The cleaned settings could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<string>> RemoveCameraCredentialsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}
