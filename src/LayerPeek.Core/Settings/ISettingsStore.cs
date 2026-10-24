using LayerPeek.Core.Printers;

namespace LayerPeek.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. A file store retries for a short while when another process holds the
/// file open (antivirus, backup, an editor); "unreadable" below means a file that still cannot be read after that.
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
    /// <exception cref="IOException">
    /// The cleaned settings could not be written, or another process still held the file after the retries.
    /// </exception>
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
    /// <exception cref="IOException">
    /// The settings could not be written, or another process still held the file after the retries (the model is not saved).
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> SetDetectedModelAsync(string printerId, PrinterModelCode code, string expectedAddress, ConnectionType expectedConnectionType, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>
    /// Sets the device path of the camera window monitor of printer <paramref name="printerId"/> (case-insensitive) to
    /// <paramref name="devicePath"/> while the stored monitor still is <paramref name="expected"/> (user decision 2026-10-23: a
    /// monitor found on another connector keeps its new path; see <see cref="Cameras.CameraWindowDefaultPlacement.Relocate"/>).
    /// Like <see cref="SetDetectedModelAsync"/> only this value changes, byte for byte (comments, formatting and unknown
    /// values stay), and only in a file that loads without warnings; a missing or invalid file, another printer or another
    /// monitor is left alone. The write is atomic.
    /// </summary>
    /// <returns><see langword="true"/> when the file was written.</returns>
    /// <remarks>The default implementation is for stores without a file (e.g. in memory): nothing is written.</remarks>
    /// <exception cref="IOException">
    /// The settings could not be read or written, also when another process still held the file after the retries.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ArgumentException"><paramref name="devicePath"/> is not a valid device path or is a known twin of <paramref name="expected"/>.</exception>
    Task<bool> SetCameraMonitorDevicePathAsync(string printerId, Cameras.CameraMonitorId expected, string devicePath, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <summary>
    /// Sets the camera overlay layout of printer <paramref name="printerId"/> (<c>printers[*].camera.overlay</c>, T-077) in
    /// the stored settings and changes <b>nothing else</b>, byte for byte like <see cref="SetDetectedModelAsync"/>: the layout
    /// edited in the camera window is saved while the settings window may hold other unsaved changes. Every entry with that
    /// id (trimmed, case-insensitive) that has a camera object gets the layout (the default layout as <c>{}</c>); the write
    /// is atomic. A missing, unreadable or too large file, one that is not syntactically valid JSON and one without such a
    /// printer camera is left alone. Use <see cref="CameraOverlayLayoutWriter"/> to announce the write.
    /// </summary>
    /// <returns><see langword="true"/> when the file was written (the stored section differed).</returns>
    /// <remarks>The default implementation is for stores without a file (e.g. in memory): nothing is written.</remarks>
    /// <exception cref="IOException">
    /// The settings could not be read or written, also when another process still held the file after the retries.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">Access to the settings location was denied.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<bool> SetCameraOverlayLayoutAsync(string printerId, Cameras.Overlay.CameraOverlayLayout layout, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
