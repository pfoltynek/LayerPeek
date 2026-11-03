namespace LayerPeek.App.Platform;

/// <summary>
/// The computer woke up from sleep (T-151, T-150 decision 9: the camera window delays start again). Windows: the
/// suspend/resume notification of the power manager (<see cref="Windows.WindowsSystemResumeEvents"/>); macOS:
/// <c>NSWorkspaceDidWakeNotification</c> (<see cref="MacOS.MacSystemResumeEvents"/>). Optional: without it a long gap
/// between snapshots is noticed anyway (<c>CameraWindowAutoVisibility.StaleAfter</c>).
/// </summary>
internal interface ISystemResumeEvents
{
    /// <summary>Raised after a resume, on any thread.</summary>
    event EventHandler? Resumed;

    /// <summary>Starts listening (UI thread, once the UI runs); later calls do nothing. Never throws.</summary>
    void Start();
}

/// <summary>No resume events (other OS, tests).</summary>
internal sealed class NoSystemResumeEvents : ISystemResumeEvents
{
    public static readonly NoSystemResumeEvents Instance = new();

    public event EventHandler? Resumed
    {
        add { }
        remove { }
    }

    public void Start()
    {
    }
}
