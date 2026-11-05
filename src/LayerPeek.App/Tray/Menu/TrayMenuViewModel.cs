using System.Collections.ObjectModel;

using LayerPeek.App.Cameras.Recording;
using LayerPeek.App.Localization;
using LayerPeek.App.Platform;
using LayerPeek.App.Printers;
using LayerPeek.App.Resources;
using LayerPeek.App.Updates;
using LayerPeek.Core.Cameras.Recording;
using LayerPeek.Core.Monitoring;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Tray.Menu;

/// <summary>
/// Content of the tray menu, fed by <see cref="IPrinterMonitor"/>: one item per printer with its state and progress,
/// followed by the fixed items (diagnostics, settings, about, quit). Every printer is a submenu, also when it is the only
/// one (T-042): "Open PrusaLink" and "Show camera" / "Hide camera" when its camera is enabled
/// (T-014; the text follows whether the camera window is open, 2026-10-22; an open window also has "Bring camera to front" bringing
/// it to the front, T-102). "Diagnostics…" is a single main menu item
/// (user decision 2026-10-21: the window has its own printer combo), enabled only while the menu shows printers (not while starting, after a monitoring failure or without
/// printers). Without printers the menu says so and offers "Add printer…". Fixed and submenu items carry access keys
/// from the resources; printer items have none.
/// Snapshots arrive on background threads. Each one only compares a cheap <see cref="PrinterMenuContent"/> of its
/// printer under the lock; when nothing visible changed nothing else happens. Changed printers are marked and a single
/// UI update is posted for a whole burst; it formats only the marked items and replaces only those whose
/// <see cref="TrayMenuEntry"/> really differs, so the platform menu is never rebuilt on a poll. The fixed items are
/// replaced as a whole (and the platform menu rebuilt) only on a language switch; enabling or disabling "Diagnostics…"
/// changes that one entry (<see cref="FixedItemChanged"/>), updated in place so that an open menu stays as it is.
/// T-058: while a new version is offered (<see cref="UpdateOffer"/>), "A new version X.Y.Z is available" stands above "User
/// manual" (T-172, right above "About…"); it appearing or disappearing replaces the fixed items (a rebuild, once per found version), its text changing
/// while an update downloads updates it in place.
/// T-146: with recordings, a printer with a camera also has, after a separator, "Start recording" / "Stop recording"
/// (disabled with the reason while it starts or stops, or when this build cannot record), "Play last recording" and "Open
/// recordings folder"; a change of its recording (rare: start, stop, failure) formats only that printer's submenu again,
/// updated in place. T-161: the same for its timelapse ("Start / Stop timelapse (experimental)", "Play last timelapse"),
/// which the same change event reports.
/// <see cref="PrinterItems"/> and <see cref="FixedItems"/> change on the UI thread only.
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
    private readonly UpdateOffer? _updates;
    private readonly ICameraRecordings? _recordings;

    // UI thread only: the update item shown now (T-058), or null.
    private TrayMenuEntry? _updateEntry;

    // UI thread only (after construction). _openCameras: printers whose camera window is open (their camera item reads
    // "Hide camera").
    private readonly ObservableCollection<TrayMenuEntry> _items;
    private readonly HashSet<string> _openCameras = new(StringComparer.OrdinalIgnoreCase);

    // UI thread only: "Diagnostics…" is enabled (the window exists and the menu shows printer items).
    private bool _diagnosticsEnabled;

    // Guarded by _gate.
    private Slot[]? _slots;
    private Dictionary<string, int> _slotById = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<int> _dirtySlots = [];
    private bool _monitoringFailed;
    private bool _layoutChanged;
    private bool _applyPending;
    private bool _disposed;

    /// <param name="images">Stored custom printer images for the printer icons (T-039); <see langword="null"/>: illustrations only.</param>
    /// <param name="updates">The update the app offers (T-058); <see langword="null"/>: no update item.</param>
    /// <param name="recordings">The camera recordings (T-146); <see langword="null"/>: no recording items.</param>
    public TrayMenuViewModel(
        IPrinterMonitor monitor,
        IUiDispatcher dispatcher,
        PrinterWebOpener webOpener,
        TrayMenuOptions options,
        CustomPrinterImages? images = null,
        UpdateOffer? updates = null,
        ICameraRecordings? recordings = null)
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
        _updates = updates;
        _updateEntry = UpdateEntry();
        FixedItems = CreateFixedItems();
        _items = [MessageEntry(Strings.ToolTipStarting)];
        PrinterItems = new ReadOnlyObservableCollection<TrayMenuEntry>(_items);
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        _images = images;
        if (_images is not null && options.PrinterIcons)
        {
            _images.Changed += OnImagesChanged;
        }

        if (_updates is not null)
        {
            _updates.Changed += OnUpdateChanged;
        }

        _recordings = recordings;
        if (_recordings is not null)
        {
            _recordings.Changed += OnRecordingChanged;
            _recordings.TimelapseExportChanged += OnTimelapseExportChanged;
        }
    }

    /// <summary>UI thread only (after construction).</summary>
    public IReadOnlyList<TrayMenuEntry> FixedItems { get; private set; }

    /// <summary>
    /// <see cref="FixedItems"/> was replaced as a whole: once per language switch, never otherwise. Raised on the UI thread
    /// after <see cref="PrinterItems"/> is up to date.
    /// </summary>
    public event EventHandler? FixedItemsChanged;

    /// <summary>
    /// The entry at the given index of <see cref="FixedItems"/> changed: "Diagnostics…" got enabled or disabled (printers
    /// shown ↔ starting, monitoring failed or no printer); never on a poll. Raised on the UI thread after
    /// <see cref="PrinterItems"/> is up to date.
    /// </summary>
    public event EventHandler<int>? FixedItemChanged;

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
    /// UI thread (T-146): a recording item was clicked. Start and Stop act on what the item showed (never a toggle of a state
    /// that changed meanwhile); the service ignores a Start while it records and a Stop while it does not. Playing and opening
    /// run off the UI thread and never throw.
    /// </summary>
    public void RunRecordingCommand(TrayMenuEntry entry)
    {
        if (_recordings is not { } recordings || entry is not { IsEnabled: true })
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

        switch (entry.Command)
        {
            case TrayMenuCommand.StartRecording when entry.PrinterId is { } id:
                recordings.Start(id);
                break;
            case TrayMenuCommand.StopRecording when entry.PrinterId is { } id:
                recordings.Stop(id);
                break;
            case TrayMenuCommand.PlayLastRecording when entry.PrinterId is { } id:
                _ = recordings.PlayLastAsync(id);
                break;
            case TrayMenuCommand.OpenRecordingsFolder when entry.PrinterId is { } id:
                _ = recordings.OpenPrinterFolderAsync(id);
                break;
            case TrayMenuCommand.SaveDashcam when entry.PrinterId is { } id:
                recordings.SaveDashcam(id);
                break;
            case TrayMenuCommand.StartTimelapse when entry.PrinterId is { } id:
                recordings.StartTimelapse(id);
                break;
            case TrayMenuCommand.StopTimelapse when entry.PrinterId is { } id:
                recordings.StopTimelapse(id);
                break;
            case TrayMenuCommand.PlayLastTimelapse when entry.PrinterId is { } id:
                _ = recordings.PlayLastTimelapseAsync(id);
                break;
            case TrayMenuCommand.ExportTimelapseJpeg when entry.PrinterId is { } id:
                _ = recordings.ExportTimelapseToJpegAsync(id);
                break;
            case TrayMenuCommand.CancelTimelapseJpegExport:
                recordings.CancelTimelapseExport();
                break;
            case TrayMenuCommand.CreateTimelapseFromRecording when entry.PrinterId is { } id:
                _ = recordings.CreateTimelapseFromRecordingAsync(id);
                break;
            case TrayMenuCommand.CancelTimelapseFromRecording:
                recordings.CancelTimelapseFromRecording();
                break;
        }
    }

    /// <summary>
    /// Any thread (T-146): the recording of a printer changed. Only that printer is formatted again (its submenu too), in
    /// the UI update of the next burst; nothing while the menu shows a status message.
    /// </summary>
    private void OnRecordingChanged(object? sender, string printerId)
    {
        if (printerId is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || _slots is null || !_slotById.TryGetValue(printerId, out int index))
            {
                return;
            }

            Slot slot = _slots[index];
            slot.RecordingChanged = true;
            if (_monitoringFailed || slot.Dirty)
            {
                return;
            }

            slot.Dirty = true;
            _dirtySlots.Add(index);
            ScheduleApplyLocked();
        }
    }

    /// <summary>
    /// Any thread (T-165, T-164): the JPEG export or the timelapse from a recording started, made progress or ended. Its item is
    /// in every printer's submenu (one job of each at a time), so every printer is formatted again in the UI update of the next burst; nothing while the menu shows a
    /// status message.
    /// </summary>
    private void OnTimelapseExportChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_disposed || _slots is null)
            {
                return;
            }

            bool scheduled = false;
            for (int index = 0; index < _slots.Length; index++)
            {
                Slot slot = _slots[index];
                slot.RecordingChanged = true;
                if (_monitoringFailed || slot.Dirty)
                {
                    continue;
                }

                slot.Dirty = true;
                _dirtySlots.Add(index);
                scheduled = true;
            }

            if (scheduled)
            {
                ScheduleApplyLocked();
            }
        }
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

        // A pending flip of "Diagnostics…" is applied here too; the menu is still rebuilt only once.
        Apply(notifyFixedItems: false);
        _updateEntry = UpdateEntry();
        FixedItems = CreateFixedItems();
        FixedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The offered update changed (any thread): the item is formatted again on the UI thread.</summary>
    private void OnUpdateChanged(object? sender, EventArgs e) => _dispatcher.Post(RefreshUpdateEntry);

    /// <summary>UI thread (T-058): shows, hides or updates the update item.</summary>
    private void RefreshUpdateEntry()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
        }

        TrayMenuEntry? entry = UpdateEntry();
        if (Equals(entry, _updateEntry))
        {
            return;
        }

        bool sameLayout = (entry is null) == (_updateEntry is null);
        _updateEntry = entry;
        if (sameLayout && entry is not null)
        {
            // The download started or ended: the same item with another text, in place (an open menu stays open).
            int index = UpdateIndex;
            TrayMenuEntry[] items = [.. FixedItems];
            items[index] = entry;
            FixedItems = items;
            FixedItemChanged?.Invoke(this, index);
            return;
        }

        FixedItems = CreateFixedItems();
        FixedItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// UI thread. "A new version X.Y.Z is available" while one is offered (disabled with "Downloading…" while a Velopack update
    /// downloads); its tooltip says what a click does. Plain text: the version is no menu text with an access key.
    /// </summary>
    private TrayMenuEntry? UpdateEntry()
    {
        if (_updates?.Available is not { } release)
        {
            return null;
        }

        string version = release.Version.ToString();
        if (_updates.IsInstalling)
        {
            return new TrayMenuEntry(TrayMenuCommand.Update, Format(Strings.MenuUpdateDownloading, version), IsEnabled: false);
        }

        string toolTip = _updates.Action == UpdateAction.InstallAndRestart ? Strings.MenuUpdateInstallHint : Strings.MenuUpdateOpenPageHint;
        return new TrayMenuEntry(TrayMenuCommand.Update, Format(Strings.MenuUpdateAvailable, version), IsEnabled: true, toolTip);
    }

    /// <summary>Index of the update item in <see cref="FixedItems"/> (right above "User manual").</summary>
    private const int UpdateIndex = 3;

    private static string Format(string format, string version) => string.Format(System.Globalization.CultureInfo.CurrentCulture, format, version);

    /// <summary>
    /// UI thread: the camera window of <paramref name="printerId"/> opened or closed (by the menu, its close button, the
    /// settings disabling the camera, removing the printer or quitting). Its camera item reads "Hide camera" while the
    /// window is open and "Show camera" while it is closed (2026-10-22); only that printer's items are formatted again.
    /// </summary>
    public void SetCameraWindowOpen(string printerId, bool isOpen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        if (isOpen ? !_openCameras.Add(printerId) : !_openCameras.Remove(printerId))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_slots is not null && _slotById.TryGetValue(printerId, out int index))
            {
                // The cached submenu holds the old camera item text.
                _slots[index].Submenu = null;
            }

            _layoutChanged = true;
        }

        Apply();
    }

    public void Dispose()
    {
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
        if (_images is not null)
        {
            _images.Changed -= OnImagesChanged;
        }

        if (_updates is not null)
        {
            _updates.Changed -= OnUpdateChanged;
        }

        if (_recordings is not null)
        {
            _recordings.Changed -= OnRecordingChanged;
            _recordings.TimelapseExportChanged -= OnTimelapseExportChanged;
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

    /// <summary>Index of "Diagnostics…" in <see cref="FixedItems"/>.</summary>
    private const int DiagnosticsIndex = 0;

    /// <summary>
    /// UI thread. The update item (T-058) above "User manual" (T-172, right above "About…") while a new version is offered.
    /// </summary>
    private TrayMenuEntry[] CreateFixedItems()
    {
        List<TrayMenuEntry> items =
        [
            DiagnosticsEntry(),
            new TrayMenuEntry(TrayMenuCommand.Settings, Strings.MenuSettings, _options.SettingsAvailable) { HasAccessKey = true },
            TrayMenuEntry.Separator,
        ];
        if (_updateEntry is { } update)
        {
            items.Add(update);
        }

        items.Add(new TrayMenuEntry(TrayMenuCommand.Manual, Strings.MenuManual, IsEnabled: true) { HasAccessKey = true });
        items.Add(new TrayMenuEntry(TrayMenuCommand.About, Strings.MenuAbout, IsEnabled: true) { HasAccessKey = true });
        items.Add(new TrayMenuEntry(TrayMenuCommand.Quit, Strings.MenuQuit, IsEnabled: true) { HasAccessKey = true });
        return [.. items];
    }

    /// <summary>UI thread. "Diagnostics…" has no printer: the window has its own printer combo (2026-10-21).</summary>
    private TrayMenuEntry DiagnosticsEntry() =>
        new(TrayMenuCommand.Diagnostics, Strings.MenuDiagnostics, _diagnosticsEnabled) { HasAccessKey = true };

    private static TrayMenuEntry MessageEntry(string text) => new(TrayMenuCommand.None, text, IsEnabled: false);

    /// <summary>No printer configured: the (disabled) message and "Add printer…" (T-042).</summary>
    private TrayMenuEntry[] NoPrinterEntries() =>
    [
        MessageEntry(Strings.ToolTipNoPrinter),
        new TrayMenuEntry(TrayMenuCommand.AddPrinter, Strings.MenuAddPrinter, _options.SettingsAvailable) { HasAccessKey = true },
    ];

    /// <summary>
    /// UI thread. A submenu header "Name: State – 42%" (the name shortened, the full name in its tooltip) whose items are
    /// built once per configuration and reused, so a state change only formats the header. Clicking the header only opens
    /// the submenu (T-042; before, a single printer opened its web interface).
    /// </summary>
    private TrayMenuEntry PrinterEntry(PrinterLine line)
    {
        if (line.RecordingChanged)
        {
            // The cached submenu holds the old recording items (T-146).
            line.Owner.Submenu = null;
        }

        // Enabled even when the address is invalid: the submenu shows the disabled web item with the reason.
        return new(TrayMenuCommand.None, TrayMenuFormatter.PrinterHeader(line.Name, line.Content), IsEnabled: true, TrayMenuFormatter.PrinterToolTip(line.Name), PrinterId: line.Id)
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
            TrayMenuFormatter.OpenWebText(),
            canOpenWeb,
            canOpenWeb ? null : Strings.MenuInvalidAddress,
            line.WebUri,
            line.Id)
        {
            HasAccessKey = true,
        };
        // "Diagnostics…" is in the main menu (2026-10-21), not here.
        if (!line.HasCamera)
        {
            return new TrayMenuEntryList([web]);
        }

        // T-102: "Bring camera to front" brings the open window to the front, right above "Hide camera".
        List<TrayMenuEntry> items = _options.CameraToFront && _openCameras.Contains(line.Id)
            ? [web, CameraToFrontEntry(line.Id), CameraEntry(line.Id)]
            : [web, CameraEntry(line.Id)];
        if (_recordings is { } recordings)
        {
            AddRecordingEntries(items, recordings, line.Id);
        }

        return new TrayMenuEntryList(items);
    }

    /// <summary>
    /// T-146 (user decisions 2026-11-03): after a separator, "Start recording" / "Stop recording" with one access key in both
    /// states (disabled with the reason while it starts or stops, or when this build cannot record), "Save the last N min"
    /// of a printer with a dashcam (T-148, disabled with the reason while it does not buffer), "Play last recording"
    /// (disabled until the printer has a recording) and "Open recordings folder". T-161: "Start timelapse" / "Stop timelapse"
    /// (experimental) after the recording controls, "Play last timelapse" after "Play last recording"; the folder is shared.
    /// T-165: "Export timelapse to JPEG… (experimental)" (or "Cancel JPEG export (42 %)") after "Play last timelapse".
    /// T-164: "Create timelapse from recording… (experimental)" (or "Cancel timelapse from recording (42 %)") after it.
    /// </summary>
    private static void AddRecordingEntries(List<TrayMenuEntry> items, ICameraRecordings recordings, string printerId)
    {
        CameraRecordingStatus status = recordings.GetStatus(printerId);
        RecordingControlState state = new(recordings.IsAvailable, status.Activity);
        items.Add(TrayMenuEntry.Separator);
        items.Add(new TrayMenuEntry(
            state.IsActive ? TrayMenuCommand.StopRecording : TrayMenuCommand.StartRecording,
            state.IsActive ? Strings.MenuStopRecording : Strings.MenuStartRecording,
            state.IsEnabled,
            state.DisabledReason,
            PrinterId: printerId)
        {
            HasAccessKey = true,
        });
        // T-148: "Save the last N min" right below, only for a printer with a dashcam.
        DashcamControlState dashcam = new(status.Dashcam, status.IsActive, status.DashcamMinutes);
        if (dashcam.IsShown)
        {
            items.Add(new TrayMenuEntry(TrayMenuCommand.SaveDashcam, dashcam.Text, dashcam.IsEnabled, dashcam.DisabledReason, PrinterId: printerId)
            {
                HasAccessKey = true,
            });
        }

        // T-161: the timelapse (experimental, the label is in the text) below the recording, one access key in both states.
        TimelapseControlState timelapse = TimelapseControlState.Of(recordings.IsTimelapseAvailable, status.Timelapse);
        items.Add(new TrayMenuEntry(
            timelapse.IsActive ? TrayMenuCommand.StopTimelapse : TrayMenuCommand.StartTimelapse,
            timelapse.IsActive ? Strings.MenuStopTimelapse : Strings.MenuStartTimelapse,
            timelapse.IsEnabled,
            timelapse.MenuToolTip,
            PrinterId: printerId)
        {
            HasAccessKey = true,
        });
        bool hasLast = status.LastFile is not null;
        items.Add(new TrayMenuEntry(TrayMenuCommand.PlayLastRecording, Strings.MenuPlayLastRecording, hasLast, hasLast ? null : Strings.MenuNoRecordingYet, PrinterId: printerId)
        {
            HasAccessKey = true,
        });
        bool hasLastTimelapse = status.Timelapse.LastFile is not null;
        items.Add(new TrayMenuEntry(
            TrayMenuCommand.PlayLastTimelapse,
            Strings.MenuPlayLastTimelapse,
            hasLastTimelapse,
            hasLastTimelapse ? null : Strings.MenuNoTimelapseYet,
            PrinterId: printerId)
        {
            HasAccessKey = true,
        });
        // T-165: the JPEG export of a timelapse file (experimental, the label is in the text), the same item for every printer.
        TimelapseExportControlState export = new(recordings.IsTimelapseExportAvailable, recordings.TimelapseExport);
        items.Add(new TrayMenuEntry(
            export.IsRunning ? TrayMenuCommand.CancelTimelapseJpegExport : TrayMenuCommand.ExportTimelapseJpeg,
            export.Text,
            export.IsEnabled,
            export.ToolTip,
            PrinterId: printerId)
        {
            HasAccessKey = true,
        });
        // T-164: a timelapse from a recording file (experimental, the label is in the text), the same item for every printer.
        TimelapseFromRecordingControlState fromRecording = new(recordings.IsTimelapseFromRecordingAvailable, recordings.TimelapseFromRecording);
        items.Add(new TrayMenuEntry(
            fromRecording.IsRunning ? TrayMenuCommand.CancelTimelapseFromRecording : TrayMenuCommand.CreateTimelapseFromRecording,
            fromRecording.Text,
            fromRecording.IsEnabled,
            fromRecording.ToolTip,
            PrinterId: printerId)
        {
            HasAccessKey = true,
        });
        items.Add(new TrayMenuEntry(TrayMenuCommand.OpenRecordingsFolder, Strings.MenuOpenRecordingsFolder, IsEnabled: true, PrinterId: printerId)
        {
            HasAccessKey = true,
        });
    }

    /// <summary>
    /// Only for printers with an enabled camera; absent otherwise (T-014). A plain item whose text follows the camera window:
    /// "Show camera" while it is closed, "Hide camera" while it is open (2026-10-22, replaces the check item of T-040).
    /// </summary>
    private TrayMenuEntry CameraEntry(string printerId) =>
        new(TrayMenuCommand.Camera, _openCameras.Contains(printerId) ? Strings.MenuHideCamera : Strings.MenuShowCamera, IsEnabled: true, PrinterId: printerId)
        {
            HasAccessKey = true,
        };

    /// <summary>"Bring camera to front" for an open camera window (T-102, macOS and Windows), worded unlike "Show camera" (2026-10-31) and with its own access key next to "Hide camera".</summary>
    private static TrayMenuEntry CameraToFrontEntry(string printerId) =>
        new(TrayMenuCommand.CameraToFront, Strings.MenuCameraToFront, IsEnabled: true, PrinterId: printerId)
        {
            HasAccessKey = true,
        };

    /// <summary>The printer entries of a new layout, one submenu per printer.</summary>
    private TrayMenuEntry[] LayoutEntries(List<(int Index, PrinterLine Line)>? lines) =>
        lines is null ? [] : [.. lines.Select(line => PrinterEntry(line.Line))];

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
    private void Apply() => Apply(notifyFixedItems: true);

    /// <param name="notifyFixedItems">
    /// Raise <see cref="FixedItemChanged"/> when "Diagnostics…" flips; <see langword="false"/> when the caller replaces
    /// the fixed items and rebuilds the menu anyway (language switch).
    /// </param>
    private void Apply(bool notifyFixedItems)
    {
        string? message;
        bool noPrinters;
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
                : null;
            noPrinters = message is null && _slots is { Length: 0 };

            if (message is null && !noPrinters && _slots is not null)
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
        else if (noPrinters)
        {
            Synchronize(NoPrinterEntries());
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

        // After the printer items, so that a handler sees the menu as it is now.
        bool diagnosticsEnabled = _diagnosticsAvailable && message is null && !noPrinters;
        if (diagnosticsEnabled != _diagnosticsEnabled)
        {
            _diagnosticsEnabled = diagnosticsEnabled;
            TrayMenuEntry[] items = [.. FixedItems];
            items[DiagnosticsIndex] = DiagnosticsEntry();
            FixedItems = items;
            if (notifyFixedItems)
            {
                FixedItemChanged?.Invoke(this, DiagnosticsIndex);
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
    /// <param name="HasCamera">The printer has an enabled camera: the menu offers "Camera".</param>
    /// <param name="RecordingChanged">Its recording changed since the submenu was built (T-146): the submenu is built again.</param>
    private readonly record struct PrinterLine(
        Slot Owner,
        string Id,
        string Name,
        Uri? WebUri,
        PrinterMenuContent Content,
        bool HasCamera,
        PrinterPicture? Picture,
        bool RecordingChanged);

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

        /// <summary>Its recording changed (T-146); cleared by <see cref="ToLine"/>, which hands it to the UI update.</summary>
        public bool RecordingChanged { get; set; }

        /// <summary>Under the owner's lock.</summary>
        public PrinterLine ToLine()
        {
            bool recordingChanged = RecordingChanged;
            RecordingChanged = false;
            return new(this, definition.Id, definition.Name, webUri, Content, definition.Camera is { Enabled: true, StreamUri: not null }, Picture, recordingChanged);
        }
    }
}
