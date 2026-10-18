namespace PrusaTrayDiagnostic.Core.Printers;

/// <summary>
/// A configured printer. Contains no secrets; credentials live in <see cref="Secrets.ISecretStore"/>.
/// Text values are trimmed and must not be empty.
/// The PrusaLink user name is not a secret (unlike the password) and is stored with the rest of the definition.
/// </summary>
public sealed record PrinterDefinition
{
    /// <summary>Stable identifier, unique (case-insensitive) among configured printers.</summary>
    public required string Id
    {
        get;
        init => field = RequireText(value);
    }

    /// <summary>Display name.</summary>
    public required string Name
    {
        get;
        init => field = RequireText(value);
    }

    public required ConnectionType ConnectionType
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown connection type.");
            }

            field = value;
        }
    }

    /// <summary>
    /// Where to reach the printer: host name / IP / URL for <see cref="ConnectionType.PrusaLink"/>,
    /// printer identifier for <see cref="ConnectionType.PrusaConnect"/>.
    /// </summary>
    public required string Address
    {
        get;
        init => field = RequireText(value);
    }

    /// <summary>
    /// User name for logging in to <see cref="ConnectionType.PrusaLink"/>; not a secret. Defaults to
    /// <see cref="PrinterUserName.Default"/>. Trimmed and validated by <see cref="PrinterUserName.Validate"/>;
    /// unused for <see cref="ConnectionType.PrusaConnect"/>.
    /// </summary>
    public string UserName
    {
        get;
        init
        {
            PrinterUserNameError error = PrinterUserName.Validate(value);
            if (error != PrinterUserNameError.None)
            {
                // The value itself is not echoed: it may contain control characters.
                throw new ArgumentException($"Invalid user name ({error}).", nameof(value));
            }

            field = value.Trim();
        }
    } = PrinterUserName.Default;

    private static string RequireText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}
