using LayerPeek.App.Tray;

namespace LayerPeek.App.Composition;

/// <summary>
/// The services <c>Program.Main</c> started before Avalonia because macOS had no active display (T-099), handed to
/// <see cref="App"/>: once Avalonia runs, <see cref="Dispatcher"/> is attached to Avalonia's dispatcher and the tray attaches
/// to <see cref="Services"/> (the same monitoring session, never a second one).
/// </summary>
/// <param name="Dispatcher">The UI thread's dispatcher of the services until Avalonia's takes over.</param>
/// <param name="Services">The services; completes on the main thread (it may still be loading the settings).</param>
internal sealed record EarlyServices(StartupUiDispatcher Dispatcher, Task<ServiceHost> Services);
