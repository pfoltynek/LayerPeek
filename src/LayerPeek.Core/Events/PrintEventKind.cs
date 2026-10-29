namespace LayerPeek.Core.Events;

/// <summary>
/// A change of a printer worth telling the user about, derived by <see cref="PrintEventDetector"/> from consecutive snapshots.
/// </summary>
public enum PrintEventKind
{
    /// <summary>A new job started printing.</summary>
    PrintStarted = 1,

    /// <summary>The job finished successfully.</summary>
    PrintFinished,

    /// <summary>The job was paused.</summary>
    PrintPaused,

    /// <summary>The job continues after a pause or after the printer waited for the user.</summary>
    PrintResumed,

    /// <summary>The job was stopped (cancelled) before completion.</summary>
    PrintStopped,

    /// <summary>The printer reports an error.</summary>
    Error,

    /// <summary>The printer waits for the user (e.g. filament runout).</summary>
    AttentionRequired,

    /// <summary>The printer has been unreachable for a while (see <see cref="PrintEventDetectorOptions.OfflineDelay"/>).</summary>
    PrinterOffline,

    /// <summary>The printer is reachable again after <see cref="PrinterOffline"/>.</summary>
    PrinterOnline,

    /// <summary>The estimated remaining time of the job dropped to the threshold (once per job).</summary>
    RemainingTime,

    /// <summary>The printer rejected the configured credentials (once, again only after it accepted them in between).</summary>
    CredentialsRejected,

    /// <summary>
    /// The nozzle and the bed cooled down to the printer's thresholds after a finished or stopped print (T-080, T-109). Not detected by
    /// <see cref="PrintEventDetector"/> but raised by <see cref="Cooldown.CooldownMonitor"/>, at most once per print.
    /// </summary>
    CooledDown,
}
