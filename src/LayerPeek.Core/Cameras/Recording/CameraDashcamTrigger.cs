namespace LayerPeek.Core.Cameras.Recording;

/// <summary>What makes the dashcam save the last minutes (T-148, user decision 2026-11-04).</summary>
public enum CameraDashcamTrigger
{
    /// <summary>"Save the last minutes" from the tray menu or the camera window; always available.</summary>
    Manual,

    /// <summary>The printer reported an error (<see cref="Printers.PrinterState.Error"/>) during a print.</summary>
    Error,

    /// <summary>The printer waits for the user (<see cref="Printers.PrinterState.Attention"/>) during a print.</summary>
    Attention,

    /// <summary>The print was stopped (<see cref="Printers.PrinterState.Stopped"/>).</summary>
    Stopped,

    /// <summary>The print failure detection of the camera (T-155) reported a failure.</summary>
    FailureDetected,
}
