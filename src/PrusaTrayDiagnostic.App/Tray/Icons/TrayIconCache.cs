namespace PrusaTrayDiagnostic.App.Tray.Icons;

/// <summary>
/// Renders each <see cref="TrayIconKey"/> once and keeps the result. The key space is small and bounded
/// (one icon per status plus one per progress step), so nothing is ever evicted. Not thread-safe: UI thread only.
/// </summary>
internal sealed class TrayIconCache<TIcon>(Func<TrayIconKey, TIcon> render) : IDisposable
    where TIcon : class
{
    private readonly Dictionary<TrayIconKey, TIcon> _icons = [];
    private bool _disposed;

    public int Count => _icons.Count;

    /// <exception cref="Exception">Rendering failed; nothing is cached, the next call tries again.</exception>
    public TIcon Get(TrayIconKey key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_icons.TryGetValue(key, out TIcon? icon))
        {
            icon = render(key);
            _icons.Add(key, icon);
        }

        return icon;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (TIcon icon in _icons.Values)
        {
            (icon as IDisposable)?.Dispose();
        }

        _icons.Clear();
    }
}
