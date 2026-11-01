using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using LayerPeek.App.Cameras;
using LayerPeek.App.Cameras.Layout;
using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Windows;

/// <summary>
/// The layout mode in the camera window (T-091): the window reports its size (the mode needs 480 × 300), focuses the first
/// card when the mode starts, makes room for a mode started from the settings window and gives the room back afterwards,
/// opens the context menu only where the mode is available, and passes Esc to the mode when the focus is elsewhere.
/// </summary>
/// <remarks>
/// Temporary room (user decisions 2026-10-25, Q5 and Q1): a normal window smaller than 480 × 300 grows to 640 × 360 at the
/// same place, moved into the work area when it would leave it (<see cref="CameraLayoutSpace.Enlarged"/>); when the mode
/// ends (Done, Cancel / Esc, a mode switch, which discards the edit first while the window is still the normal window) the
/// window gets its position and size back. Meanwhile the window remembers (and <c>camera-windows.json</c> stores) the
/// placement from before, never the temporary one. If the OS switched the window to full screen itself, the size comes
/// back when the window is the normal window again.
/// </remarks>
public sealed partial class CameraWindow
{
    // Where the normal window was before the layout mode enlarged it; null while it was not enlarged.
    private CameraWindowPlacement? _beforeLayoutSpace;

    // The place just given back, until the window reports that size (a platform may resize asynchronously): what the window
    // remembers meanwhile, e.g. when a switch to the mini preview ended the mode.
    private CameraWindowPlacement? _restoredLayoutSpace;
    private bool _layoutInputHooked;
    private bool _lastInputFromKeyboard;

    /// <summary>The layout mode's view (zones, cards, toolbar, popover).</summary>
    internal CameraLayoutOverlay LayoutModeView => LayoutOverlay;

    /// <summary>The window is enlarged for the layout mode (for tests).</summary>
    internal bool HasTemporaryLayoutSpace => _beforeLayoutSpace is not null;

    private void AttachLayoutMode(CameraViewModel viewModel)
    {
        HookLayoutInput();
        viewModel.PropertyChanged += OnLayoutPropertyChanged;
        viewModel.LayoutMode.Ended += OnLayoutModeEnded;
        viewModel.OnClientSizeChanged(ClientSize.Width, ClientSize.Height);
    }

    private void DetachLayoutMode(CameraViewModel viewModel)
    {
        viewModel.PropertyChanged -= OnLayoutPropertyChanged;
        viewModel.LayoutMode.Ended -= OnLayoutModeEnded;
    }

