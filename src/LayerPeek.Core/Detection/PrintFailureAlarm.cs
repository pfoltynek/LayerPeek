namespace LayerPeek.Core.Detection;

/// <summary>
/// A confirmed print failure (T-153): what the notification (T-154: text, snapshot in the toast, a click opens the camera
/// window, "False alarm" right in the toast) and the false alarm record need. Contains no secrets.
/// </summary>
public sealed record PrintFailureAlarm
{
    /// <summary>Unique per alarm (e.g. to match the "False alarm" action of a notification).</summary>
    public required string Id { get; init; }

    /// <summary>The printer (id as configured).</summary>
    public required string PrinterId { get; init; }

    /// <summary>Its name when the alarm was raised.</summary>
    public required string PrinterName { get; init; }

    /// <summary>The kind of failure: <see cref="PrintFailureDetectorRegistration.Id"/> (e.g. <c>bed-change</c>).</summary>
    public required string DetectorId { get; init; }

    /// <summary>When the sample that confirmed it was taken.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>The score of that sample (0–1).</summary>
    public required float Score { get; init; }

    /// <summary>The score threshold of <see cref="Sensitivity"/>.</summary>
    public required float Threshold { get; init; }

    /// <summary>The detector's sensitivity (0–100) at the time.</summary>
    public required int Sensitivity { get; init; }

    /// <summary>Job, Z, progress, time printing and region of the sample.</summary>
    public required PrintFailureContext Context { get; init; }

    /// <summary>File name of the job; <see langword="null"/> when unknown.</summary>
    public string? FileName { get; init; }

    /// <summary>The scores of the samples within the confirmation window, oldest first (for the false alarm record).</summary>
    public IReadOnlyList<float> RecentScores { get; init; } = [];

    /// <summary>
    /// The saved snapshot (JPEG in the printer's recording folder, <see cref="PrintFailureDetectionSettings.SaveSnapshots"/>);
    /// <see langword="null"/> when saving is off or failed. The rotation of the recordings may delete it later.
    /// </summary>
    public string? SnapshotPath { get; init; }

    /// <summary>
    /// The snapshot as JPEG in memory (also when it was not saved), for the false alarm record; empty when the camera gave no
    /// frame in time. About 0.1–0.3 MB at 1280×720.
    /// </summary>
    public ReadOnlyMemory<byte> SnapshotJpeg { get; init; }
}

/// <summary>What the notification of a print failure shows (T-153, texts and actions in T-154).</summary>
/// <param name="Alarm">The alarm.</param>
/// <param name="PlaySound">Whether it plays a sound: decided like a printer error (user decision 2026-11-04).</param>
public sealed record PrintFailureNotification(PrintFailureAlarm Alarm, bool PlaySound);
