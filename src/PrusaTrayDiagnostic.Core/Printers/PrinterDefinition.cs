using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.Core.Printers;

/// <summary>
/// A configured printer. Contains no secrets; credentials live in <see cref="Secrets.ISecretStore"/>.
/// Text values are trimmed and must not be empty.
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

    private static string RequireText(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim();
    }
}
