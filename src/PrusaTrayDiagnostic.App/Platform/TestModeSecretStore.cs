#if DEBUG
using System.Collections.Concurrent;

using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.App.Platform;

/// <summary>
/// Debug builds only: the secret store of the isolated test mode (T-044). Secrets live in memory for one run, so a test
/// never reads or writes the user's Windows Credential Manager. A test can hand passwords in with the file
/// <see cref="SeedFileName"/> in the test data folder: one <c>key</c>, a tab and the secret per line (read once, never
/// written).
/// </summary>
internal sealed class TestModeSecretStore : ISecretStore
{
    public const string SeedFileName = "test-secrets.txt";

    /// <summary>The limit of the Windows Credential Manager (5 × 512 bytes), so the settings window validates like on Windows.</summary>
    public const int MaxSecretBytes = 5 * 512;

    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="dataDirectory">The test data folder; a missing seed file means no secrets.</param>
    public TestModeSecretStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string seed = Path.Combine(dataDirectory, SeedFileName);
        if (!File.Exists(seed))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(seed))
        {
            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab > 0)
            {
                _secrets[line[..tab]] = line[(tab + 1)..];
            }
        }
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<string?>(cancellationToken)
            : Task.FromResult(_secrets.TryGetValue(key, out string? secret) ? secret : null);
    }

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        _secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<bool>(cancellationToken)
            : Task.FromResult(_secrets.TryRemove(key, out _));
    }
}
#endif
