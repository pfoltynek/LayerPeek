namespace LayerPeek.Core.Cameras.Overlay;

/// <summary>
/// One of the six places of the camera image where overlay widgets are stacked (T-076 3.1). Persisted by name. The
/// declaration order is the order of <see cref="CameraOverlayLayout.Widgets"/>.
/// </summary>
public enum OverlayAnchor
{
    TopLeft,
    TopCenter,
    TopRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}
