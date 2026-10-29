#if DEBUG
using System.Globalization;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using LayerPeek.App.Composition;
using LayerPeek.App.Notifications;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// Debug builds only (T-020): removes the notifications the test mode left in the notification center of macOS, those of the
/// test bundle <see cref="AppEnvironment.TestBundleIdentifier"/> only. Runs when a test app with notifications quits, and as
/// the command line mode <see cref="AppEnvironment.ClearToastsArgument"/> of the test bundle after a crashed test run.
/// Never touches the production bundle: a center of another bundle is refused.
/// </summary>
internal sealed partial class TestNotificationCleanup
{
    /// <summary>Exit code of the command line modes when something failed or the bundle is not the test bundle.</summary>
    public const int FailedExitCode = 1;

    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(200);

    private readonly IUserNotificationCenter _center;
    private readonly ILogger _logger;

    /// <exception cref="ArgumentException">The center is not that of the test bundle.</exception>
    public TestNotificationCleanup(IUserNotificationCenter center, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(logger);
        if (!string.Equals(center.BundleIdentifier, AppEnvironment.TestBundleIdentifier, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Only the notifications of the test bundle '{AppEnvironment.TestBundleIdentifier}' may be removed.", nameof(center));
        }

        _center = center;
        _logger = logger;
    }

    /// <summary>
    /// Removes the delivered and pending notifications of the test bundle and checks that none is left; removing is
    /// asynchronous in macOS (a notification just posted may arrive after it), so it is repeated until the notification center
    /// is empty or <paramref name="timeout"/> passes. Never throws.
    /// </summary>
    /// <returns>What failed; empty when no notification is left.</returns>
    public async Task<IReadOnlyList<string>> RunAsync(TimeSpan timeout, TimeProvider? time = null)
    {
        TimeProvider clock = time ?? TimeProvider.System;
        long started = clock.GetTimestamp();
        string last;
        while (true)
        {
            try
            {
                _center.RemoveAll();
                int left = await _center.CountDeliveredAsync(CancellationToken.None).ConfigureAwait(false);
                if (left == 0)
                {
                    LogCleared(_center.BundleIdentifier);
                    return [];
                }

                last = $"{left} notification(s) of {_center.BundleIdentifier} are still in the notification center.";
            }
            catch (Exception ex)
            {
                last = $"Removing the notifications of {_center.BundleIdentifier} failed: {ex.GetType().Name} {ex.Message}";
            }

            if (clock.GetElapsedTime(started) >= timeout)
            {
                LogFailed(_center.BundleIdentifier, last);
                return [last];
            }

            await Task.Delay(Retry, clock).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The command line mode <see cref="AppEnvironment.ClearToastsArgument"/> on macOS: only as the executable of the test bundle.
    /// </summary>
    /// <returns>0, or <see cref="FailedExitCode"/> after writing what failed to <paramref name="error"/>.</returns>
    [SupportedOSPlatform("macos")]
    public static int RunFromCommandLine(TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);
        IReadOnlyList<string> failures;
        if (OpenTestCenter(out string? reason) is not { } center)
        {
            failures = [$"Notifications of the test mode are removed only in the test bundle: {reason}."];
        }
        else
        {
            // Program.Main before anything else: no synchronization context, so waiting here cannot deadlock (as SingleInstanceGuard).
            failures = new TestNotificationCleanup(center, NullLogger.Instance).RunAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }

        foreach (string failure in failures)
        {
            error.WriteLine(failure);
        }

        error.Flush();
        return failures.Count == 0 ? 0 : FailedExitCode;
    }

    /// <summary>
    /// The command line mode <see cref="AppEnvironment.NotificationStatusArgument"/>: the permission and the number of delivered
    /// notifications of the test bundle, as <c>permission=&lt;NotificationPermission&gt; delivered=&lt;n&gt;</c> on one line.
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static int StatusFromCommandLine(TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (OpenTestCenter(out string? reason) is not { } center)
        {
            error.WriteLine($"The notification status is read only in the test bundle: {reason}.");
            error.Flush();
            return FailedExitCode;
        }

        try
        {
            NotificationPermission permission = center.GetPermissionAsync(CancellationToken.None).GetAwaiter().GetResult();
            int delivered = center.CountDeliveredAsync(CancellationToken.None).GetAwaiter().GetResult();
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"permission={permission} delivered={delivered}"));
            output.Flush();
            return 0;
        }
        catch (Exception ex)
        {
            error.WriteLine($"Reading the notification status failed: {ex.GetType().Name} {ex.Message}");
            error.Flush();
            return FailedExitCode;
        }
    }

    /// <summary>The center of the test bundle without a delegate (nothing is asked, nothing shown), or <see langword="null"/>.</summary>
    [SupportedOSPlatform("macos")]
    private static MacUserNotificationCenter? OpenTestCenter(out string? reason) =>
        MacUserNotificationCenter.TryOpen(AppEnvironment.TestBundleIdentifier, MacMainBundle.Identifier, NullLogger.Instance, out reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Test mode: the notifications of {BundleIdentifier} were removed from the notification center.")]
    private partial void LogCleared(string bundleIdentifier);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Test mode: removing the notifications of {BundleIdentifier} did not finish: {Failure}")]
    private partial void LogFailed(string bundleIdentifier, string failure);
}
#endif
