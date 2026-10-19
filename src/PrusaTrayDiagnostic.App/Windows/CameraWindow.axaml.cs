using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.Core.Cameras;

namespace PrusaTrayDiagnostic.App.Windows;

/// <summary>
/// Live camera image of one printer (T-014). Closing it never quits the app (<c>ShutdownMode.OnExplicitShutdown</c>); its
/// view model is disposed by <see cref="AuxiliaryWindowSlot"/> when it closes, which stops the stream.
/// </summary>
/// <remarks>
/// Frames are drawn into one <see cref="WriteableBitmap"/> (recreated only when the resolution changes) on the UI thread;
/// the <see cref="Image"/> scales it with the aspect ratio kept. The window applies the view model's mode: normal window,
/// full screen, or the mini preview (no frame, always on top, moved by dragging the image, resized by its edges). It
/// reports where the normal window and the mini preview were, so that they come back there next time. Minimizing pauses
/// the stream.
/// </remarks>
public sealed partial class CameraWindow : Window
{
    private const double DefaultMiniWidth = 320;
    private const double DefaultMiniHeight = 180;
    private const double DefaultNormalWidth = 960;
    private const double DefaultNormalHeight = 540;

    /// <summary>Pointer travel (DIP) that turns a press on the mini preview into moving the window.</summary>
    private const double MoveThreshold = 4;

    private readonly FrameSink _sink;
    private CameraViewModel? _viewModel;
    private CameraWindowMode _appliedMode = CameraWindowMode.Normal;

    // The last mode with a window of its own (normal or mini): where full screen returns to.
    private CameraWindowMode _lastWindowedMode = CameraWindowMode.Normal;
    private bool _applyingMode;
    private bool _opened;

    // Mini preview: the press that may become a move once the pointer travels MoveThreshold.
    private (PointerPressedEventArgs Pressed, Point Origin)? _pendingMove;

    public CameraWindow()
    {
        InitializeComponent();
        _sink = new FrameSink(this);
        // DataContext (CameraViewModel) is set by the creator.
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        Detach();
        _viewModel = DataContext as CameraViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.CloseRequested += OnCloseRequested;
            _viewModel.AttachFrameSink(_sink);
            if (!_opened && _viewModel.NormalPlacement is { } placement)
            {
                // Before the window is shown, so that it does not jump; OnOpened moves it back if no screen shows it any more.
                WindowStartupLocation = WindowStartupLocation.Manual;
                Place(placement);
            }
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _opened = true;
        EnsureOnScreen();
        if (_viewModel is { } viewModel)
        {
            ApplyMode(viewModel.Mode);
            viewModel.SetActive(WindowState != WindowState.Minimized);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty || !_opened || _viewModel is not { } viewModel)
        {
            return;
        }

        // A minimized window pauses the stream; restoring it reconnects.
        viewModel.SetActive(WindowState != WindowState.Minimized);
        if (!_applyingMode && WindowState != WindowState.Minimized)
        {
            viewModel.OnWindowFullScreenChanged(WindowState == WindowState.FullScreen);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        RememberPlacement();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is { } viewModel)
        {
            // Stops the stream right away; the slot disposes the view model next (which persists the window state).
            viewModel.SetActive(false);
        }

        Detach();
        ClearImage();
        base.OnClosed(e);
    }

