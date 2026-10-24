namespace LayerPeek.Core.Cameras;

/// <summary>How the camera window is shown (T-014). In Core since T-035, because the window defaults and placement use it.</summary>
public enum CameraWindowMode
{
    /// <summary>A regular resizable window with a title bar.</summary>
    Normal,

    /// <summary>The whole screen.</summary>
    FullScreen,

    /// <summary>Small borderless preview, always on top of other windows (picture-in-picture).</summary>
    Mini,
}
