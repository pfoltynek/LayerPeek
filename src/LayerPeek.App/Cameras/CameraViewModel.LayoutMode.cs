using System.ComponentModel;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using LayerPeek.App.Cameras.Layout;
using LayerPeek.App.Resources;
using LayerPeek.Core.Cameras;
using LayerPeek.Core.Cameras.Overlay;
using LayerPeek.Core.Settings;

namespace LayerPeek.App.Cameras;

/// <summary>
/// The layout mode of the camera window (T-091): available only in the normal window and only when it is at least
/// <see cref="CameraLayoutSpace.MinWidth"/> × <see cref="CameraLayoutSpace.MinHeight"/> (T-076 Q4); a switch to the mini
/// preview or full screen discards the edit first. Started from outside (the settings window, T-092) it switches the window
/// to the normal window and asks the window to make room temporarily (<see cref="IsLayoutSpaceRequested"/>).
/// </summary>
public sealed partial class CameraViewModel
{
    private CameraLayoutModeViewModel? _layoutMode;
    private RelayCommand? _editLayoutCommand;
    private double _clientWidth;
    private double _clientHeight;
    private bool _clientSizeKnown;
    private bool _layoutSpaceRequested;

    /// <summary>The layout mode of this window.</summary>
    public CameraLayoutModeViewModel LayoutMode => _layoutMode!;

    /// <summary>The layout mode is on (the window buttons and the context menu are hidden meanwhile).</summary>
    public bool IsLayoutModeActive => _layoutMode?.IsActive ?? false;

    /// <summary>
    /// The window is big enough for the layout mode (until the window reports its size, it is assumed to be).
    /// </summary>
    public bool HasLayoutRoom => !_clientSizeKnown || CameraLayoutSpace.Fits(_clientWidth, _clientHeight);

    /// <summary>"Edit layout" can be used now: the normal window, big enough, the mode not on yet.</summary>
    public bool CanEditLayout => IsNormal && !IsLayoutModeActive && HasLayoutRoom;

    /// <summary>The tip (and accessible name) of the layout button and menu item: why it is disabled in a small window.</summary>
    public string LayoutToolTip => HasLayoutRoom ? Strings.CameraLayoutEdit : Strings.CameraLayoutTooSmall;

    /// <summary>The window button and the context menu item "Edit layout".</summary>
    public IRelayCommand EditLayoutCommand => _editLayoutCommand ??= new RelayCommand(EditLayout, () => CanEditLayout);

    /// <summary>
    /// The layout mode was started from outside and the window should make room for it if it is smaller than the mode
    /// needs (enlarge it temporarily, T-091 Q5); the window confirms with <see cref="OnLayoutSpaceProvided"/>.
    /// </summary>
    internal bool IsLayoutSpaceRequested => _layoutSpaceRequested;

    /// <summary>The client size of the window (DIP), reported by the window whenever it changes.</summary>
    internal void OnClientSizeChanged(double width, double height)
    {
        if (_disposed || (_clientSizeKnown && width == _clientWidth && height == _clientHeight))
        {
            return;
        }

        bool hadRoom = HasLayoutRoom;
        _clientWidth = width;
        _clientHeight = height;
        _clientSizeKnown = true;
        if (hadRoom != HasLayoutRoom)
        {
            OnPropertyChanged(nameof(HasLayoutRoom));
            OnPropertyChanged(nameof(LayoutToolTip));
            OnLayoutAvailabilityChanged();
        }
    }

    /// <summary>
    /// The layout mode started from outside (<see cref="ICameraLayoutModeLauncher"/>): the window switches to the normal
    /// window (and stays there afterwards, Q2), the mode starts with <paramref name="startingLayout"/> (or the saved layout)
    /// and the window is asked to make room. While the mode is on already, the edit goes on unchanged (Q3).
    /// </summary>
    /// <returns>Whether the layout mode is on.</returns>
    internal bool StartLayoutModeFromOutside(CameraOverlayLayout? startingLayout)
    {
        if (_disposed)
        {
            return false;
        }

        if (IsLayoutModeActive)
        {
            return true;
        }

        Mode = CameraWindowMode.Normal;
        SetLayoutSpaceRequested(true);
        if (!LayoutMode.Start(startingLayout))
        {
            SetLayoutSpaceRequested(false);
            return false;
        }

        return true;
    }

    /// <summary>The window made room for the layout mode (or found it big enough).</summary>
    internal void OnLayoutSpaceProvided() => SetLayoutSpaceRequested(false);

    private void CreateLayoutMode(ICameraOverlayLayoutWriter? layoutWriter, ILogger logger)
    {
        _layoutMode = new CameraLayoutModeViewModel(PrinterId, Overlay, layoutWriter, logger);
        _layoutMode.PropertyChanged += OnLayoutModePropertyChanged;
        _layoutMode.Ended += OnLayoutModeEnded;
    }

    private void DisposeLayoutMode()
    {
        if (_layoutMode is not { } layoutMode)
        {
            return;
        }

        layoutMode.PropertyChanged -= OnLayoutModePropertyChanged;
        layoutMode.Ended -= OnLayoutModeEnded;
        layoutMode.Dispose();
    }

    /// <summary>Before the mode changes: anything but the normal window discards the edit first (T-091).</summary>
    private void LeaveLayoutModeFor(CameraWindowMode mode)
    {
        if (mode != CameraWindowMode.Normal && _layoutMode is { IsActive: true } layoutMode)
        {
            layoutMode.Abandon();
        }
    }

    private void EditLayout()
    {
        if (CanEditLayout)
        {
            LayoutMode.Start();
        }
    }

    private void OnLayoutAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanEditLayout));
        _editLayoutCommand?.NotifyCanExecuteChanged();
    }

    private void OnLayoutModePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraLayoutModeViewModel.IsActive))
        {
            OnPropertyChanged(nameof(IsLayoutModeActive));
            OnLayoutAvailabilityChanged();
        }
    }

    private void OnLayoutModeEnded(object? sender, CameraLayoutEndedEventArgs e) => SetLayoutSpaceRequested(false);

    private void SetLayoutSpaceRequested(bool requested)
    {
        if (_layoutSpaceRequested != requested)
        {
            _layoutSpaceRequested = requested;
            OnPropertyChanged(nameof(IsLayoutSpaceRequested));
        }
    }
}
