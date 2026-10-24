namespace LayerPeek.App.Platform;

internal static class Disposables
{
    /// <summary>
    /// Disposes every item in order, also when some fail, so that one broken resource does not leak the rest.
    /// Failures are passed to <paramref name="onFailure"/>; a failing handler (e.g. a disposed logger) is ignored.
    /// </summary>
    public static void DisposeAll(IEnumerable<IDisposable?> items, Action<Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(onFailure);
        foreach (IDisposable? item in items)
        {
            try
            {
                item?.Dispose();
            }
            catch (Exception ex)
            {
                Report(onFailure, ex);
            }
        }
    }

    private static void Report(Action<Exception> onFailure, Exception exception)
    {
        try
        {
            onFailure(exception);
        }
        catch (Exception)
        {
            // Nothing left to report to; releasing the remaining items matters more.
        }
    }
}
