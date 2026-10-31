using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Printers;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The pin of the camera window (T-117): the window button and the context menu item switch "always on top" of the current
/// mode (normal window or mini preview; full screen has none) right away and save only that option of the printer
/// (<see cref="ICameraWindowTopmostWriter"/>, which announces it to the running settings and an open settings window). A save
/// that fails or leaves the file alone switches the window back to the stored option. Saves run one after another in the order of
/// the clicks; while one is pending, settings applied meanwhile keep the switched option for that mode.
/// </summary>
public sealed partial class CameraViewModel
{
    private ICameraWindowTopmostWriter? _topmostWriter;
    private Task _topmostSaves = Task.CompletedTask;
    private int _topmostRequest;

    // The option switched in this window whose save has not finished, and the request that switched it last (UI thread).
    private (bool Topmost, int Request)? _pendingNormal;
    private (bool Topmost, int Request)? _pendingMini;

    /// <summary>Switches "always on top" of the current mode (the pin); not in full screen.</summary>
    public IRelayCommand ToggleTopmostCommand { get; private set; } = null!;

    /// <summary>Tip and accessible name of the pin: says the state ("Always on top: on" / "off").</summary>
    public string TopmostToolTip => IsTopmost ? Strings.CameraTopmostOn : Strings.CameraTopmostOff;

    /// <summary>The saves of the pin so far, in order; completes when they are done. Never faults. For tests.</summary>
    internal Task TopmostSaves => _topmostSaves;

    private void CreateTopmost(ICameraWindowTopmostWriter? topmostWriter)
    {
        _topmostWriter = topmostWriter;
        ToggleTopmostCommand = new RelayCommand(ToggleTopmost, () => !_disposed && _mode != CameraWindowMode.FullScreen);
    }

    /// <summary>The mode changed: the pin shows the option of the new mode and is off in full screen.</summary>
    private void OnTopmostModeChanged()
    {
        OnPropertyChanged(nameof(TopmostToolTip));
        ToggleTopmostCommand.NotifyCanExecuteChanged();
    }

    private void ToggleTopmost()
    {
        if (_disposed || _mode == CameraWindowMode.FullScreen)
        {
            return;
        }

        CameraWindowMode mode = _mode;
        bool topmost = !IsTopmost;
        int request = ++_topmostRequest;
        SetPending(mode, (topmost, request));
        // The window follows IsTopmost right away (z-order, taskbar button, Dock icon and Spaces, T-102).
        SetDefaults(_defaults.WithTopmost(mode, topmost));
        Task previous = _topmostSaves;
        _topmostSaves = SaveTopmostAsync(previous, mode, topmost, request);
    }

    /// <summary>UI thread, after <paramref name="previous"/>; never faults.</summary>
    private async Task SaveTopmostAsync(Task previous, CameraWindowMode mode, bool topmost, int request)
    {
        await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        bool saved = false;
        try
        {
            if (_topmostWriter is null)
            {
                LogTopmostNotSaved(PrinterId, mode);
            }
            else
            {
                saved = await _topmostWriter.SaveAsync(PrinterId, mode, topmost, CancellationToken.None).ConfigureAwait(true);
                if (!saved)
                {
                    // Left alone: no file, no such printer camera, a newer settings format or a window value of another type
                    // (a file that holds the value already counts as saved).
                    LogTopmostNotSaved(PrinterId, mode);
                }
            }
        }
        catch (Exception ex)
        {
            // Any failure (the file, no data folder, a bug) switches back; it must not end the app from the command.
            LogTopmostSaveFailed(ex, PrinterId, mode);
        }

        if (_disposed || Pending(mode) is not { } pending || pending.Request != request)
        {
            // Closed meanwhile, or clicked again: the newer save decides.
            return;
        }

        SetPending(mode, null);
        if (!saved)
        {
            // Back to the stored option (what monitoring runs with), not just the previous value of this window.
            bool stored = StoredWindow() is { } window ? window.IsTopmost(mode) : !topmost;
            SetDefaults(_defaults.WithTopmost(mode, stored));
        }
    }

    /// <summary>Settings applied while a save of the pin is pending keep the switched option of that mode.</summary>
    private CameraWindowDefaults WithPendingTopmost(CameraWindowDefaults defaults)
    {
        if (_pendingNormal is { } normal)
        {
            defaults = defaults.WithTopmost(CameraWindowMode.Normal, normal.Topmost);
        }

        if (_pendingMini is { } mini)
        {
            defaults = defaults.WithTopmost(CameraWindowMode.Mini, mini.Topmost);
        }

        return defaults;
    }

    private (bool Topmost, int Request)? Pending(CameraWindowMode mode) => mode == CameraWindowMode.Normal ? _pendingNormal : _pendingMini;

    private void SetPending(CameraWindowMode mode, (bool Topmost, int Request)? pending)
    {
        if (mode == CameraWindowMode.Normal)
        {
            _pendingNormal = pending;
        }
        else
        {
            _pendingMini = pending;
        }
    }

    /// <summary>The window defaults of this printer in the settings monitoring runs with; <see langword="null"/> when it is gone.</summary>
    private CameraWindowDefaults? StoredWindow()
    {
        foreach (PrinterDefinition printer in _printers.Current.Printers ?? [])
        {
            if (string.Equals(printer.Id, PrinterId, StringComparison.OrdinalIgnoreCase))
            {
                return printer.Camera.Window;
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The always-on-top option of the camera window of printer '{PrinterId}' ({Mode}) was not saved; the window switched back.")]
    private partial void LogTopmostNotSaved(string printerId, CameraWindowMode mode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Saving the always-on-top option of the camera window of printer '{PrinterId}' ({Mode}) failed; the window switched back.")]
    private partial void LogTopmostSaveFailed(Exception exception, string printerId, CameraWindowMode mode);
}
