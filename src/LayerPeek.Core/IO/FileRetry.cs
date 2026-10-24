namespace LayerPeek.Core.IO;

/// <summary>
/// Retries an access to an app file (settings, camera window state, printer images) that failed only because another
/// process holds the file open (antivirus, backup, an editor; user decision 2026-10-22): a few short, growing delays, then
/// the last error is thrown. Any other error is thrown at once. The one implementation for Core and App.
/// </summary>
public static class FileRetry
{
    // Windows: ERROR_SHARING_VIOLATION (32) and ERROR_LOCK_VIOLATION (33) as HRESULTs.
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    // Elsewhere .NET emulates FileShare with an advisory flock() and reports a conflict as EWOULDBLOCK, with the raw errno
    // as HResult (35 on Apple platforms and the BSDs, 11 on Linux).
    private static readonly int WouldBlock = OperatingSystem.IsWindows() ? 0
        : OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsIOS() || OperatingSystem.IsFreeBSD() ? 35
        : 11;

    // About 1.5 s in total: enough for a scanner or a backup to let go, short enough for a save the user waits for.
    private static readonly TimeSpan[] DelayArray =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800),
    ];

    /// <summary>The waits before the retries, in order; one more attempt than delays.</summary>
    public static IReadOnlyList<TimeSpan> Delays => DelayArray;

    /// <summary>
    /// Whether <paramref name="exception"/> means the file is only held by someone else for now. <paramref name="replacing"/>:
    /// replacing or deleting an existing file, which Windows refuses with "access denied" while another process has it open
    /// without delete sharing (typically a scanner); for anything else "access denied" is permanent.
    /// </summary>
    public static bool IsTransient(Exception exception, bool replacing) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => false,
        IOException io => io.HResult is SharingViolation or LockViolation || (WouldBlock != 0 && io.HResult == WouldBlock),
        UnauthorizedAccessException => replacing,
        _ => false,
    };

    /// <summary>
    /// Runs <paramref name="operation"/>, and again after each of <see cref="Delays"/> while it fails with a transient error
    /// (<see cref="IsTransient"/>). The operation must leave nothing behind when it fails.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled, also during a wait.</exception>
    public static async Task<T> RunAsync<T, TState>(
        Func<TState, CancellationToken, Task<T>> operation,
        TState state,
        bool replacing,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(timeProvider);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await operation(state, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < DelayArray.Length && IsTransient(ex, replacing))
            {
                // Wait below, outside the catch block.
            }

            await Task.Delay(DelayArray[attempt], timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="RunAsync{T, TState}"/> for a synchronous file operation such as <see cref="File.Move(string, string, bool)"/>
    /// or <see cref="File.Delete"/>.
    /// </summary>
    public static Task RunAsync<TState>(Action<TState> operation, TState state, bool replacing, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunAsync(
            static (args, _) =>
            {
                args.Operation(args.State);
                return Task.FromResult(true);
            },
            (Operation: operation, State: state),
            replacing,
            timeProvider,
            cancellationToken);
    }
}
