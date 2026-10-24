namespace LayerPeek.Core.Secrets;

/// <summary>
/// Secure per-user storage for passwords, API keys and tokens, backed by the OS credential store.
/// Keys are case-insensitive (some backends ignore case); use <see cref="SecretKeys"/> to build them.
/// </summary>
public interface ISecretStore
{
    /// <returns>The secret, or <see langword="null"/> when none is stored under <paramref name="key"/>.</returns>
    /// <exception cref="SecretStoreException">The OS credential store failed.</exception>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Creates or replaces the secret stored under <paramref name="key"/>.</summary>
    /// <exception cref="ArgumentException">The secret exceeds the size supported by the backend.</exception>
    /// <exception cref="SecretStoreException">The OS credential store failed.</exception>
    Task SetAsync(string key, string secret, CancellationToken cancellationToken);

    /// <returns><see langword="true"/> when a secret was deleted, <see langword="false"/> when none existed.</returns>
    /// <exception cref="SecretStoreException">The OS credential store failed.</exception>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);
}
