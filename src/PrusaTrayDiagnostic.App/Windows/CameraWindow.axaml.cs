using System.ComponentModel;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using PrusaTrayDiagnostic.App.Cameras;
using PrusaTrayDiagnostic.App.Platform.Windows;
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
/// reports where the normal window and the mini preview were, so that they come back there next time. Without a
/// remembered placement (or with "remember" off in the settings) it opens with the printer's defaults (T-035: mode,
/// monitor, size and corner, <see cref="CameraWindowPlanner"/>). Minimizing pauses the stream.
/// </remarks>
public sealed partial class CameraWindow : Window
{
    /// <summary>Pointer travel (DIP) that turns a press on the mini preview into moving the window.</summary>
    private const double MoveThreshold = 4;

    private readonly FrameSink _sink;
    private IScreenCatalog? _screens;
    private CameraViewModel? _viewModel;

    // The normal window was placed from the defaults with the assumed chrome: corrected with the real one after opening.
    private CameraWindowLayout? _placedWithAssumedChrome;
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

    /// <summary>
    /// The monitors the window is placed on (T-035); default: Avalonia's screens of this window. Set before the
    /// <c>DataContext</c> (tests use fake monitors).
    /// </summary>
    internal IScreenCatalog ScreenCatalog
    {
        get => _screens ??= new AvaloniaScreenCatalog(this);
        init => _screens = value ?? throw new ArgumentNullException(nameof(value));
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
            if (!_opened)
            {
                PlaceBeforeShowing(_viewModel);
            }
        }

