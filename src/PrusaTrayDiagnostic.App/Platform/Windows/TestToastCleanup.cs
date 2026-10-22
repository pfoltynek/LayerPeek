#if DEBUG
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using PrusaTrayDiagnostic.App.Composition;
using PrusaTrayDiagnostic.App.Notifications;

namespace PrusaTrayDiagnostic.App.Platform.Windows;

/// <summary>
/// Debug builds only (T-067): removes what the toasts of the test mode leave in Windows, the toasts of the test app
/// <see cref="AppEnvironment.TestAppUserModelId"/> in the notification center and its Start menu shortcut
/// <see cref="ShortcutFileName"/>. Runs when a test app with toasts quits, and as the command line mode
/// <see cref="ClearToastsArgument"/> after a crashed test run. Never touches the production app id or shortcut: both are
/// fixed here, and a shortcut path with another file name is refused.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class TestToastCleanup
{
    /// <summary>The command line mode of this cleanup (<see cref="AppEnvironment.ClearToastsArgument"/>).</summary>
    public const string ClearToastsArgument = AppEnvironment.ClearToastsArgument;

    /// <summary>The Start menu shortcut of the test app (<see cref="AppEnvironment.TestShortcutFileName"/>).</summary>
    public const string ShortcutFileName = AppEnvironment.TestShortcutFileName;

    /// <summary>Exit code of the command line mode when something could not be removed.</summary>
    public const int FailedExitCode = 1;

    private readonly IToastHistory _history;
    private readonly IShortcutStore _shortcuts;
    private readonly string _shortcutPath;
    private readonly ILogger _logger;

    /// <param name="shortcutPath">The test shortcut; its file name must be <see cref="ShortcutFileName"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="shortcutPath"/> is not a path of the test shortcut.</exception>
    public TestToastCleanup(IToastHistory history, IShortcutStore shortcuts, string shortcutPath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        ArgumentNullException.ThrowIfNull(logger);
        if (!string.Equals(Path.GetFileName(shortcutPath), ShortcutFileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Only the test shortcut '{ShortcutFileName}' may be removed.", nameof(shortcutPath));
        }

        _history = history;
        _shortcuts = shortcuts;
        _shortcutPath = shortcutPath;
        _logger = logger;
    }

    /// <summary>The test shortcut in the per-user Start menu.</summary>
    public static string DefaultShortcutPath() => AppShortcutRegistration.ShortcutPathFor(ShortcutFileName);

    /// <summary>Where the test shortcut is removed (composition tests).</summary>
    internal string ShortcutPath => _shortcutPath;

    /// <summary>
    /// Clears the notification center history of the test app and deletes the test shortcut; the second step runs even
    /// when the first one failed. Never throws.
    /// </summary>
    /// <returns>What failed; empty when everything is gone.</returns>
    public IReadOnlyList<string> Run()
    {
        List<string> failures = [];
        try
        {
            _history.Clear(AppEnvironment.TestAppUserModelId);
            LogHistoryCleared(AppEnvironment.TestAppUserModelId);
        }
        catch (Exception ex)
        {
            failures.Add($"Clearing the toasts of {AppEnvironment.TestAppUserModelId} failed: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            LogHistoryFailed(ex, AppEnvironment.TestAppUserModelId);
        }

        try
        {
            if (_shortcuts.Delete(_shortcutPath))
            {
                LogShortcutDeleted();
            }
        }
        catch (Exception ex)
        {
            failures.Add($"Deleting '{_shortcutPath}' failed: {ex.GetType().Name} {ex.Message}");
            LogShortcutFailed(ex);
        }

        return failures;
    }

    /// <summary>The command line mode <see cref="ClearToastsArgument"/>: the real history and Start menu.</summary>
    /// <returns>0, or <see cref="FailedExitCode"/> after writing what failed to <paramref name="error"/>.</returns>
    public static int RunFromCommandLine(TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        IReadOnlyList<string> failures;
        try
        {
            failures = new TestToastCleanup(new WinRtToastHistory(), new WindowsShortcutStore(), DefaultShortcutPath(), NullLogger.Instance).Run();
        }
        catch (Exception ex)
        {
            failures = [$"{ex.GetType().Name}: {ex.Message}"];
        }

        foreach (string failure in failures)
        {
            error.WriteLine(failure);
        }

        error.Flush();
        return failures.Count == 0 ? 0 : FailedExitCode;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Test mode: the toasts of {AppUserModelId} were removed from the notification center.")]
    private partial void LogHistoryCleared(string appUserModelId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Test mode: removing the toasts of {AppUserModelId} from the notification center failed.")]
    private partial void LogHistoryFailed(Exception exception, string appUserModelId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Test mode: the Start menu shortcut of the test app was deleted.")]
    private partial void LogShortcutDeleted();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Test mode: deleting the Start menu shortcut of the test app failed.")]
    private partial void LogShortcutFailed(Exception exception);
}
#endif
