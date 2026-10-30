using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Windows;

/// <summary>
/// macOS (T-038 question 1, user decision 2026-10-30): the mini preview that is always on top is on every Space and above
/// apps in full screen (<see cref="MacWindowSpaces"/>). Applied whenever the mode or <see cref="Avalonia.Controls.Window.Topmost"/>
/// of the open window changes; leaving that state restores the native flags before e.g. full screen is entered (which needs
/// the flags of a normal window). Other OSes: nothing.
/// </summary>
public sealed partial class CameraWindow
{
    private IWindowCollectionBehavior? _collectionBehavior;
    private MacWindowSpaces? _spaces;
    private bool _spacesResolved;

    /// <summary>
    /// The native <c>collectionBehavior</c> of this window; default: the <c>NSWindow</c> on macOS, nothing elsewhere and
    /// with the headless platform. Tests set a fake; setting it applies the current state at once.
    /// </summary>
    internal IWindowCollectionBehavior? CollectionBehavior
    {
        get
        {
            _ = Spaces;
            return _collectionBehavior;
        }

        set
        {
            _spaces?.Apply(false);
            _collectionBehavior = value;
            _spaces = value is null ? null : new MacWindowSpaces(value);
            _spacesResolved = true;
            UpdateSpaces();
        }
    }

    /// <summary>The window is on every Space now (tests).</summary>
    internal bool IsOnAllSpaces => _spaces is { IsOnAllSpaces: true };

    private MacWindowSpaces? Spaces
    {
        get
        {
            if (!_spacesResolved && _opened)
            {
                // The native window exists once the window was shown.
                _spacesResolved = true;
                if (OperatingSystem.IsMacOS() && NativeWindowCollectionBehavior.Of(TryGetPlatformHandle()) is { } native)
                {
                    _collectionBehavior = native;
                    _spaces = new MacWindowSpaces(native);
                }
            }

            return _spaces;
        }
    }

    /// <summary>Joins or leaves every Space after the mode or "always on top" of the open window changed.</summary>
    private void UpdateSpaces()
    {
        if (_opened && !_closed)
        {
            Spaces?.Apply(MacWindowSpaces.JoinsAllSpaces(_appliedMode, Topmost));
        }
    }
}
