namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The OSStatus of a failed Keychain call with the system's description of it (never any item data); the inner exception
/// of the <see cref="Core.Secrets.SecretStoreException"/> of <see cref="KeychainSecretStore"/>.
/// </summary>
public sealed class KeychainException : Exception
{
    public KeychainException()
    {
    }

    public KeychainException(string message)
        : base(message)
    {
    }

    public KeychainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <param name="status">The OSStatus.</param>
    /// <param name="description">From <c>SecCopyErrorMessageString</c>; <see langword="null"/> when the system has none.</param>
    public KeychainException(int status, string? description)
        : base(description is null ? $"OSStatus {status}." : $"{description} (OSStatus {status}).")
    {
        Status = status;
    }

    /// <summary>The OSStatus (e.g. -25308 <c>errSecInteractionNotAllowed</c>); 0 when unknown.</summary>
    public int Status { get; }
}
