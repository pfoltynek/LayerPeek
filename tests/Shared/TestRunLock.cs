namespace PrusaTrayDiagnostic.Tests.Shared;

/// <summary>
/// Cross-process lock that serializes concurrent runs of one test assembly (e.g. IDE test explorer and CLI),
/// so that the cleanup sweep of one run never deletes data of another running one.
/// The lock file is deleted when the lock is released or the process dies.
/// </summary>
internal sealed class TestRunLock : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly FileStream _file;

    private TestRunLock(FileStream file) => _file = file;

    public static async Task<TestRunLock> AcquireAsync(string name, TimeSpan timeout)
    {
        string path = Path.Combine(Path.GetTempPath(), $"{name}.lock");
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                return new TestRunLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose));
            }
            // UnauthorizedAccessException: the previous holder's file can be briefly delete-pending on Windows.
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(RetryDelay);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new TimeoutException($"Another test run still holds '{path}' after {timeout}.", ex);
            }
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
}
