namespace PrusaTrayDiagnostic.Core.Settings;

/// <summary>
/// Which print events produce a system notification. <see cref="Enabled"/> is the master switch.
/// </summary>
public sealed record NotificationSettings
{
    public bool Enabled { get; init; } = true;

    public bool PrintStarted { get; init; } = true;

    public bool PrintFinished { get; init; } = true;

    public bool PrintPaused { get; init; } = true;

    public bool PrintResumed { get; init; } = true;

    /// <summary>A job was stopped (cancelled) before completion.</summary>
    public bool PrintStopped { get; init; } = true;

    public bool Error { get; init; } = true;

    /// <summary>The printer waits for the user (e.g. filament runout).</summary>
    public bool Attention { get; init; } = true;

    /// <summary>The printer went offline or came back online.</summary>
    public bool Offline { get; init; } = true;
}
