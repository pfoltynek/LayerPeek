namespace LayerPeek.App.Tray;

/// <summary>Queues work to the UI thread; abstracted so that view models are testable without Avalonia.</summary>
internal interface IUiDispatcher
{
    /// <summary>Runs <paramref name="action"/> later on the UI thread, in posting order. Never blocks.</summary>
    void Post(Action action);
}
