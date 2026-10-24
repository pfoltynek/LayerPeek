using LayerPeek.Core.Cameras;

namespace LayerPeek.Core.Printers;

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

    /// <summary>Camera of the printer; <see cref="CameraSettings.Disabled"/> when none is configured.</summary>
    public CameraSettings Camera
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = CameraSettings.Disabled;

    /// <summary>
    /// Model code last reported by the printer (PrusaLink <c>/api/version</c>, firmware 6.5+), kept so that the image is
    /// right while the printer is offline; <see langword="null"/> when never detected. Updated through
    /// <see cref="Settings.AppSettings.WithDetectedModel"/>; shown through <see cref="PrinterImage.Resolve"/>.
    /// </summary>
    public PrinterModelCode? DetectedModel { get; init; }

    /// <summary>The user's choice of the printer image (T-039); <see cref="PrinterImageChoice.Automatic"/> by default.</summary>
    public PrinterImageChoice Image
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = PrinterImageChoice.Automatic;

    private static string RequireText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}
