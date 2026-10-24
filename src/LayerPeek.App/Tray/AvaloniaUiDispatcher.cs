using Avalonia.Threading;

namespace LayerPeek.App.Tray;

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
