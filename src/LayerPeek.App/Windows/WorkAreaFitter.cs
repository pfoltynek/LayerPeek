using Avalonia;
using Avalonia.Controls;

namespace LayerPeek.App.Windows;

/// <summary>
/// Opens an auxiliary window (settings T-033, diagnostics T-041) at its designed size, shrunk to the work area of its
/// screen with <see cref="AuxiliaryWindowPlacement"/>. Create it in the window's constructor after
/// <c>InitializeComponent</c> (the XAML <c>Width</c> / <c>Height</c> are the designed size) and call
/// <see cref="CorrectAfterOpening"/> from <c>OnOpened</c>. UI thread only.
/// </summary>
internal sealed class WorkAreaFitter
{
    private readonly Window _window;

    /// <summary>Remembers the designed size and fits the window before it is shown.</summary>
    public WorkAreaFitter(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        DesignedSize = new Size(window.Width, window.Height);
        FitBeforeShowing();
    }

    /// <summary>Client size from the XAML, before any fitting.</summary>
    public Size DesignedSize { get; }

    private Size MinSize => new(_window.MinWidth, _window.MinHeight);

    /// <summary>
    /// After opening, with the real screen and the measured frame: only when the window as shown does not fit (a thicker
    /// frame than assumed, another screen) it shrinks and is centred again; otherwise nothing moves.
    /// </summary>
    public void CorrectAfterOpening()
    {
        if (_window.WindowState != WindowState.Normal || (_window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary) is not { } screen)
        {
            return;
        }

        if (AuxiliaryWindowPlacement.CorrectionAfterOpening(
                new Size(_window.Width, _window.Height),
                DesignedSize,
                MinSize,
                screen.WorkingArea,
                screen.Scaling,
                AuxiliaryWindowPlacement.ChromeOf(_window.FrameSize, _window.ClientSize)) is { } bounds)
        {
            _window.Width = bounds.ClientSize.Width;
            _window.Height = bounds.ClientSize.Height;
            _window.Position = bounds.Position;
        }
    }

    /// <summary>
    /// Before the window is shown: a work area smaller than the window (high scaling, a small screen, a large taskbar)
    /// shrinks it, so that it appears already fitted and <c>CenterScreen</c> centres the fitted size (no visible jump).
    /// The screen is the one <c>CenterScreen</c> picks for a window without an owner (the one under the window's
    /// position, else the primary); the frame is not known yet, so the assumed title bar is used.
    /// </summary>
    private void FitBeforeShowing()
    {
        if (_window.Screens.ScreenCount == 0)
        {
            return;
        }

        Avalonia.Platform.Screen? screen = _window.Screens.ScreenFromPoint(_window.Position) ?? _window.Screens.Primary;
        Size size = AuxiliaryWindowPlacement.SizeBeforeShowing(DesignedSize, MinSize, screen?.WorkingArea, screen?.Scaling ?? 1.0);
        _window.Width = size.Width;
        _window.Height = size.Height;
    }
}
