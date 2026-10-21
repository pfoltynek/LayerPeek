using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>One tray icon remembered by Explorer (Windows 11 22H2+).</summary>
/// <param name="KeyName">Name of its key, a number chosen by Explorer.</param>
/// <param name="ExecutablePath">Full path of the owning executable (known folder prefix expanded), or <see langword="null"/>.</param>
/// <param name="HasPromotion">
/// <c>IsPromoted</c> exists: the user (or this app) already decided whether the icon is shown on the taskbar or in the
/// overflow.
/// </param>
internal sealed record NotifyIconEntry(string KeyName, string? ExecutablePath, bool HasPromotion);

/// <summary>Explorer's per-icon settings of the notification area.</summary>
internal interface INotifyIconSettings
{
    /// <exception cref="Exception">The settings cannot be read.</exception>
    IReadOnlyList<NotifyIconEntry> ReadEntries();

    /// <summary>Shows the icon on the taskbar (<c>IsPromoted = 1</c>); an entry that no longer exists is left alone.</summary>
    void Promote(string keyName);

    /// <summary>Deletes <c>IsPromoted</c> of the entry, keeping the entry; a missing entry or value is ignored.</summary>
    void ClearPromotion(string keyName);
}

internal enum TrayIconPromotionResult
{
    /// <summary>At least one entry of the executable was promoted.</summary>
    Promoted,

    /// <summary>Every entry of the executable already has <c>IsPromoted</c>; the user's choice is kept.</summary>
    LeftToUser,

    /// <summary>Explorer created no entry of the executable within the wait limit.</summary>
    NotFound,

    Cancelled,

    /// <summary>The registry could not be read or written (logged).</summary>
    Failed,
}

/// <summary>
/// Workaround (T-061): Windows 11 puts new tray icons into the overflow and has no API to change that, so the undocumented
/// <c>HKCU\Control Panel\NotifyIconSettings\&lt;n&gt;\IsPromoted</c> is set once the icon is shown. Only entries without the
/// value are promoted, so an icon the user moved back into the overflow (<c>IsPromoted = 0</c>) stays there. Explorer
/// creates the entry only after the icon appeared, hence the polling; it applies the change immediately (verified on
/// Windows 11 in T-061).
/// </summary>
internal sealed partial class TrayIconPromotion
{
    /// <summary>File name of the app; other hosts (<c>dotnet.exe</c>) share their entry with other apps.</summary>
    public const string ExecutableName = "PrusaTrayDiagnostic.exe";

    /// <summary>Explorer wrote the entry within a few seconds in a test (T-061); a busy start may take longer.</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Reads of the settings before giving up: about 30 seconds with <see cref="DefaultPollInterval"/>.</summary>
    public const int DefaultAttempts = 30;

    private readonly INotifyIconSettings _settings;
    private readonly ILogger<TrayIconPromotion> _logger;
    private readonly Func<CancellationToken, Task> _wait;
    private readonly int _attempts;

    /// <param name="wait">The pause between two reads; <see cref="DefaultPollInterval"/> when <see langword="null"/>.</param>
    /// <param name="attempts">How many times the settings are read before giving up.</param>
    public TrayIconPromotion(
        INotifyIconSettings settings,
        ILogger<TrayIconPromotion> logger,
        Func<CancellationToken, Task>? wait = null,
        int attempts = DefaultAttempts)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        _settings = settings;
        _logger = logger;
        _wait = wait ?? (static cancellationToken => Task.Delay(DefaultPollInterval, cancellationToken));
        _attempts = attempts;
    }

    /// <summary>Whether <paramref name="processPath"/> is the app's own executable (not a host such as <c>dotnet.exe</c>).</summary>
    public static bool AppliesTo(string? processPath) =>
        !string.IsNullOrEmpty(processPath)
        && string.Equals(Path.GetFileName(processPath), ExecutableName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Waits for Explorer's entries of <paramref name="executablePath"/> and promotes those the user has not decided on.
    /// Never throws for registry failures or cancellation.
    /// </summary>
    public async Task<TrayIconPromotionResult> PromoteAsync(string executablePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                List<NotifyIconEntry> own = OwnEntries(executablePath);
                if (own.Count > 0)
                {
                    return PromoteUndecided(own);
                }

                if (attempt >= _attempts)
                {
                    LogNotFound(attempt);
                    return TrayIconPromotionResult.NotFound;
                }

                await _wait(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return TrayIconPromotionResult.Cancelled;
        }
        catch (Exception ex)
        {
            LogFailed(ex);
            return TrayIconPromotionResult.Failed;
        }
    }

    /// <summary>
    /// Uninstall (hook <c>--veloapp-uninstall</c>, T-017): deletes <c>IsPromoted</c> from Explorer's entries of
    /// <paramref name="executablePath"/>, so a reinstall to the same path is promoted again. The entries themselves stay:
    /// Explorer keeps them in memory and, once deleted, did not create them again while it kept running (tested in
    /// T-061), which would leave a reinstall in the overflow. Never throws; returns how many values were deleted.
    /// </summary>
    public int ClearPromotions(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        List<NotifyIconEntry> own;
        try
        {
            own = OwnEntries(executablePath);
        }
        catch (Exception ex)
        {
            LogFailed(ex);
            return 0;
        }

        int cleared = 0;
        foreach (NotifyIconEntry entry in own)
        {
            if (!entry.HasPromotion)
            {
                continue;
            }

            try
            {
                _settings.ClearPromotion(entry.KeyName);
                cleared++;
            }
            catch (Exception ex)
            {
                LogClearFailed(ex);
            }
        }

        return cleared;
    }

    private TrayIconPromotionResult PromoteUndecided(List<NotifyIconEntry> own)
    {
        int promoted = 0;
        int failed = 0;
        foreach (NotifyIconEntry entry in own)
        {
            if (entry.HasPromotion)
            {
                continue;
            }

            try
            {
                _settings.Promote(entry.KeyName);
                promoted++;
            }
            catch (Exception ex)
            {
                LogFailed(ex);
                failed++;
            }
        }

        if (promoted == 0)
        {
            if (failed > 0)
            {
                return TrayIconPromotionResult.Failed;
            }

            LogLeftToUser();
            return TrayIconPromotionResult.LeftToUser;
        }

        LogPromoted(promoted);
        return TrayIconPromotionResult.Promoted;
    }

    private List<NotifyIconEntry> OwnEntries(string executablePath) =>
        [.. _settings.ReadEntries().Where(e => SamePath(e.ExecutablePath, executablePath))];

    private static bool SamePath(string? a, string b)
    {
        if (string.IsNullOrWhiteSpace(a))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tray icon shown on the taskbar ({Count} notification area entries promoted).")]
    private partial void LogPromoted(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The user already chose where the tray icon is shown; left as it is.")]
    private partial void LogLeftToUser();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Explorer created no notification area entry for the app ({Attempts} reads).")]
    private partial void LogNotFound(int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The notification area settings of Explorer could not be read or written.")]
    private partial void LogFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The taskbar choice of a notification area entry of the app could not be deleted.")]
    private partial void LogClearFailed(Exception exception);
}
