using System.Collections.ObjectModel;

using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Content of the tray menu, fed by <see cref="IPrinterMonitor"/>: one item per printer with its state and progress
/// (clicking opens the web interface), followed by the fixed items.
/// Snapshots arrive on background threads. Each one only compares a cheap <see cref="PrinterMenuContent"/> of its
/// printer under the lock; when nothing visible changed nothing else happens. Changed printers are marked and a single
/// UI update is posted for a whole burst; it formats only the marked items and replaces only those whose
/// <see cref="TrayMenuEntry"/> really differs, so the platform menu is never rebuilt on a poll.
/// <see cref="PrinterItems"/> changes on the UI thread only.
/// </summary>
internal sealed class TrayMenuViewModel : ITrayMenuSource, IMonitoringObserver, IDisposable
{
    private readonly IPrinterMonitor _monitor;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUrlLauncher _launcher;
    private readonly Action _apply;
    private readonly Lock _gate = new();

    // UI thread only (after construction).
    private readonly ObservableCollection<TrayMenuEntry> _items;

    // Guarded by _gate.
    private Slot[]? _slots;
    private Dictionary<string, int> _slotById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _dirtySlots = [];
    private bool _monitoringFailed;
    private bool _layoutChanged;
    private bool _applyPending;
    private bool _disposed;

    public TrayMenuViewModel(IPrinterMonitor monitor, IUiDispatcher dispatcher, IUrlLauncher launcher, TrayMenuOptions options)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(options);
        _monitor = monitor;
        _dispatcher = dispatcher;
        _launcher = launcher;
        _apply = Apply;
        FixedItems =
        [
            new TrayMenuEntry(TrayMenuCommand.Settings, Strings.MenuSettings, options.SettingsAvailable),
            new TrayMenuEntry(TrayMenuCommand.Diagnostics, Strings.MenuDiagnostics, options.DiagnosticsAvailable),
            TrayMenuEntry.Separator,
            new TrayMenuEntry(TrayMenuCommand.About, Strings.MenuAbout, IsEnabled: true),
            new TrayMenuEntry(TrayMenuCommand.Quit, Strings.MenuQuit, IsEnabled: true),
        ];
        _items = [MessageEntry(Strings.ToolTipStarting)];
        PrinterItems = new ReadOnlyObservableCollection<TrayMenuEntry>(_items);
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    public IReadOnlyList<TrayMenuEntry> FixedItems { get; }

    public ReadOnlyObservableCollection<TrayMenuEntry> PrinterItems { get; }

