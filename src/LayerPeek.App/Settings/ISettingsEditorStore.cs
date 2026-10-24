using LayerPeek.Core.Settings;

namespace LayerPeek.App.Settings;

/// <summary>What the settings window starts with.</summary>
/// <param name="Status">Whether the stored settings were used (otherwise <see cref="Settings"/> are the defaults).</param>
/// <param name="Warnings">Invalid parts of the file that were corrected or left out while loading.</param>
/// <param name="PrintersWithPassword">
/// Ids (case-insensitive) of printers with a saved password; <see langword="null"/> when that is unknown (no secret store
/// on this OS, or it failed).
/// </param>
/// <param name="CamerasWithRemovedCredentials">
/// Ids (case-insensitive) of printers whose camera address lost its credentials when the settings were loaded, in this
/// load or at startup (the file was saved without them since, see <c>MonitoringSession</c>); <see langword="null"/> = none.
/// </param>
/// <param name="CustomImages">
/// Stored custom images (PNG) by printer id (case-insensitive) of printers that use one (T-039); <see langword="null"/> = none.
/// </param>
internal sealed record SettingsEditorData(
    AppSettings Settings,
    SettingsLoadStatus Status,
    IReadOnlyList<string> Warnings,
    IReadOnlySet<string>? PrintersWithPassword,
    IReadOnlySet<string>? CamerasWithRemovedCredentials = null,
    IReadOnlyDictionary<string, byte[]>? CustomImages = null);

/// <summary>A change made in the settings window.</summary>
/// <param name="Original">The settings the window started with; printers missing in <paramref name="Updated"/> were removed.</param>
/// <param name="NewPasswords">New passwords by printer id (only printers of <paramref name="Updated"/>); never logged.</param>
/// <param name="NewImages">
/// Custom images imported in the window (PNG) by printer id, for printers of <paramref name="Updated"/> that use a custom image
/// (T-039); stored only after the settings were saved. <see langword="null"/> = none.
/// </param>
internal sealed record SettingsChange(
    AppSettings Original,
    AppSettings Updated,
    IReadOnlyDictionary<string, string> NewPasswords,
    IReadOnlyDictionary<string, byte[]>? NewImages = null);

/// <summary>Outcome of <see cref="ISettingsEditorStore.SaveAsync"/>. Public only because xUnit theory data needs it.</summary>
public enum SettingsSaveStatus
{
    /// <summary>Settings and passwords were saved and applied to monitoring.</summary>
    Saved,

    /// <summary>A password could not be written; nothing was saved.</summary>
    PasswordNotSaved,

    /// <summary>The settings file could not be written; nothing was saved (password changes were undone).</summary>
    SettingsNotSaved,
}

/// <param name="RollbackIncomplete">
/// After a failure, a password written before it could not be restored (the stored password may differ from before).
/// </param>
/// <param name="UndeletedPasswords">Saved, but this many passwords of removed printers could not be deleted.</param>
/// <param name="UnsavedImages">
/// Saved, but the new custom images of these printers (ids, case-insensitive) could not be stored (T-039); the window keeps
/// them so that the next Save tries again. <see langword="null"/> = none.
/// </param>
internal sealed record SettingsSaveResult(SettingsSaveStatus Status, bool RollbackIncomplete = false, int UndeletedPasswords = 0, IReadOnlySet<string>? UnsavedImages = null);

/// <summary>Loads and saves what the settings window edits: <see cref="AppSettings"/> and printer passwords.</summary>
internal interface ISettingsEditorStore
{
    /// <summary>Never fails because of the file or the secret store (see <see cref="SettingsEditorData.Status"/>).</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<SettingsEditorData> LoadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves <paramref name="change"/> so that a failure never leaves settings pointing to a printer whose new password is
    /// missing; on success applies the settings to monitoring. Not cancellable (an interrupted save would need a rollback
    /// anyway). Never throws for storage failures.
    /// </summary>
    Task<SettingsSaveResult> SaveAsync(SettingsChange change);

    /// <summary>
    /// Waits at most <paramref name="timeout"/> for a save in progress (e.g. when the app quits, so that an interrupted save
    /// does not leave an orphaned password). <see langword="true"/> when no save is running any more.
    /// </summary>
    Task<bool> WaitForPendingSaveAsync(TimeSpan timeout);
}

/// <summary>Applies saved settings to the running app (restarts monitoring with them).</summary>
internal interface ISettingsApplier
{
    /// <summary>
    /// Thread-safe and returns at once; ignored once the app is quitting. The task completes when nothing polls with the
    /// previous settings any more (e.g. before passwords of removed printers are deleted); it never faults.
    /// <paramref name="credentialsChanged"/>: a printer password changed, so monitoring restarts even if the settings are equal.
    /// </summary>
    Task ApplyAsync(AppSettings settings, bool credentialsChanged);

    /// <summary>
    /// The settings monitoring runs with, including detected models saved in the background meanwhile (T-039);
    /// <see langword="null"/> when unknown. A save of the settings window takes the latest detected model from here.
    /// </summary>
    AppSettings? CurrentSettings => null;
}