    private void HookLayoutInput()
    {
        if (_layoutInputHooked)
        {
            return;
        }

        _layoutInputHooked = true;
        AddHandler(KeyDownEvent, OnLayoutKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnLayoutPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        SizeChanged += (_, _) =>
        {
            if (_restoredLayoutSpace is { } restored && ClientSize == PlacedSize(restored))
            {
                _restoredLayoutSpace = null;
            }

            _viewModel?.OnClientSizeChanged(ClientSize.Width, ClientSize.Height);
        };
        Opened += (_, _) =>
        {
            _viewModel?.OnClientSizeChanged(ClientSize.Width, ClientSize.Height);
            if (_viewModel is { IsLayoutModeActive: true })
            {
                // Opened by the settings window right into the layout mode.
                EnsureLayoutSpace();
                Dispatcher.UIThread.Post(() => LayoutOverlay.FocusFirst(fromKeyboard: true));
            }
        };
    }

    private void OnLayoutPointerPressed(object? sender, PointerPressedEventArgs e) => _lastInputFromKeyboard = false;

    /// <summary>
    /// Tunnel: notes that the keyboard was used (the mode then shows the focus ring on the first card); Esc in the mode with
    /// the focus outside it goes to the mode (with the focus inside, the mode's own handler sees it).
    /// </summary>
    private void OnLayoutKeyDown(object? sender, KeyEventArgs e)
    {
        _lastInputFromKeyboard = true;
        if (e.Key == Key.Escape && !e.Handled && _viewModel is { IsLayoutModeActive: true } && !LayoutOverlay.Root.IsKeyboardFocusWithin)
        {
            e.Handled = LayoutOverlay.HandleEscape();
        }
    }

    /// <summary>
    /// The context menu (T-076 5.1) opens in the normal window outside the layout mode and, with only "Always on top" (T-117),
    /// in the mini preview; never in full screen.
    /// </summary>
    private void OnContextMenuOpening(object? sender, CancelEventArgs e) =>
        e.Cancel = _viewModel is not ({ IsNormal: true, IsLayoutModeActive: false } or { IsMini: true });

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CameraViewModel.IsLayoutModeActive) when _viewModel is { IsLayoutModeActive: true }:
                EnsureLayoutSpace();
                bool fromKeyboard = _lastInputFromKeyboard;
                // After the bindings showed the mode.
                Dispatcher.UIThread.Post(() => LayoutOverlay.FocusFirst(fromKeyboard));
                break;
            case nameof(CameraViewModel.IsLayoutSpaceRequested):
                EnsureLayoutSpace();
                break;
            case nameof(CameraViewModel.Mode):
                // After the window applied the mode: a size kept from an edit ended in full screen comes back now.
                RestoreLayoutSpace();
                break;
        }
    }

    private void OnLayoutModeEnded(object? sender, CameraLayoutEndedEventArgs e) => RestoreLayoutSpace();

    /// <summary>A layout mode started from outside gets room (Q5): a smaller normal window is enlarged for its time.</summary>
    private void EnsureLayoutSpace()
    {
        if (!_opened || _viewModel is not { IsLayoutModeActive: true, IsLayoutSpaceRequested: true } viewModel)
        {
            return;
        }

        if (WindowState == WindowState.Normal && _appliedMode == CameraWindowMode.Normal && _beforeLayoutSpace is null)
        {
            CameraWindowPlacement current = CurrentPlacement();
            if (CameraLayoutSpace.Enlarged(current, ScreenCatalog.GetScreens(), FramedChrome() ?? CameraWindowPlanner.AssumedNormalChrome) is { } enlarged)
            {
                _beforeLayoutSpace = current;
                Place(enlarged);
            }
        }

        viewModel.OnLayoutSpaceProvided();
    }

    /// <summary>The window gets back the place it had before the layout mode enlarged it, once it is the normal window.</summary>
    private void RestoreLayoutSpace()
    {
        if (_beforeLayoutSpace is not { } before || _viewModel is { IsLayoutModeActive: true })
        {
            return;
        }

        if (!_opened || WindowState != WindowState.Normal || _appliedMode != CameraWindowMode.Normal)
        {
            // Full screen (or the window closes): restored when it is the normal window again; remembered as before anyway.
            return;
        }

        _beforeLayoutSpace = null;
        _restoredLayoutSpace = before;
        Place(before);
        if (ClientSize == PlacedSize(before))
        {
            _restoredLayoutSpace = null;
        }
    }

    /// <summary>Where the window is, for remembering it: the place before a temporary enlargement (Q5, never remembered).</summary>
    private CameraWindowPlacement PlacementToRemember() =>
        _appliedMode == CameraWindowMode.Normal && (_beforeLayoutSpace ?? _restoredLayoutSpace) is { } before ? before : CurrentPlacement();

    private CameraWindowPlacement CurrentPlacement() => new(Position.X, Position.Y, ClientSize.Width, ClientSize.Height);

    /// <summary>The client size <see cref="Place"/> gives the window for <paramref name="placement"/> (at least the minimum).</summary>
    private Avalonia.Size PlacedSize(CameraWindowPlacement placement) =>
        new(Math.Max(placement.Width, MinWidth), Math.Max(placement.Height, MinHeight));
}
