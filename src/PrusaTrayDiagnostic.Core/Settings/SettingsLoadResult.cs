namespace PrusaTrayDiagnostic.Core.Settings;

public enum SettingsLoadStatus
{
    /// <summary>Settings were loaded (possibly with <see cref="SettingsLoadResult.Warnings"/>).</summary>
    Loaded,

    /// <summary>No settings file exists yet (first run); defaults are used.</summary>
    NotFound,

    /// <summary>The settings file is not valid; defaults are used and the file is left untouched.</summary>
    Corrupted,

    /// <summary>The settings file could not be read (locked, access denied...); defaults are used.</summary>
    Unreadable,
}

/// <summary>
/// Outcome of <see cref="ISettingsStore.LoadAsync"/>. Messages are technical English texts intended for logs
/// and for the UI to surface to the user.
/// </summary>
public sealed record SettingsLoadResult
{
    public required AppSettings Settings { get; init; }

    public required SettingsLoadStatus Status { get; init; }

    /// <summary>Why the stored settings were not used; set for <see cref="SettingsLoadStatus.Corrupted"/> and <see cref="SettingsLoadStatus.Unreadable"/>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Invalid parts of the file that were skipped or corrected while loading.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Ids (trimmed) of every printer entry in the file, also of entries that were skipped as invalid (unknown connection
    /// type, missing address, duplicate id...): data kept for such a printer (e.g. its custom image, T-039) must survive
    /// until the user fixes or removes the entry. Empty when the file was not <see cref="SettingsLoadStatus.Loaded"/>.
    /// </summary>
    public IReadOnlyList<string> StoredPrinterIds { get; init; } = [];
}
