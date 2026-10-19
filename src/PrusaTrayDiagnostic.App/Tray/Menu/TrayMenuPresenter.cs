using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Microsoft.Extensions.Logging;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Mirrors an <see cref="ITrayMenuSource"/> onto a platform menu item by item (a changed printer updates only its own
/// item) and routes clicks: "open web" items to the web interface, diagnostics (with the printer of its submenu), settings and the
/// other fixed items to <see cref="ITrayAppCommands"/>; submenu headers do nothing.
/// UI thread only.
/// </summary>
internal sealed partial class TrayMenuPresenter : IDisposable
{
    private readonly ITrayMenuTarget _target;
    private readonly ITrayMenuSource _source;
    private readonly ITrayAppCommands _commands;
    private readonly ILogger _logger;
    private readonly INotifyCollectionChanged _printerItems;
    private bool _disposed;

    public TrayMenuPresenter(ITrayMenuTarget target, ITrayMenuSource source, ITrayAppCommands commands, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(logger);
        _target = target;
        _source = source;
        _commands = commands;
        _logger = logger;
        _printerItems = source.PrinterItems;

        _target.SetFixedItems(source.FixedItems);
        InsertAll();
        _printerItems.CollectionChanged += OnPrinterItemsChanged;
        _source.FixedItemsChanged += OnFixedItemsChanged;
        _target.ItemClicked += OnItemClicked;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _printerItems.CollectionChanged -= OnPrinterItemsChanged;
        _source.FixedItemsChanged -= OnFixedItemsChanged;
        _target.ItemClicked -= OnItemClicked;
    }

    /// <summary>Language switch: the menu is built again (it happens once per switch, never on a poll).</summary>
    private void OnFixedItemsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _target.SetFixedItems(_source.FixedItems);
        InsertAll();
    }

    private void OnPrinterItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Replace when e.NewItems is not null && e.NewStartingIndex >= 0:
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    _target.UpdatePrinterItem(e.NewStartingIndex + i, (TrayMenuEntry)e.NewItems[i]!);
                }

                break;
            case NotifyCollectionChangedAction.Add when e.NewItems is not null && e.NewStartingIndex >= 0:
                for (int i = 0; i < e.NewItems.Count; i++)
                {
                    _target.InsertPrinterItem(e.NewStartingIndex + i, (TrayMenuEntry)e.NewItems[i]!);
                }

                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null && e.OldStartingIndex >= 0:
                for (int i = 0; i < e.OldItems.Count; i++)
                {
                    _target.RemovePrinterItem(e.OldStartingIndex);
                }

                break;
            default:
                // Move, Reset or a change without indexes: rebuild the printer part only.
                _target.ClearPrinterItems();
                InsertAll();
                break;
        }
    }

    private void InsertAll()
    {
        ReadOnlyObservableCollection<TrayMenuEntry> items = _source.PrinterItems;
        for (int i = 0; i < items.Count; i++)
        {
            _target.InsertPrinterItem(i, items[i]);
        }
    }

    private void OnItemClicked(object? sender, TrayMenuEntry entry)
    {
        // A click can still arrive from an open menu after quitting started; disabled items are never acted on.
        if (_disposed || entry is null || !entry.IsEnabled || entry.IsSeparator)
        {
            return;
        }

        try
        {
            switch (entry.Command)
            {
                case TrayMenuCommand.OpenWeb:
                    _source.OpenWeb(entry);
                    break;
                case TrayMenuCommand.Diagnostics:
                    _commands.ShowDiagnostics(entry.PrinterId);
                    break;
                case TrayMenuCommand.Camera when entry.PrinterId is { } printerId:
                    _commands.ShowCamera(printerId);
                    break;
                case TrayMenuCommand.Settings:
                    _commands.ShowSettings();
                    break;
                case TrayMenuCommand.About:
                    _commands.ShowAbout();
                    break;
                case TrayMenuCommand.Quit:
                    _commands.Quit();
                    break;
                default:
                    break;
            }
        }
        catch (Exception ex)
        {
            // Called from a platform event handler: an exception would crash the app.
            LogCommandFailed(_logger, ex, entry.Command);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Tray menu command {Command} failed.")]
    private static partial void LogCommandFailed(ILogger logger, Exception exception, TrayMenuCommand command);
}
