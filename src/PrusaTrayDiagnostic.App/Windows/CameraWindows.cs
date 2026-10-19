using PrusaTrayDiagnostic.App.Cameras;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// The camera windows of the app (T-014): at most one per printer (a second request activates it); windows of different
/// printers may be open at the same time. Each window owns its view model, which is disposed when the window closes
/// (stopping the stream). After <see cref="Shutdown"/> (quitting) all are closed and none opens. UI thread only.
/// </summary>
internal sealed class CameraWindows
{
    private readonly Func<string, CameraViewModel?> _createViewModel;
    private readonly Dictionary<string, AuxiliaryWindowSlot> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CameraWindow> _open = new(StringComparer.OrdinalIgnoreCase);
    private bool _shutDown;

    /// <param name="createViewModel">The view model of a new window; <see langword="null"/> when the printer has no enabled camera.</param>
    public CameraWindows(Func<string, CameraViewModel?> createViewModel)
    {
        ArgumentNullException.ThrowIfNull(createViewModel);
        _createViewModel = createViewModel;
    }

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
                CameraWindow window = new() { DataContext = created };
                _open[printerId] = window;
                window.Closed += (_, _) => _open.Remove(printerId);
                // The stream runs only while the window is open: the slot disposes the view model when it closes.
                return (new AvaloniaAuxiliaryWindow(window), created);
            });
        }
        finally
        {
            if (!used)
            {
                created?.Dispose();
            }
        }

        return slot.IsOpen;
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
