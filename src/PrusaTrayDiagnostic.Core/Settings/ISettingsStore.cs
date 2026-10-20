using PrusaTrayDiagnostic.Core.Printers;

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
    /// that is not syntactically valid JSON, is left alone; so is a file with invalid UTF-8 in a string the cleanup has
    /// to read (property names, printer ids and camera addresses). Invalid bytes elsewhere (e.g. in a printer name) are
    /// copied through unchanged. Duplicate properties are all cleaned (see the implementation remarks).
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

    /// <summary>
    /// Sets the detected model of printer <paramref name="printerId"/> (<c>printers[*].detectedModel</c>, T-039) in the stored
    /// settings and changes <b>nothing else</b>, like <see cref="RemoveCameraCredentialsAsync"/>: the model detected by
    /// monitoring is saved in the background, without the user, so entries the loader skipped, unknown values and
    /// formatting must survive. Every entry with that id (trimmed, case-insensitive) gets the code; the write is atomic.
    /// Only entries whose address and connection type are still <paramref name="expectedAddress"/> and
    /// <paramref name="expectedConnectionType"/> (the machine the code was detected on) are changed. A missing, unreadable or
    /// too large file, one that is not syntactically valid JSON and one without such a printer is left alone.
    /// </summary>
    /// <returns><see langword="true"/> when the file was written (the code differed or was missing).</returns>
    /// <remarks>The default implementation is for stores without a file (e.g. in memory): nothing is written.</remarks>
    /// <exception cref="IOException">The settings could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> SetDetectedModelAsync(string printerId, PrinterModelCode code, string expectedAddress, ConnectionType expectedConnectionType, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
