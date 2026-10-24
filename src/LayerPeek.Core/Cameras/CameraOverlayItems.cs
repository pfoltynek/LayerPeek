namespace LayerPeek.Core.Cameras;

/// <summary>
/// Items the print overlay of a camera window can show (T-036). Printer items (<see cref="PrinterName"/>,
/// <see cref="State"/>, <see cref="NozzleTemperature"/>, <see cref="BedTemperature"/>) are shown whenever they are known;
/// job items (<see cref="CameraOverlayOptions.JobItems"/>) only during a job. Persisted by name.
/// </summary>
[Flags]
public enum CameraOverlayItems
{
    None = 0,
    PrinterName = 1 << 0,
    State = 1 << 1,
    NozzleTemperature = 1 << 2,
    BedTemperature = 1 << 3,
    Progress = 1 << 4,
    RemainingTime = 1 << 5,
    FinishTime = 1 << 6,
    PrintTime = 1 << 7,
    FileName = 1 << 8,
}
