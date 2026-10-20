using System.Collections.ObjectModel;

using PrusaTrayDiagnostic.App.Localization;
using PrusaTrayDiagnostic.App.Platform;
using PrusaTrayDiagnostic.App.Printers;
using PrusaTrayDiagnostic.App.Resources;
using PrusaTrayDiagnostic.Core.Monitoring;
using PrusaTrayDiagnostic.Core.Printers;
using PrusaTrayDiagnostic.Core.Settings;

namespace PrusaTrayDiagnostic.App.Tray.Menu;

/// <summary>
/// Content of the tray menu, fed by <see cref="IPrinterMonitor"/>: one item per printer with its state and progress,
/// followed by the fixed items. A single printer is a plain item (clicking opens the web interface); with several
/// printers each one is a submenu ("Open … in the browser", "Diagnostics…" preselected on that printer, T-013, and
/// "Camera…" when its camera is enabled, T-014; a single printer gets "Camera…" as the next item).
/// Snapshots arrive on background threads. Each one only compares a cheap <see cref="PrinterMenuContent"/> of its
/// printer under the lock; when nothing visible changed nothing else happens. Changed printers are marked and a single
/// UI update is posted for a whole burst; it formats only the marked items and replaces only those whose
/// <see cref="TrayMenuEntry"/> really differs, so the platform menu is never rebuilt on a poll.
/// <see cref="PrinterItems"/> changes on the UI thread only.
/// </summary>
internal sealed class TrayMenuViewModel : ITrayMenuSource, IMonitoringObserver, IDisposable, ILanguageAware
{
    private readonly IPrinterMonitor _monitor;
    private readonly IUiDispatcher _dispatcher;
    private readonly PrinterWebOpener _webOpener;
    private readonly bool _diagnosticsAvailable;
    private readonly TrayMenuOptions _options;
    private readonly Action _apply;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _disposing = new();
    private readonly CustomPrinterImages? _images;

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

