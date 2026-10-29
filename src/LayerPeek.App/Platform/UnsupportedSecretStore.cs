using LayerPeek.Core.Secrets;

namespace LayerPeek.App.Platform;

/// <summary>
/// <see cref="ISecretStore"/> for operating systems without an implementation (neither Windows nor macOS).
/// Every operation fails with <see cref="SecretStoreException"/>: printers that need a password report it
/// as a problem, the rest of the app keeps working.
/// </summary>
internal sealed class UnsupportedSecretStore : ISecretStore
{
    private const string Message = "Secure credential storage is not supported on this operating system yet.";

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<string?>(cancellationToken)
            : Task.FromException<string?>(new SecretStoreException(Message));
    }

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(secret);
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : Task.FromException(new SecretStoreException(Message));
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled<bool>(cancellationToken)
            : Task.FromException<bool>(new SecretStoreException(Message));
    }
}
