namespace LayerPeek.Core.Secrets;

/// <summary>
/// Builds <see cref="ISecretStore"/> keys so that every component addresses the same secret the same way.
/// </summary>
public static class SecretKeys
{
    /// <summary>Key of the credential (password or API key) used to access the printer.</summary>
    public static string ForPrinter(string printerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        return $"printer/{printerId}";
    }
}
