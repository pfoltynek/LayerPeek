using System.ComponentModel;

using Avalonia.Controls;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Layout;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Printers;

namespace LayerPeek.App.Windows;

/// <summary>
/// The camera windows of the app (T-014): at most one per printer (a second request activates it); windows of different
/// printers may be open at the same time. Each window owns its view model, which is disposed when the window closes
/// (stopping the stream). After <see cref="Shutdown"/> (quitting) all are closed and none opens. UI thread only.
/// </summary>
internal sealed class CameraWindows : ICameraWindowPlacer, ICameraLayoutModeLauncher, ICameraWindowAutoTarget
{
    private readonly Func<string, PrinterDefinition?, CameraViewModel?> _createViewModel;
    private readonly IScreenCatalog? _screens;
    private readonly Func<CameraWindow, WindowChrome?>? _measureChrome;
    private readonly Func<CameraWindow, double, WindowChrome?>? _predictChrome;
    private readonly Func<Window, IDisposable>? _track;
    private readonly Action? _bringAppToFront;
    private readonly IWindowActivator? _activator;
    private readonly Dictionary<string, AuxiliaryWindowSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CameraWindow> _open = new(StringComparer.OrdinalIgnoreCase);

    // T-151: windows being closed by CloseAutomatically, until they report closing.
    private readonly HashSet<CameraWindow> _closingAutomatically = new(ReferenceEqualityComparer.Instance);
    private bool _shutDown;

    /// <param name="createViewModel">
    /// The view model of a new window for the printer id: from the given saved printer (Apply, T-040), otherwise from the
    /// monitored settings; <see langword="null"/> when the printer has no enabled camera.
    /// </param>
    /// <param name="screens">Monitors for tests; <see langword="null"/>: each window asks Avalonia (<see cref="AvaloniaScreenCatalog"/>).</param>
    /// <param name="measureChrome">Frame measurement for tests (<see cref="CameraWindow.ChromeMeasurement"/>); <see langword="null"/>: the real one.</param>
    /// <param name="track">Tracks each new window before it is shown (T-043: taskbar properties, recently used windows).</param>
    /// <param name="predictChrome">Frame prediction for tests (<see cref="CameraWindow.ChromePrediction"/>); <see langword="null"/>: the real one.</param>
    /// <param name="bringAppToFront">
    /// Activates the app before a camera window is activated (macOS, T-019, user decision 2026-10-29: like the settings,
    /// diagnostics and About windows, <see cref="AvaloniaAuxiliaryWindow"/>); not for a window Apply shows without
    /// activating it. <see langword="null"/>: nothing.
    /// </param>
    /// <param name="activator">
    /// Activates a camera window (T-119: "Bring camera to front" on Windows past the foreground lock,
    /// <see cref="WindowsWindowActivator"/>); <see langword="null"/>: <see cref="Window.Activate"/>.
    /// </param>
    public CameraWindows(
        Func<string, PrinterDefinition?, CameraViewModel?> createViewModel,
        IScreenCatalog? screens = null,
        Func<CameraWindow, WindowChrome?>? measureChrome = null,
        Func<Window, IDisposable>? track = null,
        Func<CameraWindow, double, WindowChrome?>? predictChrome = null,
        Action? bringAppToFront = null,
        IWindowActivator? activator = null)
    {
        _bringAppToFront = bringAppToFront;
        _activator = activator;
        _measureChrome = measureChrome;
        _predictChrome = predictChrome;
        _track = track;
        ArgumentNullException.ThrowIfNull(createViewModel);
        _createViewModel = createViewModel;
        _screens = screens;
    }

    /// <summary>
    /// A camera window opened or closed (any way: menu, close button, settings, quitting); UI thread. The tray menu shows
    /// "Hide camera" for the printer while its window is open (T-040).
    /// </summary>
    public event EventHandler<CameraWindowOpenChangedEventArgs>? OpenChanged;

    /// <inheritdoc/>
    public event EventHandler<CameraWindowCloseDeferralChangedEventArgs>? CloseDeferralChanged;

    /// <summary>The open windows by printer id (for tests and diagnostics).</summary>
    public IReadOnlyDictionary<string, CameraWindow> OpenWindows => _open;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> OpenPrinterIds => _open.Keys;

    /// <summary>Opens the camera window of <paramref name="printerId"/>, or activates it when it is open.</summary>
    /// <returns>Whether a window of the printer is open now (not when it has no enabled camera, or after <see cref="Shutdown"/>).</returns>
    public bool Show(string printerId) => Show(printerId, saved: null);

