namespace LayerPeek.App.Settings;

/// <summary>
/// Serializes the two writers of whole overlay layouts in this process: a save of the settings window (from reading the
/// latest saved layouts until monitoring got the saved settings) and "Done" of the layout mode of a camera window (writing
/// one layout and announcing it). Thus a layout saved by a camera window is either announced before the settings window
/// reads the latest layouts (the save keeps it) or written after the settings file (the file keeps it); it is never lost
/// between them, and monitoring never gets the settings window's older copy after the announced layout.
/// </summary>
/// <remarks>Not reentrant. Handlers of the layout announcement run while it is held and must not wait for it.</remarks>
internal sealed class SettingsWriteGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Waits until no other writer holds the gate; pair with <see cref="Exit"/>.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task EnterAsync(CancellationToken cancellationToken) => _semaphore.WaitAsync(cancellationToken);

    public void Exit() => _semaphore.Release();

    public void Dispose() => _semaphore.Dispose();
}