        base.OnDataContextChanged(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _opened = true;
        CorrectFrame();
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
        else if (e.PropertyName == nameof(CameraViewModel.IsTopmost) && _opened && _viewModel is { } current && !_applyingMode)
        {
            // T-037: a saved option changes only the z-order of the open window, never its position or size.
            Topmost = current.Defaults.IsTopmost(_appliedMode);
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
        // The monitor the window is on before anything changes (T-035 Q5: a mode switch stays on it).
        PixelBounds current = CurrentBounds();
        _placedWithAssumedChrome = null;
        _applyingMode = true;
        try
        {
            CameraWindowMode previousWindowed = _lastWindowedMode;
            if (mode != CameraWindowMode.Mini)
            {
                // T-037: the option of the normal window; full screen is never on top.
                Topmost = viewModel.Defaults.IsTopmost(mode);
                WindowDecorations = WindowDecorations.Full;
            }

            switch (mode)
            {
                case CameraWindowMode.FullScreen:
                    // Full screen on the monitor the window is on.
                    WindowState = WindowState.FullScreen;
                    break;
                case CameraWindowMode.Mini:
                    WindowState = WindowState.Normal;
                    WindowDecorations = WindowDecorations.None;
                    Topmost = viewModel.Defaults.IsTopmost(CameraWindowMode.Mini);
                    // First time: the default size and corner of the mini preview on the monitor the window is on.
                    Place(viewModel.MiniPlacement ?? FromDefaults(viewModel.Defaults.Mini, CameraWindowMode.Mini, current));
                    _lastWindowedMode = CameraWindowMode.Mini;
                    break;
                default:
                    WindowState = WindowState.Normal;
                    if (previousWindowed == CameraWindowMode.Mini)
                    {
                        Place(viewModel.NormalPlacement ?? FromDefaults(viewModel.Defaults.Normal, CameraWindowMode.Normal, current));
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
    /// Before the window is shown (no visible jump), T-035: the mode chosen by the view model, the remembered placement
    /// of that mode when it is reachable, else the printer's defaults on the chosen monitor. Full screen places the normal
    /// window on its monitor first; <see cref="OnOpened"/> then switches. Without screens the window keeps the default size
    /// and centres itself.
    /// </summary>
    private void PlaceBeforeShowing(CameraViewModel viewModel)
    {
        bool mini = viewModel.Mode == CameraWindowMode.Mini;
        // T-037: the option of the mode it opens in (full screen, switched to after opening, is never on top).
        Topmost = viewModel.IsTopmost;
        if (mini)
        {
            // Opened as the mini preview right away, without a normal window first.
            WindowDecorations = WindowDecorations.None;
            _appliedMode = CameraWindowMode.Mini;
            _lastWindowedMode = CameraWindowMode.Mini;
        }

        CameraWindowMode layoutMode = mini ? CameraWindowMode.Mini : CameraWindowMode.Normal;
        CameraWindowLayout layout = mini ? viewModel.Defaults.Mini : viewModel.Defaults.Normal;
        CameraWindowPlacement? remembered = mini ? viewModel.MiniPlacement : viewModel.NormalPlacement;
        CameraWindowPlacement? placement = CameraWindowPlanner.ForOpening(
            viewModel.Defaults,
            layoutMode,
            remembered,
            ScreenCatalog.GetScreens(),
            CameraWindowPlanner.AssumedChrome(layoutMode));
        if (placement is not { } chosen)
        {
            Width = layout.Size.Width;
            Height = layout.Size.Height;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Place(chosen);
        if (!mini && remembered != chosen)
        {
            _placedWithAssumedChrome = layout;
        }
    }

    /// <summary>
    /// After opening, with the real chrome (frame and, on Windows, the invisible borders): a normal window placed from the
    /// defaults with a different assumed chrome is placed again on its monitor, once (a corner keeps its visible margin).
    /// </summary>
    private void CorrectFrame()
    {
        if (_placedWithAssumedChrome is not { } layout || WindowState != WindowState.Normal)
        {
            return;
        }

        _placedWithAssumedChrome = null;
        if (MeasuredChrome() is not { } chrome || chrome.IsCloseTo(CameraWindowPlanner.AssumedNormalChrome))
        {
            return;
        }

        if (CameraWindowPlanner.OnCurrentScreen(layout, CurrentBounds(), ScreenCatalog.GetScreens(), chrome) is { } placement)
        {
            Place(placement);
        }
    }

    /// <summary>
    /// <paramref name="layout"/> on the monitor of <paramref name="window"/>; without screens the default size where the
    /// window is.
    /// </summary>
    private CameraWindowPlacement FromDefaults(CameraWindowLayout layout, CameraWindowMode mode, PixelBounds window) =>
        CameraWindowPlanner.OnCurrentScreen(layout, window, ScreenCatalog.GetScreens(), ChromeFor(mode))
            ?? new CameraWindowPlacement(Position.X, Position.Y, layout.Size.Width, layout.Size.Height);

    /// <summary>The chrome of the window in <paramref name="mode"/>: measured while it has a frame, else assumed.</summary>
    private WindowChrome ChromeFor(CameraWindowMode mode) =>
        mode == CameraWindowMode.Mini ? WindowChrome.None
        : _opened && WindowDecorations == WindowDecorations.Full && MeasuredChrome() is { } measured ? measured
        : CameraWindowPlanner.AssumedNormalChrome;

    /// <summary>
    /// The real chrome of the shown window: the frame from <c>FrameSize</c>, the invisible borders from the window manager
    /// (Windows only; elsewhere none). <see langword="null"/> while the frame is not known.
    /// </summary>
    private WindowChrome? MeasuredChrome()
    {
        if (CameraWindowPlanner.FrameOf(FrameSize, ClientSize) is not { } frame)
        {
            return null;
        }

        Thickness invisible = default;
        if (OperatingSystem.IsWindows()
            && TryGetPlatformHandle() is { HandleDescriptor: "HWND", Handle: var hwnd }
            && WindowFrameNative.TryGetBounds(hwnd) is { } bounds)
        {
            invisible = CameraWindowPlanner.InvisibleBordersOf(bounds.Window, bounds.Visible, DesktopScaling);
        }

        return new WindowChrome(frame, invisible);
    }

    /// <summary>The window in screen pixels (client size scaled; enough to find its monitor).</summary>
    private PixelBounds CurrentBounds()
    {
        double scaling = _opened ? DesktopScaling : 1.0;
        return new PixelBounds(
            Position.X,
            Position.Y,
            Math.Max((int)Math.Round(ClientSize.Width * scaling), 1),
            Math.Max((int)Math.Round(ClientSize.Height * scaling), 1));
    }

    /// <summary>
    /// A position that cannot be reached (another screen layout, the title bar above a screen): the window moves to where
    /// the printer's defaults put it (T-035), keeping its mode.
    /// </summary>
    private void EnsureOnScreen()
    {
        if (!_opened || WindowState != WindowState.Normal || _viewModel is not { } viewModel)
        {
            return;
        }

        IReadOnlyList<CameraScreen> screens = ScreenCatalog.GetScreens();
        if (screens.Count == 0 || CameraWindowPlanner.IsReachable(new CameraWindowPlacement(Position.X, Position.Y, ClientSize.Width, ClientSize.Height), screens))
        {
            return;
        }

        CameraWindowMode layoutMode = _appliedMode == CameraWindowMode.Mini ? CameraWindowMode.Mini : CameraWindowMode.Normal;
        if (CameraWindowPlanner.ForOpening(viewModel.Defaults, layoutMode, remembered: null, screens, ChromeFor(layoutMode)) is { } placement)
        {
            Place(placement);
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