    /// <inheritdoc/>
    public bool ShowAutomatically(string printerId) => Show(printerId, saved: null, activate: false);

    /// <inheritdoc/>
    public bool CloseAutomatically(string printerId)
    {
        if (_shutDown || string.IsNullOrWhiteSpace(printerId) || !_open.TryGetValue(printerId, out CameraWindow? open))
        {
            return false;
        }

        // The same close as the close button (placement remembered, stream stopped), reported as automatic.
        _ = _closingAutomatically.Add(open);
        open.Close();
        return !_open.ContainsKey(printerId);
    }

    /// <inheritdoc/>
    public bool IsCloseDeferred(string printerId) =>
        !string.IsNullOrWhiteSpace(printerId)
        && _open.TryGetValue(printerId, out CameraWindow? open)
        && open.DataContext is CameraViewModel { DefersAutoClose: true };

    /// <inheritdoc/>
    public bool Start(string printerId, CameraOverlayLayout? startingLayout)
    {
        if (_shutDown || string.IsNullOrWhiteSpace(printerId))
        {
            return false;
        }

        if (_open.TryGetValue(printerId, out CameraWindow? open) && open.DataContext is CameraViewModel viewModel)
        {
            // Into the normal window (and the layout mode, unless it is on already), then activated.
            return viewModel.StartLayoutModeFromOutside(startingLayout) && Show(printerId) && viewModel.IsLayoutModeActive;
        }

        bool started = false;
        // A new window: in the layout mode before it is placed and shown, so that it opens as the normal window.
        bool shown = Show(printerId, saved: null, prepare: created => started = created.StartLayoutModeFromOutside(startingLayout));
        return shown && started;
    }

    /// <param name="saved">
    /// Apply (T-040): a new window comes from this saved printer and is placed by its window settings, like an open window
    /// (<see cref="CameraViewModel.ApplyWindowSettings"/>), and shown without taking the focus from the settings window
    /// (user decision 2026-10-21); <see langword="null"/>: the monitored settings, activated.
    /// </param>
    /// <param name="prepare">Called with the view model of a new window before the window gets it (T-091: the layout mode).</param>
    /// <param name="activate">
    /// Whether the window is activated; <see langword="null"/>: unless <paramref name="saved"/> is given. <see langword="false"/>
    /// for the automatic open (T-151, user decision 2026-11-03 #1): shown without taking the focus, and without activating
    /// the app on macOS.
    /// </param>
    private bool Show(string printerId, PrinterDefinition? saved, Action<CameraViewModel>? prepare = null, bool? activate = null)
    {
        if (_shutDown || string.IsNullOrWhiteSpace(printerId))
        {
            return false;
        }

        if (!_slots.TryGetValue(printerId, out AuxiliaryWindowSlot? slot))
        {
            slot = new AuxiliaryWindowSlot();
            _slots.Add(printerId, slot);
        }

        // Apply keeps the focus in the settings window. Exception (user decision 2026-10-21): full screen takes it when the
        // window switches after opening (on Windows Avalonia activates a shown window whose state changes), so that Esc
        // and F11 reach the camera window that covers the settings.
        bool activates = activate ?? saved is null;
        CameraViewModel? created = null;
        if (!slot.IsOpen)
        {
            // Null when the camera was disabled meanwhile (a stale menu item): nothing opens.
            created = _createViewModel(printerId, saved);
            if (created is null)
            {
                return false;
            }

            if (saved is not null)
            {
                // As for an open window, but before the window gets the view model: it is placed from it before it
                // is shown (no visible jump).
                created.ApplyWindowSettings(saved.Camera.Window);
            }

            prepare?.Invoke(created);
        }

        bool used = false;
        try
        {
            slot.ShowOrActivate(() =>
            {
                used = true;
                // The monitors first: the window places itself when it gets its view model.
                CameraWindow window = _screens is { } screens
                    ? new() { ChromeMeasurement = _measureChrome, ChromePrediction = _predictChrome, ScreenCatalog = screens, ShowActivated = activates, DataContext = created }
                    : new() { ChromeMeasurement = _measureChrome, ChromePrediction = _predictChrome, ShowActivated = activates, DataContext = created };
                _open[printerId] = window;
                CameraViewModel? viewModel = created;
                PropertyChangedEventHandler? deferral = viewModel is null ? null : (_, e) =>
                {
                    if (e.PropertyName == nameof(CameraViewModel.DefersAutoClose))
                    {
                        CloseDeferralChanged?.Invoke(this, new CameraWindowCloseDeferralChangedEventArgs(printerId, viewModel.DefersAutoClose));
                    }
                };
                if (viewModel is not null)
                {
                    viewModel.PropertyChanged += deferral;
                }

                window.Closed += (_, _) =>
                {
                    if (viewModel is not null)
                    {
                        viewModel.PropertyChanged -= deferral;
                    }

                    if (_open.TryGetValue(printerId, out CameraWindow? current) && ReferenceEquals(current, window))
                    {
                        _open.Remove(printerId);
                    }

                    bool automatic = _closingAutomatically.Remove(window);
                    OpenChanged?.Invoke(this, new CameraWindowOpenChangedEventArgs(printerId, isOpen: false, automatic));
                };
                // The stream runs only while the window is open: the slot disposes the view model when it closes.
                return (new AvaloniaAuxiliaryWindow(window, _track, _bringAppToFront, _activator), created);
            }, activate: activates);
        }
        finally
        {
            if (!used)
            {
                created?.Dispose();
            }
        }

        if (used && slot.IsOpen)
        {
            OpenChanged?.Invoke(this, new CameraWindowOpenChangedEventArgs(printerId, isOpen: true));
        }

        return slot.IsOpen;
    }

