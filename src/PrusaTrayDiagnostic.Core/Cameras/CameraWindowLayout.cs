namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>Default size and position of one kind of camera window (normal window or mini preview), T-035.</summary>
public sealed record CameraWindowLayout
{
    /// <exception cref="ArgumentNullException"><paramref name="size"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="corner"/> is not a defined value.</exception>
    public CameraWindowLayout(CameraWindowSize size, CameraWindowCorner corner)
    {
        Size = size;
        Corner = corner;
    }

    /// <summary>Client size in device-independent units.</summary>
    public CameraWindowSize Size
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    public CameraWindowCorner Corner
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown camera window corner.");
            }

            field = value;
        }
    }
}
