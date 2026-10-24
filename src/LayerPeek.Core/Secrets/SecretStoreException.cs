namespace LayerPeek.Core.Secrets;

/// <summary>
/// The OS credential store backing <see cref="ISecretStore"/> failed. The message never contains the secret.
/// </summary>
public sealed class SecretStoreException : Exception
{
    public SecretStoreException()
    {
    }

    public SecretStoreException(string message)
        : base(message)
    {
    }

    public SecretStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
