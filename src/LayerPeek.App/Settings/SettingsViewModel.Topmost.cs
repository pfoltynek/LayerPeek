using LayerPeek.Core.Settings;

namespace LayerPeek.App.Settings;

/// <summary>
/// "Always on top" switched by the pin of a camera window (T-117), taken over like a layout saved by the layout mode
/// (<see cref="OnLayoutSaved"/>): the printer takes only that option of that mode as its saved and edited value; its other
/// unsaved changes and those of the window stay.
/// </summary>
public sealed partial class SettingsViewModel
{
    // Options saved by a camera window that this window has not taken over yet.
    private readonly AnnouncedCameraTopmost _announcedTopmost = new();
    private ICameraWindowTopmostWriter? _topmostWriter;

    private void InitializeTopmost(ICameraWindowTopmostWriter? writer)
    {
        if (writer is not null)
        {
            _topmostWriter = writer;
            writer.TopmostSaved += OnTopmostSaved;
        }
    }

    private void DisposeTopmost()
    {
        if (_topmostWriter is { } writer)
        {
            writer.TopmostSaved -= OnTopmostSaved;
            _topmostWriter = null;
        }
    }

    /// <summary>
    /// The writer's thread: the pin of a camera window saved an option. Until the window takes it over on the UI thread, it
    /// waits in <see cref="_announcedTopmost"/>: a save meanwhile keeps it for a printer whose option of that mode the window
    /// did not change, and the window takes it after loading or saving. A printer removed in the window or not saved yet
    /// ignores it.
    /// </summary>
    private void OnTopmostSaved(object? sender, CameraWindowTopmostSavedEventArgs saved)
    {
        if (saved is null)
        {
            return;
        }

        CameraTopmostChoice choice = new(saved.PrinterId, saved.Mode, saved.Topmost);
        _announcedTopmost.Add(choice);
        _uiDispatcher.Post(() => TakeSavedTopmost(choice));
    }

    /// <summary>
    /// UI thread: see <see cref="OnTopmostSaved"/>. While loading or saving the option keeps waiting (taken over afterwards);
    /// an option already taken over, saved by the window meanwhile or replaced by a newer one is ignored.
    /// </summary>
    internal void TakeSavedTopmost(CameraTopmostChoice choice)
    {
        if (_disposed || !_loaded || _isSaving || !_announcedTopmost.TryTake(choice))
        {
            return;
        }

        TakeOverTopmost(choice);
        UpdateDirty();
    }

    /// <summary>The printer takes <paramref name="choice"/> as its saved and edited option (no unsaved change).</summary>
    private void TakeOverTopmost(CameraTopmostChoice choice)
    {
        PrinterEditorViewModel? printer = Printers.FirstOrDefault(candidate => !candidate.IsNew && string.Equals(candidate.Id, choice.PrinterId, StringComparison.OrdinalIgnoreCase));
        if (printer is null)
        {
            return;
        }

        _baseline = _baseline.WithCameraWindowTopmost(printer.Id, choice.Mode, choice.Topmost);
        printer.TakeSavedTopmost(choice.Mode, choice.Topmost);
    }

    /// <summary>After loading or a failed save: every option saved by a camera window meanwhile. The caller updates the dirty state.</summary>
    private void TakeWaitingTopmost()
    {
        foreach (CameraTopmostChoice choice in _announcedTopmost.TakeAll())
        {
            TakeOverTopmost(choice);
        }
    }

    /// <summary>
    /// Right after a successful save, with <see cref="_baseline"/> already the saved settings, like
    /// <see cref="TakeLayoutsAfterSave"/>: the printers take the options the store kept instead of the window's, and the
    /// options saved by a camera window after the store read them. The caller updates the dirty state.
    /// </summary>
    private void TakeTopmostAfterSave(AnnouncedCameraTopmost.Reader latest, IReadOnlyList<CameraTopmostChoice>? kept)
    {
        _announcedTopmost.Forget(latest);
        foreach (CameraTopmostChoice choice in kept ?? [])
        {
            TakeOverTopmost(choice);
        }

        TakeWaitingTopmost();
    }
}
