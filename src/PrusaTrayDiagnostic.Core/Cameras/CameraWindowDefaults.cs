namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>
/// How the camera window of a printer opens (T-035), part of <see cref="CameraSettings"/>. The defaults reproduce the
/// T-014 behaviour: the remembered placement wins, otherwise a 960 × 540 normal window in the center of the primary
/// monitor; the mini preview is 320 × 180. Kept while the camera is disabled. Changes apply the next time the window opens.
/// </summary>
public sealed record CameraWindowDefaults
{
    /// <summary>Normal window: 960 × 540 in the center (T-014).</summary>
    public static CameraWindowLayout DefaultNormal { get; } = new(new CameraWindowSize(960, 540), CameraWindowCorner.Center);

    /// <summary>Mini preview: 320 × 180 (T-014) in the bottom right corner.</summary>
    public static CameraWindowLayout DefaultMini { get; } = new(new CameraWindowSize(320, 180), CameraWindowCorner.BottomRight);

    /// <summary>All defaults; also used for settings files written before T-035.</summary>
    public static CameraWindowDefaults Default { get; } = new();

    /// <summary>
    /// <see langword="true"/> (default): the remembered position and size (<c>camera-windows.json</c>) are used when there
    /// are any, these defaults only otherwise. <see langword="false"/>: the window always opens with these defaults.
    /// </summary>
    public bool RememberLastPlacement { get; init; } = true;

    public CameraWindowStartMode StartMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown camera window start mode.");
            }

            field = value;
        }
    } = CameraWindowStartMode.Last;

    /// <summary>
    /// Monitor for all modes (including full screen); <see langword="null"/> = the primary monitor. A chosen monitor that
    /// is not connected is kept; the window then opens on the primary monitor.
    /// </summary>
    public CameraMonitorId? Monitor { get; init; }

    /// <summary>Size and position of the normal window.</summary>
    public CameraWindowLayout Normal
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = DefaultNormal;

    /// <summary>Size and position of the mini preview.</summary>
    public CameraWindowLayout Mini
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = DefaultMini;

    /// <summary>
    /// Whether saving <paramref name="updated"/> over <paramref name="saved"/> must forget the remembered state of the
    /// camera window of that printer (T-035 decision: changed defaults clear it, so that they take effect). Counts the
    /// start mode, the monitor and both layouts; a change of <see cref="RememberLastPlacement"/> alone does not.
    /// </summary>
    public static bool RequiresForgettingRememberedState(CameraWindowDefaults saved, CameraWindowDefaults updated)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(updated);
        return saved.StartMode != updated.StartMode
            || saved.Monitor != updated.Monitor
            || saved.Normal != updated.Normal
            || saved.Mini != updated.Mini;
    }
}