    private void Detach()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.AttachFrameSink(null);
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraViewModel.Mode) && _opened && _viewModel is { } viewModel)
        {
            ApplyMode(viewModel.Mode);
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void ApplyMode(CameraWindowMode mode)
    {
        if (_viewModel is not { } viewModel || mode == _appliedMode)
        {
            return;
        }

        RememberPlacement();
        _applyingMode = true;
        try
        {
            CameraWindowMode previousWindowed = _lastWindowedMode;
            if (mode != CameraWindowMode.Mini)
            {
                Topmost = false;
                WindowDecorations = WindowDecorations.Full;
            }

            switch (mode)
            {
                case CameraWindowMode.FullScreen:
                    WindowState = WindowState.FullScreen;
                    break;
                case CameraWindowMode.Mini:
                    WindowState = WindowState.Normal;
                    WindowDecorations = WindowDecorations.None;
                    Topmost = true;
                    // First time: where the normal window was, at the default size (the corner is an open question).
                    Place(viewModel.MiniPlacement ?? new CameraWindowPlacement(Position.X, Position.Y, DefaultMiniWidth, DefaultMiniHeight));
                    _lastWindowedMode = CameraWindowMode.Mini;
                    break;
                default:
                    WindowState = WindowState.Normal;
                    if (previousWindowed == CameraWindowMode.Mini)
                    {
                        Place(viewModel.NormalPlacement ?? new CameraWindowPlacement(Position.X, Position.Y, DefaultNormalWidth, DefaultNormalHeight));
                    }

                    _lastWindowedMode = CameraWindowMode.Normal;
                    break;
            }

            _appliedMode = mode;
        }
        finally
        {
            _applyingMode = false;
        }

        EnsureOnScreen();
    }

    /// <summary>Tells the view model where the window is in its current windowed mode (not in full screen or minimized).</summary>
    private void RememberPlacement()
    {
        if (_viewModel is { } viewModel && _opened && WindowState == WindowState.Normal && _appliedMode != CameraWindowMode.FullScreen)
        {
            viewModel.RememberPlacement(_appliedMode, new CameraWindowPlacement(Position.X, Position.Y, ClientSize.Width, ClientSize.Height));
        }
    }

    private void Place(CameraWindowPlacement placement)
    {
        if (!placement.IsValid)
        {
            return;
        }

        Width = Math.Max(placement.Width, MinWidth);
        Height = Math.Max(placement.Height, MinHeight);
        Position = new PixelPoint(placement.X, placement.Y);
    }

    /// <summary>
    /// A remembered position that cannot be reached (another screen layout, the title bar above a screen): the window moves
    /// to the primary screen.
    /// </summary>
    private void EnsureOnScreen()
    {
        if (!_opened || WindowState != WindowState.Normal || Screens.ScreenCount == 0)
        {
            return;
        }

        double scaling = DesktopScaling;
        PixelRect bounds = new(Position, PixelSize.FromSize(ClientSize, scaling));
        if (CameraWindowPlacementRules.IsReachable(bounds, [.. Screens.All.Select(static screen => screen.WorkingArea)]))
        {
            return;
        }

        if (Screens.Primary is { } primary)
        {
            PixelRect area = primary.WorkingArea;
            Position = new PixelPoint(
                area.X + Math.Max((area.Width - bounds.Width) / 2, 0),
                area.Y + Math.Max((area.Height - bounds.Height) / 2, 0));
        }
    }

    private void OnSurfacePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is not { } viewModel || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pendingMove = null;
        if (e.ClickCount == 2)
        {
            // Exactly a double-click: a triple-click must not switch twice.
            e.Handled = true;
            viewModel.OnImageDoubleClicked();
        }
        else if (e.ClickCount == 1 && viewModel.IsMini)
        {
            // The move starts only once the pointer really moves, so that a click (and a double-click) stays a click.
            e.Handled = true;
            _pendingMove = (e, e.GetPosition(this));
        }
    }

    private void OnSurfacePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingMove is not { } pending)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _viewModel is not { IsMini: true })
        {
            _pendingMove = null;
            return;
        }

        Vector moved = e.GetPosition(this) - pending.Origin;
        if (Math.Abs(moved.X) >= MoveThreshold || Math.Abs(moved.Y) >= MoveThreshold)
        {
            _pendingMove = null;
            BeginMoveDrag(pending.Pressed);
        }
    }

    private void OnSurfacePointerReleased(object? sender, PointerReleasedEventArgs e) => _pendingMove = null;

    private void OnGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is { IsMini: true } && sender is Control { Tag: WindowEdge edge } && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            BeginResizeDrag(edge, e);
        }
    }

    /// <summary>
    /// Copies a frame into the bitmap (one block copy when the row layouts match, which is the usual case) and asks the
    /// image to redraw. Allocates only when the resolution changes.
    /// </summary>
    private void Present(CameraFrameBuffer frame)
    {
        // The image owns the bitmap (no field: the window would have to be disposable); it is disposed when the window closes.
        WriteableBitmap? bitmap = Video.Source as WriteableBitmap;
        if (bitmap is null || bitmap.PixelSize.Width != frame.Width || bitmap.PixelSize.Height != frame.Height)
        {
            WriteableBitmap? old = bitmap;
            bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            Video.Source = bitmap;
            old?.Dispose();
        }

        using (ILockedFramebuffer target = bitmap.Lock())
        {
            BgraPixels.CopyTo(frame.Pixels, frame.Stride, frame.Height, target);
        }

        Video.InvalidateVisual();
    }

    private void ClearImage()
    {
        WriteableBitmap? shown = Video.Source as WriteableBitmap;
        Video.Source = null;
        shown?.Dispose();
    }

    private sealed class FrameSink(CameraWindow owner) : ICameraFrameSink
    {
        public void Present(CameraFrameBuffer frame) => owner.Present(frame);

        public void Clear() => owner.ClearImage();
    }
}
