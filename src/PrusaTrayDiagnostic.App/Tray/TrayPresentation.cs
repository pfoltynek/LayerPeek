namespace PrusaTrayDiagnostic.App.Tray;

internal enum TrayMessage
{
    /// <summary>The tooltip describes the primary printer (<see cref="TrayPresentationKey.Printer"/>).</summary>
    Printer,
    Starting,
    NotConfigured,
    MonitoringFailed,
}

/// <summary>
/// Cheap value identity of what the tray shows. Built on every poll; the tooltip text is formatted only when
/// the key changes.
/// </summary>
internal readonly record struct TrayPresentationKey(TrayIconKey Icon, TrayMessage Message, ToolTipContent Printer)
{
    private static readonly TrayIconKey NoDataIcon = TrayIconKey.For(TrayStatus.Offline, null);

    /// <param name="printers">Configured printers in configuration order; <see langword="null"/> while the settings are loading.</param>
    /// <param name="monitoringFailed">The monitoring service stopped unexpectedly.</param>
    public static TrayPresentationKey Create(IReadOnlyList<TrayPrinter>? printers, bool monitoringFailed)
    {
        if (monitoringFailed)
        {
            return new(TrayIconKey.For(TrayStatus.Error, null), TrayMessage.MonitoringFailed, default);
        }

        if (printers is null)
        {
            return new(NoDataIcon, TrayMessage.Starting, default);
        }

        int primaryIndex = TrayStatusRules.SelectPrimary(printers);
        if (primaryIndex < 0)
        {
            return new(NoDataIcon, TrayMessage.NotConfigured, default);
        }

        TrayPrinter primary = printers[primaryIndex];
        TrayIconKey icon = TrayIconKey.For(primary.Status, primary.Snapshot?.Job?.ProgressPercent);
        return new(icon, TrayMessage.Printer, ToolTipContent.From(primary, printers.Count - 1));
    }
}

/// <summary>Everything the tray shows.</summary>
internal sealed record TrayPresentation(TrayIconKey IconKey, string ToolTipText)
{
    /// <summary>Formats the tooltip in the current UI culture.</summary>
    public static TrayPresentation Format(TrayPresentationKey key) => new(key.Icon, key.Message switch
    {
        TrayMessage.Starting => TrayToolTipFormatter.Starting(),
        TrayMessage.NotConfigured => TrayToolTipFormatter.NotConfigured(),
        TrayMessage.MonitoringFailed => TrayToolTipFormatter.MonitoringFailed(),
        _ => TrayToolTipFormatter.Format(key.Printer),
    });
}
