using Avalonia.Controls;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// The camera windows of the app (T-014): at most one per printer (a second request activates it); windows of different
/// printers may be open at the same time. Each window owns its view model, which is disposed when the window closes
/// (stopping the stream). After <see cref="Shutdown"/> (quitting) all are closed and none opens. UI thread only.
/// </summary>
internal sealed class CameraWindows : ICameraWindowPlacer
{
    private readonly Func<string, CameraViewModel?> _createViewModel;
    private readonly IScreenCatalog? _screens;
    private readonly Func<CameraWindow, WindowChrome?>? _measureChrome;
    private readonly Func<Window, IDisposable>? _track;
    private readonly Dictionary<string, AuxiliaryWindowSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CameraWindow> _open = new(StringComparer.OrdinalIgnoreCase);
    private bool _shutDown;

    /// <param name="createViewModel">The view model of a new window; <see langword="null"/> when the printer has no enabled camera.</param>
    /// <param name="screens">Monitors for tests; <see langword="null"/>: each window asks Avalonia (<see cref="AvaloniaScreenCatalog"/>).</param>
    /// <param name="measureChrome">Frame measurement for tests (<see cref="CameraWindow.ChromeMeasurement"/>); <see langword="null"/>: the real one.</param>
    /// <param name="track">Tracks each new window before it is shown (T-043: taskbar properties, recently used windows).</param>
    public CameraWindows(
        Func<string, CameraViewModel?> createViewModel,
        IScreenCatalog? screens = null,
        Func<CameraWindow, WindowChrome?>? measureChrome = null,
        Func<Window, IDisposable>? track = null)
    {
        _measureChrome = measureChrome;
        _track = track;
        ArgumentNullException.ThrowIfNull(createViewModel);
        _createViewModel = createViewModel;
        _screens = screens;
    }

    /// <summary>
    /// A camera window opened or closed (any way: menu, close button, settings, quitting); UI thread. The tray menu checks
    /// the printer's "Camera" item while its window is open (T-040).
    /// </summary>
    public event EventHandler<CameraWindowOpenChangedEventArgs>? OpenChanged;

    /// <summary>The open windows by printer id (for tests and diagnostics).</summary>
    public IReadOnlyDictionary<string, CameraWindow> OpenWindows => _open;

    /// <summary>Opens the camera window of <paramref name="printerId"/>, or activates it when it is open.</summary>
    /// <returns>Whether a window of the printer is open now (not when it has no enabled camera, or after <see cref="Shutdown"/>).</returns>
    public bool Show(string printerId)
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

        CameraViewModel? created = null;
        if (!slot.IsOpen)
        {
            // Null when the camera was disabled meanwhile (a stale menu item): nothing opens.
            created = _createViewModel(printerId);
            if (created is null)
            {
                return false;
            }
        }

        bool used = false;
        try
        {
            slot.ShowOrActivate(() =>
            {
                used = true;
                // The monitors first: the window places itself when it gets its view model.
                CameraWindow window = _screens is { } screens
                    ? new() { ChromeMeasurement = _measureChrome, ScreenCatalog = screens, DataContext = created }
                    : new() { ChromeMeasurement = _measureChrome, DataContext = created };
                _open[printerId] = window;
                window.Closed += (_, _) =>
                {
                    if (_open.TryGetValue(printerId, out CameraWindow? current) && ReferenceEquals(current, window))
                    {
                        _open.Remove(printerId);
                    }

                    OpenChanged?.Invoke(this, new CameraWindowOpenChangedEventArgs(printerId, isOpen: false));
                };
                // The stream runs only while the window is open: the slot disposes the view model when it closes.
                return (new AvaloniaAuxiliaryWindow(window, _track), created);
            });
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
    /// The "Camera" item of the tray menu (T-040): closes the open window of <paramref name="printerId"/> (like its close
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

    /// <inheritdoc/>
    public bool ApplySettings(string printerId, CameraWindowDefaults window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_shutDown || string.IsNullOrWhiteSpace(printerId)
            || !_open.TryGetValue(printerId, out CameraWindow? open) || open.DataContext is not CameraViewModel viewModel)
        {
            return false;
        }

        viewModel.ApplyWindowSettings(window);
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
    }
}

/// <summary>The camera window of a printer opened or closed (T-040).</summary>
internal sealed class CameraWindowOpenChangedEventArgs : EventArgs
{
    public CameraWindowOpenChangedEventArgs(string printerId, bool isOpen)
    {
        PrinterId = printerId;
        IsOpen = isOpen;
    }

    public string PrinterId { get; }

    /// <summary>Open now (otherwise closed).</summary>
    public bool IsOpen { get; }
}
