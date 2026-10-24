using Avalonia.Controls;
using Avalonia.Threading;

namespace LayerPeek.App.UiTests.Infrastructure;

/// <summary>
/// What one UI test body created: windows are closed and objects disposed when the body ends, also when it fails, so
/// that nothing outlives the test (the application itself is torn down by the session after every test). UI thread only.
/// </summary>
public sealed class UiTestScope : IDisposable
{
    private readonly List<Window> _windows = [];
    private readonly List<IDisposable> _disposables = [];

    internal UiTestScope(CancellationToken cancellationToken) => CancellationToken = cancellationToken;

    /// <summary>Cancelled by the xUnit test token or after <see cref="HeadlessSession.TestTimeout"/>.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Closes <paramref name="window"/> after the test unless the test closed it.</summary>
    public T Track<T>(T window)
        where T : Window
    {
        ArgumentNullException.ThrowIfNull(window);
        _windows.Add(window);
        return window;
    }

    /// <summary>Disposes <paramref name="disposable"/> after the test (in reverse order of registration).</summary>
    public T Defer<T>(T disposable)
        where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(disposable);
        _disposables.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        List<Exception> failures = [];
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            try
            {
                if (_windows[i].IsVisible)
                {
                    _windows[i].Close();
                }
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        for (int i = _disposables.Count - 1; i >= 0; i--)
        {
            try
            {
                _disposables[i].Dispose();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        _windows.Clear();
        _disposables.Clear();
        Dispatcher.UIThread.RunJobs();
        if (failures.Count > 0)
        {
            throw new AggregateException("UI test cleanup failed.", failures);
        }
    }
}
