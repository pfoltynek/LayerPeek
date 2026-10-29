namespace LayerPeek.App.Tray;

/// <summary>
/// The synchronization context of the main thread before Avalonia exists (T-099): continuations of <c>await</c> on the
/// main thread (the services started while macOS has no active display) come back to the main thread through
/// <see cref="StartupUiDispatcher"/>, as they would on Avalonia's UI thread, and move to Avalonia's dispatcher with it.
/// </summary>
internal sealed class StartupSynchronizationContext(StartupUiDispatcher dispatcher) : SynchronizationContext
{
    private readonly StartupUiDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        _dispatcher.Post(() => d(state));
    }

    /// <summary>Not supported: waiting for the main thread from another thread could deadlock the startup loop.</summary>
    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("The startup synchronization context only posts.");

    public override SynchronizationContext CreateCopy() => new StartupSynchronizationContext(_dispatcher);
}
