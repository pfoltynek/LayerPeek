using Avalonia.Threading;

namespace PrusaTrayDiagnostic.App.Tray;

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
