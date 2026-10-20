using Microsoft.Extensions.Logging;

using PrusaTrayDiagnostic.Core.Images;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Printers;

/// <summary>
/// Custom printer images at startup (T-039), in the background: images of printers that no longer exist are deleted
/// (only when the settings were really loaded: after a failed load the defaults have no printers and every image would
/// go; entries the loader skipped keep theirs), then the images in use are read into <see cref="CustomPrinterImages"/>.
/// </summary>
internal static partial class PrinterImageStartup
{
    /// <param name="store">Resolved here; may throw (e.g. no per-user data folder), which is logged.</param>
    /// <param name="status">How the settings were loaded; the orphan sweep runs only for <see cref="SettingsLoadStatus.Loaded"/>.</param>
    /// <param name="storedPrinterIds">
    /// Ids of every printer entry of the file, also of entries the loader skipped (<see cref="SettingsLoadResult.StoredPrinterIds"/>):
    /// their images stay until the user fixes or removes the entry.
    /// </param>
    /// <returns>Never faults; completes when both steps are done.</returns>
    public static async Task RunAsync(
        Func<IPrinterImageStore> store,
        CustomPrinterImages images,
        AppSettings settings,
        SettingsLoadStatus status,
        IReadOnlyList<string> storedPrinterIds,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(storedPrinterIds);
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            IPrinterImageStore imageStore = store();
            if (status == SettingsLoadStatus.Loaded)
            {
                int removed = await imageStore.RemoveOrphansAsync([.. settings.Printers.Select(static printer => printer.Id), .. storedPrinterIds], cancellationToken).ConfigureAwait(false);
                if (removed > 0)
                {
                    LogOrphansRemoved(logger, removed);
                }
            }

            await images.LoadAsync(imageStore, settings.Printers, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Quitting.
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} printer image(s) of printers that are no longer configured.")]
    private static partial void LogOrphansRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom printer images could not be prepared; the printers show their illustrations.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
