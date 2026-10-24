namespace LayerPeek.Tests.Shared;

/// <summary>
/// Cross-process lock that serializes concurrent runs of one test assembly (e.g. IDE test explorer and CLI),
/// so that the cleanup sweep of one run never deletes data of another running one.
/// The lock file is deleted when the lock is released or the process dies.
/// Locks of folders reserved per worktree come from <see cref="ReservedTempDirectory.AcquireLockAsync"/>; the
/// name-based <see cref="AcquireAsync"/> is global for the whole machine (all worktrees).
/// </summary>
internal sealed class TestRunLock : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly FileStream _file;

    private TestRunLock(FileStream file) => _file = file;

    /// <summary>Path of the lock file.</summary>
    public string Path => _file.Name;

    /// <summary>Machine-wide lock <c>%TEMP%/&lt;name&gt;.lock</c>, shared by all worktrees.</summary>
    public static Task<TestRunLock> AcquireAsync(string name, TimeSpan timeout) =>
        AcquireFileAsync(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{name}.lock"), timeout);

    /// <summary>Waits up to <paramref name="timeout"/> for the lock file <paramref name="path"/>.</summary>
    public static async Task<TestRunLock> AcquireFileAsync(string path, TimeSpan timeout)
    {
        DateTime start = DateTime.UtcNow;
        DateTime deadline = start + timeout;
        while (true)
        {
            try
            {
                return Open(path);
            }
            // UnauthorizedAccessException: the previous holder's file can be briefly delete-pending on Windows.
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(RetryDelay);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                string waited = (DateTime.UtcNow - start).TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                throw new TimeoutException(
                    $"Test run lock '{path}' not acquired after waiting {waited} s: another test run of the same assembly "
                    + "(IDE test explorer or 'dotnet test') still holds it. Let it finish or stop it, then run the tests again.",
                    ex);
            }
        }
    }

    /// <summary>The lock file <paramref name="path"/> if nobody holds it right now, otherwise <see langword="null"/>.</summary>
    public static TestRunLock? TryAcquire(string path)
    {
        try
        {
            return Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Runs <paramref name="action"/>, retrying transient failures (e.g. a file briefly locked by antivirus).</summary>
    public static async Task RetryAsync(string description, Action action)
    {
        const int attempts = 5;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < attempts && ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                await Task.Delay(RetryDelay * attempt);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Test cleanup failed: {description}. Leftovers will be removed by the next test run.", ex);
            }
        }
    }

    public void Dispose() => _file.Dispose();

    private static TestRunLock Open(string path) =>
        new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
}