    /// <summary>
    /// The "Show camera" / "Hide camera" item of the tray menu (T-040): closes the open window of <paramref name="printerId"/> (like its close
    /// button: the stream stops and its placement is remembered), otherwise opens it like <see cref="Show"/>.
    /// </summary>
    /// <returns>Whether a window of the printer is open now.</returns>
    public bool Toggle(string printerId)
    {
        if (!_shutDown && !string.IsNullOrWhiteSpace(printerId) && _open.TryGetValue(printerId, out CameraWindow? open))
        {
            open.Close();
            return _open.ContainsKey(printerId);
        }

        return Show(printerId);
    }

    /// <summary>
    /// "Bring camera to front" for an open window (T-102, macOS and Windows): restores a minimized window (to full screen when it
    /// was), then activates the app (macOS) and the window like <see cref="Show"/>, also a window on top (it has no Dock
    /// icon or taskbar button to come back by); a window that closed meanwhile (a stale menu item) opens.
    /// </summary>
    /// <returns>Whether a window of the printer is open now.</returns>
    public bool BringToFront(string printerId)
    {
        if (!_shutDown
            && !string.IsNullOrWhiteSpace(printerId)
            && _open.TryGetValue(printerId, out CameraWindow? open)
            && open.WindowState == WindowState.Minimized)
        {
            open.WindowState = open.DataContext is CameraViewModel { Mode: CameraWindowMode.FullScreen } ? WindowState.FullScreen : WindowState.Normal;
        }

        return Show(printerId);
    }

    /// <inheritdoc/>
    public bool ApplySettings(PrinterDefinition printer)
    {
        ArgumentNullException.ThrowIfNull(printer);
        if (_shutDown || !printer.Camera.Enabled)
        {
            return false;
        }

        if (!_open.TryGetValue(printer.Id, out CameraWindow? open))
        {
            return Show(printer.Id, printer);
        }

        if (open.DataContext is not CameraViewModel viewModel)
        {
            return false;
        }

        viewModel.ApplyWindowSettings(printer.Camera.Window);
        return true;
    }

    /// <summary>The language changed (T-029): every open camera window formats its texts again.</summary>
    public void RefreshTexts()
    {
        foreach (AuxiliaryWindowSlot slot in _slots.Values)
        {
            slot.RefreshTexts();
        }
    }

    /// <summary>Closes every window (stopping its stream) and refuses new ones. Safe to call repeatedly.</summary>
    public void Shutdown()
    {
        _shutDown = true;
        foreach (AuxiliaryWindowSlot slot in _slots.Values)
        {
            slot.Shutdown();
        }

        _open.Clear();
        _closingAutomatically.Clear();
    }
}

/// <summary>The camera window of a printer opened or closed (T-040).</summary>
internal sealed class CameraWindowOpenChangedEventArgs : EventArgs
{
    public CameraWindowOpenChangedEventArgs(string printerId, bool isOpen, bool automatic = false)
    {
        PrinterId = printerId;
        IsOpen = isOpen;
        Automatic = automatic;
    }

    public string PrinterId { get; }

    /// <summary>Open now (otherwise closed).</summary>
    public bool IsOpen { get; }

    /// <summary>
    /// Closed by <see cref="CameraWindows.CloseAutomatically"/> (T-151: the printer is off); <see langword="false"/> for every
    /// other close (the user, settings, quitting) and for opening.
    /// </summary>
    public bool Automatic { get; }
}