    /// <param name="images">Stored custom printer images for the printer icons (T-039); <see langword="null"/>: illustrations only.</param>
    public TrayMenuViewModel(IPrinterMonitor monitor, IUiDispatcher dispatcher, PrinterWebOpener webOpener, TrayMenuOptions options, CustomPrinterImages? images = null)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(webOpener);
        ArgumentNullException.ThrowIfNull(options);
        _monitor = monitor;
        _dispatcher = dispatcher;
        _webOpener = webOpener;
        _diagnosticsAvailable = options.DiagnosticsAvailable;
        _options = options;
        _apply = Apply;
        FixedItems = CreateFixedItems();
        _items = [MessageEntry(Strings.ToolTipStarting)];
        PrinterItems = new ReadOnlyObservableCollection<TrayMenuEntry>(_items);
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _images = images;
        if (_images is not null && options.PrinterIcons)
        {
            _images.Changed += OnImagesChanged;
        }
    }

    /// <summary>UI thread only (after construction).</summary>
    public IReadOnlyList<TrayMenuEntry> FixedItems { get; private set; }

    public event EventHandler? FixedItemsChanged;

    public ReadOnlyObservableCollection<TrayMenuEntry> PrinterItems { get; }

    /// <inheritdoc/>
    void IMonitoringObserver.ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SetPrinters(settings.Printers);
    }

    /// <summary>
    /// Settings saved without restarting monitoring (T-039: a detected model or another image): the same printers with new
    /// definitions. The states they show are kept; only what changed (the icon) is updated.
    /// </summary>
    void IMonitoringObserver.UpdateSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SetPrinters(settings.Printers, keepStates: true);
    }

    /// <summary>Starts showing <paramref name="printers"/> (configuration order); clears a previous failure. Thread-safe.</summary>
    public void SetPrinters(IReadOnlyList<PrinterDefinition> printers) => SetPrinters(printers, keepStates: false);

    /// <param name="keepStates">
    /// The run goes on (<see cref="IMonitoringObserver.UpdateSettings"/>): printers keep the state they show (by id) and a
    /// failure stays reported.
    /// </param>
    private void SetPrinters(IReadOnlyList<PrinterDefinition> printers, bool keepStates)
    {
        ArgumentNullException.ThrowIfNull(printers);
        Slot[] slots = new Slot[printers.Count];
        Dictionary<string, int> slotById = new(printers.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < slots.Length; i++)
        {
            PrinterDefinition printer = printers[i] ?? throw new ArgumentException("Printer list must not contain null entries.", nameof(printers));
            slots[i] = new Slot(printer, PrinterWebAddress.For(printer)) { Picture = PictureOf(printer) };
            slotById.TryAdd(printer.Id, i);
        }

        lock (_gate)
        {
            if (keepStates && _slots is not null)
            {
                foreach (Slot slot in slots)
                {
                    if (_slotById.TryGetValue(slot.Definition.Id, out int old))
                    {
                        slot.Content = _slots[old].Content;
                    }
                }
            }
            else
            {
                _monitoringFailed = false;
            }

            // Pending marks refer to the previous printers; the new layout is applied as a whole.
            _dirtySlots.Clear();
            _slots = slots;
            _slotById = slotById;
            ScheduleLayoutLocked();
        }
    }

    private PrinterPicture? PictureOf(PrinterDefinition printer) =>
        !_options.PrinterIcons ? null
        : _images is not null ? _images.PictureOf(printer)
        : PrinterPicture.For(printer, customPng: null);

    /// <summary>A custom image was saved or removed (any thread): the icons are resolved again.</summary>
    private void OnImagesChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || _slots is null)
            {
                return;
            }

            bool changed = false;
            foreach (Slot slot in _slots)
            {
                PrinterPicture? picture = PictureOf(slot.Definition);
                if (!Equals(picture, slot.Picture))
                {
                    slot.Picture = picture;
                    changed = true;
                }
            }

            if (changed)
            {
                ScheduleLayoutLocked();
            }
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

        PrinterDefinition? printer = null;
        CancellationToken cancellationToken;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // The printer is logged in (proxy) only when the entry still shows the current address of its printer: an entry
            // from a previous configuration (printer moved) opens its plain address.
            if (entry.PrinterId is { } id
                && _slots is not null
                && _slotById.TryGetValue(id, out int index)
                && _slots[index].WebUri == uri)
            {
                printer = _slots[index].Definition;
            }

            cancellationToken = _disposing.Token;
        }

        // The opener asks the web gateway now (not when the menu was built: it checks the login and may start the local
        // proxy), the launcher validates the address again; both run off the UI thread, neither throws, and failures are
        // logged without the access token.
        _ = _webOpener.OpenAsync(uri, printer, cancellationToken);
    }

    /// <summary>
    /// UI thread: the language changed. Every printer item (including its cached submenu) and the fixed items are
    /// formatted again; the presenter then rebuilds the platform menu once.
    /// </summary>
    public void RefreshTexts()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (Slot slot in _slots ?? [])
            {
                slot.Submenu = null;
            }

            _layoutChanged = true;
        }

        Apply();
        FixedItems = CreateFixedItems();
        FixedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        if (_images is not null)
        {
            _images.Changed -= OnImagesChanged;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        // A browser start still waiting for the web gateway is dropped (e.g. when quitting).
        _disposing.Cancel();
        _disposing.Dispose();
    }

    private TrayMenuEntry[] CreateFixedItems() =>
    [
        new TrayMenuEntry(TrayMenuCommand.Settings, Strings.MenuSettings, _options.SettingsAvailable),
        new TrayMenuEntry(TrayMenuCommand.Diagnostics, Strings.MenuDiagnostics, _options.DiagnosticsAvailable),
        TrayMenuEntry.Separator,
        new TrayMenuEntry(TrayMenuCommand.About, Strings.MenuAbout, IsEnabled: true),
        new TrayMenuEntry(TrayMenuCommand.Quit, Strings.MenuQuit, IsEnabled: true),
    ];

    private static TrayMenuEntry MessageEntry(string text) => new(TrayMenuCommand.None, text, IsEnabled: false);

    /// <summary>
    /// UI thread. One printer: a plain item that opens the web interface (as before T-013). Several printers: a submenu
    /// header "Name: State – 42%" whose items (web interface, diagnostics) are built once per configuration and reused,
    /// so a state change only formats the header.
    /// </summary>
    private TrayMenuEntry PrinterEntry(PrinterLine line)
    {
        string header = TrayMenuFormatter.PrinterHeader(line.Name, line.Content);
        if (!line.Grouped)
        {
            return new TrayMenuEntry(
                TrayMenuCommand.OpenWeb,
                header,
                IsEnabled: line.WebUri is not null,
                TrayMenuFormatter.PrinterToolTip(line.ConnectionType, line.WebUri is not null),
                line.WebUri,
                line.Id)
            {
                Icon = line.Picture,
            };
        }

        // Enabled even when the address is invalid: the submenu still offers diagnostics.
        return new TrayMenuEntry(TrayMenuCommand.None, header, IsEnabled: true, PrinterId: line.Id)
        {
            Submenu = line.Owner.Submenu ??= PrinterSubmenu(line),
            Icon = line.Picture,
        };
    }

    private TrayMenuEntryList PrinterSubmenu(PrinterLine line)
    {
        bool canOpenWeb = line.WebUri is not null;
        TrayMenuEntry web = new(
            TrayMenuCommand.OpenWeb,
            line.ConnectionType == ConnectionType.PrusaConnect ? Strings.MenuOpenPrusaConnect : Strings.MenuOpenPrusaLink,
            canOpenWeb,
            canOpenWeb ? null : Strings.MenuInvalidAddress,
            line.WebUri,
            line.Id);
        TrayMenuEntry diagnostics = new(TrayMenuCommand.Diagnostics, Strings.MenuDiagnostics, _diagnosticsAvailable, PrinterId: line.Id);
        return new TrayMenuEntryList(line.HasCamera ? [web, diagnostics, CameraEntry(line.Id)] : [web, diagnostics]);
    }

    /// <summary>Only for printers with an enabled camera; absent otherwise (T-014).</summary>
    private static TrayMenuEntry CameraEntry(string printerId) =>
        new(TrayMenuCommand.Camera, Strings.MenuCamera, IsEnabled: true, PrinterId: printerId);

    /// <summary>The printer entries of a new layout: a single printer with a camera gets its "Camera…" entry right after it.</summary>
    private TrayMenuEntry[] LayoutEntries(List<(int Index, PrinterLine Line)>? lines)
    {
        if (lines is null)
        {
            return [];
        }

        if (lines is [(_, { Grouped: false, HasCamera: true } single)])
        {
            return [PrinterEntry(single), CameraEntry(single.Id)];
        }

        return [.. lines.Select(line => PrinterEntry(line.Line))];
    }

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
                bool grouped = _slots.Length > 1;
                if (layoutChanged)
                {
                    for (int i = 0; i < _slots.Length; i++)
                    {
                        (lines ??= new(_slots.Length)).Add((i, _slots[i].ToLine(grouped)));
                    }
                }
                else
                {
                    foreach (int index in _dirtySlots)
                    {
                        (lines ??= new(_dirtySlots.Count)).Add((index, _slots[index].ToLine(grouped)));
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
            Synchronize(LayoutEntries(lines));
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
    /// <param name="Grouped">Several printers are configured: the printer is a submenu.</param>
    /// <param name="HasCamera">The printer has an enabled camera: the menu offers "Camera…".</param>
    private readonly record struct PrinterLine(
        Slot Owner,
        string Id,
        string Name,
        ConnectionType ConnectionType,
        Uri? WebUri,
        PrinterMenuContent Content,
        bool Grouped,
        bool HasCamera,
        PrinterPicture? Picture);

    /// <summary>Mutable per-printer state, guarded by the owner's lock (except <see cref="Submenu"/>).</summary>
    private sealed class Slot(PrinterDefinition definition, Uri? webUri)
    {
        public PrinterDefinition Definition => definition;

        public Uri? WebUri => webUri;

        public PrinterMenuContent Content { get; set; }

        /// <summary>Queued in <c>_dirtySlots</c> for the next UI update.</summary>
        public bool Dirty { get; set; }

        /// <summary>UI thread only: the submenu items, built on first use and reused for this configuration.</summary>
        public TrayMenuEntryList? Submenu { get; set; }

        /// <summary>The icon of the printer item (T-039); <see langword="null"/> without printer icons.</summary>
        public PrinterPicture? Picture { get; set; }

        public PrinterLine ToLine(bool grouped) =>
            new(this, definition.Id, definition.Name, definition.ConnectionType, webUri, Content, grouped, definition.Camera is { Enabled: true, StreamUri: not null }, Picture);
    }
}
