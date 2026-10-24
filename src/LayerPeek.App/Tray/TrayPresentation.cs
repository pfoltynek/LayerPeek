namespace LayerPeek.App.Tray;

internal enum TrayMessage
{
    /// <summary>The tooltip describes the only printer (<see cref="TrayPresentationKey.Printer"/>).</summary>
    Printer,

    /// <summary>
    /// Several printers: one line per printer (<see cref="TrayToolTipFormatter.FormatSummary"/>); what they show is
    /// identified by <see cref="TrayPresentationKey.SummaryVersion"/>.
    /// </summary>
    Summary,
    Starting,
    NotConfigured,
    MonitoringFailed,
}

/// <summary>
/// Cheap value identity of what the tray shows. Built on every poll; the tooltip text is formatted only when
/// the key changes.
/// </summary>
/// <param name="Printer">Content of the only printer; <see langword="default"/> unless <see cref="TrayMessage.Printer"/>.</param>
/// <param name="SummaryVersion">
/// Changes whenever a line of the multi-printer tooltip changes (kept by the owner, which compares the content of the
/// one printer a snapshot belongs to, so that a poll costs O(1) instead of comparing every line); 0 unless
/// <see cref="TrayMessage.Summary"/>.
/// </param>
internal readonly record struct TrayPresentationKey(TrayIconKey Icon, TrayMessage Message, ToolTipContent Printer, long SummaryVersion)
{
    private static readonly TrayIconKey NoDataIcon = TrayIconKey.For(TrayStatus.Offline, null);

    /// <param name="printers">Configured printers in configuration order; <see langword="null"/> while the settings are loading.</param>
    /// <param name="monitoringFailed">The monitoring service stopped unexpectedly.</param>
    /// <param name="summaryVersion">Version of the per-printer lines; used only for more than one printer.</param>
    public static TrayPresentationKey Create(IReadOnlyList<TrayPrinter>? printers, bool monitoringFailed, long summaryVersion = 0)
    {
        if (monitoringFailed)
        {
            return new(TrayIconKey.For(TrayStatus.Error, null), TrayMessage.MonitoringFailed, default, 0);
        }

        if (printers is null)
        {
            return new(NoDataIcon, TrayMessage.Starting, default, 0);
        }

        // One icon for all printers: the most severe printer wins, the first configured one on a tie (T-013).
        int primaryIndex = TrayStatusRules.SelectPrimary(printers);
        if (primaryIndex < 0)
        {
            return new(NoDataIcon, TrayMessage.NotConfigured, default, 0);
        }

        TrayPrinter primary = printers[primaryIndex];
        TrayIconKey icon = TrayIconKey.For(primary.Status, primary.Snapshot?.Job?.ProgressPercent);
        return printers.Count == 1
            ? new(icon, TrayMessage.Printer, ToolTipContent.From(primary, otherPrinterCount: 0), 0)
            : new(icon, TrayMessage.Summary, default, summaryVersion);
    }
}

/// <summary>Everything the tray shows.</summary>
internal sealed record TrayPresentation(TrayIconKey IconKey, string ToolTipText)
{
    /// <summary>Formats the tooltip in the current UI culture.</summary>
    /// <param name="summary">Content of every printer in configuration order; required for <see cref="TrayMessage.Summary"/>.</param>
    public static TrayPresentation Format(TrayPresentationKey key, IReadOnlyList<ToolTipContent>? summary = null) => new(key.Icon, key.Message switch
    {
        TrayMessage.Starting => TrayToolTipFormatter.Starting(),
        TrayMessage.NotConfigured => TrayToolTipFormatter.NotConfigured(),
        TrayMessage.MonitoringFailed => TrayToolTipFormatter.MonitoringFailed(),
        TrayMessage.Summary => TrayToolTipFormatter.FormatSummary(summary ?? throw new ArgumentNullException(nameof(summary))),
        _ => TrayToolTipFormatter.Format(key.Printer),
    });
}
