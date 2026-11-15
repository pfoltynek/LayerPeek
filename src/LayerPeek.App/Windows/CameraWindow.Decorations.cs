using Avalonia.Controls;

using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Windows;

/// <summary>
/// macOS (T-237, the fix approved in T-038 question 2): after AppKit took the window out of full screen by itself (Esc, the
/// green button, the menu), the decorations of the mode switched to afterwards reach the native window although
/// Avalonia.Native ignores them (<see cref="MacFullScreenDecorations"/>); the mode then places the window as usual, so the
/// mini preview comes back without a frame at its size and corner. Other OSes, and exits the app asked for: nothing.
/// </summary>
public sealed partial class CameraWindow
{
    private IWindowDecorations? _nativeDecorations;
    private MacFullScreenDecorations? _fullScreenDecorations;
    private bool _decorationsResolved;

    /// <summary>
    /// The native decorations of this window; default: the <c>NSWindow</c> on macOS, nothing elsewhere and with the headless
    /// platform. Tests set a fake.
    /// </summary>
    internal IWindowDecorations? NativeDecorations
    {
        get
        {
            _ = FullScreenDecorations;
            return _nativeDecorations;
        }

        set
        {
            _nativeDecorations = value;
            _fullScreenDecorations = value is null ? null : new MacFullScreenDecorations(value);
            _decorationsResolved = true;
        }
    }

    /// <summary>Avalonia ignores changes of the decorations now (tests).</summary>
    internal bool DecorationsIgnoredByAvalonia => _fullScreenDecorations is { IsIgnoredByAvalonia: true };

    private MacFullScreenDecorations? FullScreenDecorations
    {
        get
        {
            if (!_decorationsResolved && _opened)
            {
                // The native window exists once the window was shown.
                _decorationsResolved = true;
                if (OperatingSystem.IsMacOS() && NativeWindowDecorations.Of(TryGetPlatformHandle()) is { } native)
                {
                    _nativeDecorations = native;
                    _fullScreenDecorations = new MacFullScreenDecorations(native);
                }
            }

            return _fullScreenDecorations;
        }
    }

    /// <summary>Before the mode follows a new window state: whether AppKit (not the app) left full screen.</summary>
    private void OnFullScreenStateChanged(WindowState previous, WindowState current) =>
        FullScreenDecorations?.OnWindowStateChanged(previous == WindowState.FullScreen, current == WindowState.FullScreen, byApp: _applyingMode);

    /// <summary>After Avalonia got the new decorations: applied natively when Avalonia ignores them.</summary>
    private void OnDecorationsChanged()
    {
        if (_opened && !_closed)
        {
            FullScreenDecorations?.OnDecorationsChanged(WindowDecorations == WindowDecorations.Full, CanResize, CanMinimize && Owner is null);
        }
    }
}
