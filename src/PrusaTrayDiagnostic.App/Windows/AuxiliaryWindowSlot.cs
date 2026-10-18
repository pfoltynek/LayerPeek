namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>An auxiliary window as <see cref="AuxiliaryWindowSlot"/> needs it (an Avalonia window in the app, a fake in tests).</summary>
internal interface IAuxiliaryWindow
{
    event EventHandler? Closed;

    void Show();

    void Activate();

    void Close();
}

/// <summary>
/// Keeps at most one instance of an auxiliary window (About, Diagnostics): a second request activates the open window.
/// What the window owns (its view model) is disposed exactly once when it closes. After <see cref="Shutdown"/> (the app
/// is quitting) the window is closed and no new one opens. UI thread only.
/// </summary>
internal sealed class AuxiliaryWindowSlot
{
    private IAuxiliaryWindow? _window;
    private IDisposable? _owned;
    private bool _shutDown;

    public bool IsOpen => _window is not null;

    /// <param name="create">Creates the window and what it owns (e.g. its view model, which subscribes to live data).</param>
    /// <param name="reuse">
    /// Called with what the open window owns before it is activated again (e.g. to show another printer); not called
    /// for a new window.
    /// </param>
    public void ShowOrActivate(Func<(IAuxiliaryWindow Window, IDisposable? Owned)> create, Action<IDisposable?>? reuse = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (_shutDown)
        {
            return;
        }

        if (_window is not null)
        {
            reuse?.Invoke(_owned);
            _window.Activate();
            return;
        }

        (IAuxiliaryWindow window, IDisposable? owned) = create();
        _window = window;
        _owned = owned;
        window.Closed += OnClosed;
        try
        {
            window.Show();
            window.Activate();
        }
        catch
        {
            window.Closed -= OnClosed;
            Release();
            throw;
        }
    }

    /// <summary>Closes the open window, releases what it owns and refuses new windows. Safe to call repeatedly.</summary>
    public void Shutdown()
    {
        _shutDown = true;
        IAuxiliaryWindow? window = _window;
        if (window is null)
        {
            return;
        }

        window.Close();
        // The view model must be released even if the window did not report closing (e.g. it was never shown).
        window.Closed -= OnClosed;
        Release();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is IAuxiliaryWindow window)
        {
            window.Closed -= OnClosed;
        }

        Release();
    }

    private void Release()
    {
        IDisposable? owned = _owned;
        _window = null;
        _owned = null;
        owned?.Dispose();
    }
}
