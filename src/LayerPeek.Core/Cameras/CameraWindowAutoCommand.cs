namespace LayerPeek.Core.Cameras;

/// <summary>What <see cref="CameraWindowAutoVisibility"/> asks the app to do with the camera window of a printer (T-150).</summary>
public enum CameraWindowAutoAction
{
    /// <summary>
    /// Close the window automatically: the same path as a close by the user (remembers the placement, ends the stream), but
    /// without reporting <see cref="CameraWindowAutoVisibility.WindowClosedByUser"/>. Remember the "closed automatically" flag.
    /// </summary>
    Close,

    /// <summary>
    /// Open the window again, the same way as "Show camera" from the tray menu, without taking the focus. Forget the
    /// "closed automatically" flag.
    /// </summary>
    Open,

    /// <summary>Only forget the remembered "closed automatically" flag; the window stays as it is.</summary>
    ForgetAutoClosed,
}

/// <summary>A command of <see cref="CameraWindowAutoVisibility"/> for one printer (id as configured).</summary>
public readonly record struct CameraWindowAutoCommand(string PrinterId, CameraWindowAutoAction Action)
{
    /// <summary>
    /// The "closed automatically" flag of the printer after this command, to be persisted (<c>camera-windows.json</c>):
    /// <see langword="true"/> only for <see cref="CameraWindowAutoAction.Close"/>.
    /// </summary>
    public bool AutoClosed => Action == CameraWindowAutoAction.Close;
}
