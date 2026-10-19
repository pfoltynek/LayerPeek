namespace PrusaTrayDiagnostic.Core.Cameras;

/// <summary>Mode in which the camera window opens (T-035). Persisted by name.</summary>
public enum CameraWindowStartMode
{
    /// <summary>The mode the window had last time (<see cref="CameraWindowMode.Normal"/> when nothing is remembered).</summary>
    Last,

    /// <summary>Always <see cref="CameraWindowMode.Normal"/>.</summary>
    Normal,

    /// <summary>Always <see cref="CameraWindowMode.Mini"/>.</summary>
    Mini,

    /// <summary>Always <see cref="CameraWindowMode.FullScreen"/>.</summary>
    FullScreen,
}