    /// <inheritdoc/>
    void IMonitoringObserver.ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SetPrinters(settings.Printers);
    }

    /// <summary>Starts showing <paramref name="printers"/> (configuration order); clears a previous failure. Thread-safe.</summary>
    public void SetPrinters(IReadOnlyList<PrinterDefinition> printers)
    {
        ArgumentNullException.ThrowIfNull(printers);
        Slot[] slots = new Slot[printers.Count];
        Dictionary<string, int> slotById = new(printers.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < slots.Length; i++)
        {
            PrinterDefinition printer = printers[i] ?? throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            slots[i] = new Slot(printer, PrinterWebAddress.For(printer));
            slotById.TryAdd(printer.Id, i);
        }

        lock (_gate)
        {
            // Pending marks refer to the previous printers; the new layout is applied as a whole.
            _dirtySlots.Clear();
            _slots = slots;
            _slotById = slotById;
            _monitoringFailed = false;
            ScheduleLayoutLocked();
        }
    }

    /// <inheritdoc/>
    public void ReportMonitoringFailed()
    {
        lock (_gate)
        {
            _monitoringFailed = true;
            ScheduleLayoutLocked();
        }
    }

    public void OpenWeb(TrayMenuEntry entry)
    {
        if (entry is not { Command: TrayMenuCommand.OpenWeb, IsEnabled: true, WebUri: { } uri })
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        // The launcher validates the address again, runs off the UI thread and logs failures; it never throws.
        _ = _launcher.OpenAsync(uri);
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        lock (_gate)
        {
            _disposed = true;
        }
    }

    private static TrayMenuEntry MessageEntry(string text) => new(TrayMenuCommand.None, text, IsEnabled: false);

    private static TrayMenuEntry PrinterEntry(PrinterLine line) => new(
        TrayMenuCommand.OpenWeb,
        TrayMenuFormatter.PrinterHeader(line.Name, line.Content),
        IsEnabled: line.WebUri is not null,
        TrayMenuFormatter.PrinterToolTip(line.ConnectionType, line.WebUri is not null),
        line.WebUri);

    private void OnSnapshotUpdated(object? sender, PrinterSnapshot snapshot)
    {
        if (snapshot is null)
        {
            return;
        }

        PrinterMenuContent content = PrinterMenuContent.From(snapshot);
        lock (_gate)
        {
            // Snapshots of printers that are not (or no longer) configured are ignored.
            if (_disposed || _slots is null || !_slotById.TryGetValue(snapshot.PrinterId, out int index))
            {
                return;
            }

            Slot slot = _slots[index];
            if (slot.Content == content)
            {
                return;
            }

            slot.Content = content;
            // After a failure the menu shows the message only; the content is kept for consistency, nothing is shown.
            if (_monitoringFailed || slot.Dirty)
            {
                return;
            }

            slot.Dirty = true;
            _dirtySlots.Add(index);
            ScheduleApplyLocked();
        }
    }

    private void ScheduleLayoutLocked()
    {
        if (_disposed)
        {
            return;
        }

        _layoutChanged = true;
        ScheduleApplyLocked();
    }

    private void ScheduleApplyLocked()
    {
        if (!_applyPending)
        {
            _applyPending = true;
            _dispatcher.Post(_apply);
        }
    }

    /// <summary>UI thread. Copies what changed under the lock, formats and applies it outside.</summary>
    private void Apply()
    {
        string? message;
        bool layoutChanged;
        // Created only when printer items have to be formatted (not for status messages or empty updates).
        List<(int Index, PrinterLine Line)>? lines = null;
        lock (_gate)
        {
            _applyPending = false;
            if (_disposed)
            {
                return;
            }

            layoutChanged = _layoutChanged;
            _layoutChanged = false;
            message = _monitoringFailed ? Strings.ToolTipMonitoringFailed
                : _slots is null ? Strings.ToolTipStarting
                : _slots.Length == 0 ? Strings.ToolTipNoPrinter
                : null;

            if (message is null && _slots is not null)
            {
                if (layoutChanged)
                {
                    for (int i = 0; i < _slots.Length; i++)
                    {
                        (lines ??= new(_slots.Length)).Add((i, _slots[i].ToLine()));
                    }
                }
                else
                {
                    foreach (int index in _dirtySlots)
                    {
                        (lines ??= new(_dirtySlots.Count)).Add((index, _slots[index].ToLine()));
                    }
                }
            }

            foreach (int index in _dirtySlots)
            {
                if (_slots is not null && index < _slots.Length)
                {
                    _slots[index].Dirty = false;
                }
            }

            _dirtySlots.Clear();
        }

        if (message is not null)
        {
            Synchronize([MessageEntry(message)]);
        }
        else if (layoutChanged)
        {
            Synchronize(lines is null ? [] : [.. lines.Select(static line => PrinterEntry(line.Line))]);
        }
        else if (lines is not null)
        {
            foreach ((int index, PrinterLine line) in lines)
            {
                TrayMenuEntry entry = PrinterEntry(line);
                // A change that ended where it started (e.g. busy and back to idle in one burst) touches nothing.
                if (index < _items.Count && !_items[index].Equals(entry))
                {
                    _items[index] = entry;
                }
            }
        }
    }

    /// <summary>Turns the collection into <paramref name="entries"/> with the fewest changes: replace, then add or remove at the end.</summary>
    private void Synchronize(TrayMenuEntry[] entries)
    {
        int common = Math.Min(entries.Length, _items.Count);
        for (int i = 0; i < common; i++)
        {
            if (!_items[i].Equals(entries[i]))
            {
                _items[i] = entries[i];
            }
        }

        for (int i = common; i < entries.Length; i++)
        {
            _items.Add(entries[i]);
        }

        while (_items.Count > entries.Length)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }

    /// <summary>What one printer item needs; copied out of the lock for formatting.</summary>
    private readonly record struct PrinterLine(string Name, ConnectionType ConnectionType, Uri? WebUri, PrinterMenuContent Content);

    /// <summary>Mutable per-printer state, guarded by the owner's lock.</summary>
    private sealed class Slot(PrinterDefinition definition, Uri? webUri)
    {
        public PrinterMenuContent Content { get; set; }

        /// <summary>Queued in <c>_dirtySlots</c> for the next UI update.</summary>
        public bool Dirty { get; set; }

        public PrinterLine ToLine() => new(definition.Name, definition.ConnectionType, webUri, Content);
    }
}
