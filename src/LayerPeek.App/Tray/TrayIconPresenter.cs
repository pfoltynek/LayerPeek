using System.ComponentModel;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Tray;

/// <summary>
/// Pushes an <see cref="ITrayStatusSource"/> to a tray icon. Done in code instead of XAML bindings because the
/// icon is a platform object the view model deliberately does not know. Generic over the icon type so that it is
/// testable without the Avalonia platform (<c>WindowIcon</c> in the app). UI thread only.
/// </summary>
internal sealed class TrayIconPresenter<TIcon> : IDisposable
    where TIcon : class
{
    private readonly ITrayIconTarget<TIcon> _target;
    private readonly ITrayStatusSource _source;
    private readonly Func<TrayIconKey, TIcon> _iconProvider;
    private readonly ILogger _logger;

    /// <param name="iconProvider">Returns the (cached) icon for a key; may throw, the previous icon is then kept.</param>
    public TrayIconPresenter(ITrayIconTarget<TIcon> target, ITrayStatusSource source, Func<TrayIconKey, TIcon> iconProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(iconProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _target = target;
        _source = source;
        _iconProvider = iconProvider;
        _logger = logger;

        ApplyIcon();
        ApplyToolTip();
        _source.PropertyChanged += OnSourcePropertyChanged;
    }

    public void Dispose() => _source.PropertyChanged -= OnSourcePropertyChanged;

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ITrayStatusSource.IconKey):
                ApplyIcon();
                break;
            case nameof(ITrayStatusSource.ToolTipText):
                ApplyToolTip();
                break;
            case null or "":
                // "All properties changed".
                ApplyIcon();
                ApplyToolTip();
                break;
        }
    }

    private void ApplyIcon()
    {
        TrayIconKey key = _source.IconKey;
        TIcon icon;
        try
        {
            icon = _iconProvider(key);
        }
        catch (Exception ex)
        {
            // Keep the previous icon (initially the static one from App.axaml), so the app stays reachable in the tray.
            TrayLog.IconFailed(_logger, ex, key.Status, key.Progress);
            return;
        }

        _target.SetIcon(icon);
    }

    private void ApplyToolTip() => _target.SetToolTipText(_source.ToolTipText);
}

internal static partial class TrayLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Rendering the tray icon {Status} ({Progress} %) failed.")]
    public static partial void IconFailed(ILogger logger, Exception exception, TrayStatus status, int progress);
}
