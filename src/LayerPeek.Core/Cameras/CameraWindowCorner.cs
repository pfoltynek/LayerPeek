namespace LayerPeek.Core.Cameras;

/// <summary>
/// Where a camera window opens in the working area of its monitor (T-035): a corner with
/// <see cref="CameraWindowDefaultPlacement.MarginDip"/> from both edges, or the center. Persisted by name.
/// </summary>
public enum CameraWindowCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center,
}
