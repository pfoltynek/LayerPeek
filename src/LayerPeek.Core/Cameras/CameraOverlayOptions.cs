namespace LayerPeek.Core.Cameras;

/// <summary>
/// What the print overlay of the camera window shows and where (T-036), part of <see cref="CameraSettings"/>. The normal
/// window and full screen share <see cref="Normal"/>, the mini preview has its own <see cref="Mini"/>; both are drawn in
/// <see cref="Corner"/>. The defaults reproduce the T-014 overlay: state, progress and remaining time in the bottom left
/// corner. Kept while the camera is disabled; changes apply to open camera windows after saving. The connection state of
/// the camera is not part of it (always shown).
/// </summary>
public sealed record CameraOverlayOptions
{
    /// <summary>Every item.</summary>
    public const CameraOverlayItems AllItems =
        CameraOverlayItems.PrinterName | CameraOverlayItems.State | CameraOverlayItems.NozzleTemperature | CameraOverlayItems.BedTemperature
        | JobItems;

    /// <summary>Items shown only during a job (running, paused or waiting for the user mid-print).</summary>
    public const CameraOverlayItems JobItems =
        CameraOverlayItems.Progress | CameraOverlayItems.RemainingTime | CameraOverlayItems.FinishTime | CameraOverlayItems.PrintTime
        | CameraOverlayItems.FileName;

    /// <summary>The T-014 overlay: "Printing – 42%" and "1 h 5 min remaining".</summary>
    public const CameraOverlayItems DefaultItems = CameraOverlayItems.State | CameraOverlayItems.Progress | CameraOverlayItems.RemainingTime;

    public const CameraOverlayCorner DefaultCorner = CameraOverlayCorner.BottomLeft;

    /// <summary>All defaults; also used for settings files written before T-036.</summary>
    public static CameraOverlayOptions Default { get; } = new();

    /// <summary>The single items in display order: printer data first, then job data (T-033 design).</summary>
    public static IReadOnlyList<CameraOverlayItems> Items { get; } =
    [
        CameraOverlayItems.PrinterName,
        CameraOverlayItems.State,
        CameraOverlayItems.NozzleTemperature,
        CameraOverlayItems.BedTemperature,
        CameraOverlayItems.Progress,
        CameraOverlayItems.RemainingTime,
        CameraOverlayItems.FinishTime,
        CameraOverlayItems.PrintTime,
        CameraOverlayItems.FileName,
    ];

    /// <summary>Items of the normal window and of full screen.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value contains unknown flags.</exception>
    public CameraOverlayItems Normal
    {
        get;
        init => field = Validate(value);
    } = DefaultItems;

    /// <summary>Items of the mini preview.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value contains unknown flags.</exception>
    public CameraOverlayItems Mini
    {
        get;
        init => field = Validate(value);
    } = DefaultItems;

    /// <exception cref="ArgumentOutOfRangeException">Not one of the four corners.</exception>
    public CameraOverlayCorner Corner
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown camera overlay corner.");
            }

            field = value;
        }
    } = DefaultCorner;

    /// <summary>The items of <paramref name="mode"/>: <see cref="Mini"/> for the mini preview, <see cref="Normal"/> otherwise.</summary>
    public CameraOverlayItems ItemsFor(CameraWindowMode mode) => mode == CameraWindowMode.Mini ? Mini : Normal;

    /// <summary>Whether <paramref name="items"/> contains only known flags.</summary>
    public static bool IsValid(CameraOverlayItems items) => (items & ~AllItems) == 0;

    private static CameraOverlayItems Validate(CameraOverlayItems value) =>
        IsValid(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown camera overlay items.");
}
