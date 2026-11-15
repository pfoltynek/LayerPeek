using System.Runtime.Versioning;

using Avalonia.Platform;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The native look of one window's <c>WindowDecorations</c> (macOS, T-237): the title bar with its buttons, or none.
/// Calls never throw; UI (main) thread only.
/// </summary>
internal interface IWindowDecorations
{
    /// <summary>
    /// Gives the window a title bar with its buttons (<paramref name="framed"/>) or no frame at all, as Avalonia.Native does
    /// for <c>WindowDecorations.Full</c> and <c>WindowDecorations.None</c>.
    /// </summary>
    void Apply(bool framed, bool resizable, bool miniaturizable);
}

/// <summary>
/// Works around Avalonia.Native 12.1.6 (T-237): a window that AppKit takes out of full screen by itself (Esc, the green
/// button, the menu) keeps the internal full-screen flag of <c>WindowImpl</c>, which only Avalonia's own exit
/// (<c>WindowState</c> set by the app) clears; until Avalonia enters full screen again, every change of
/// <c>WindowDecorations</c> is only stored, never applied. The mini preview switched to after such an exit therefore kept
/// the title bar (28 pt taller, below the work area). While the flag is stale, this class applies each change of the
/// decorations to the native window itself (<see cref="IWindowDecorations"/>); Avalonia stores the same value, so its own
/// later updates agree. AppKit reports that exit only after the animation (<c>windowDidExitFullScreen:</c>), so the frame
/// it restores is final when the decorations and then the placement are applied. Platform independent logic (tested with
/// fakes); the native side is <see cref="NativeWindowDecorations"/>.
/// </summary>
internal sealed class MacFullScreenDecorations(IWindowDecorations native)
{
    // NSWindowStyleMask flags Avalonia.Native sets for the decorations (WindowImpl::CalculateStyleMask).
    internal const nuint Titled = 1 << 0;
    internal const nuint Closable = 1 << 1;
    internal const nuint Miniaturizable = 1 << 2;
    internal const nuint Resizable = 1 << 3;
    internal const nuint TexturedBackground = 1 << 8;
    internal const nuint FullSizeContentView = 1 << 15;

    /// <summary>Avalonia ignores changes of the decorations now (the window left full screen without the app).</summary>
    public bool IsIgnoredByAvalonia { get; private set; }

    /// <summary>
    /// The window state changed. <paramref name="byApp"/>: the app set it (Avalonia then enters or leaves full screen
    /// itself and keeps its flag right); otherwise AppKit changed it.
    /// </summary>
    public void OnWindowStateChanged(bool wasFullScreen, bool isFullScreen, bool byApp)
    {
        if (isFullScreen)
        {
            // Whoever entered it, the next exit decides: the app's goes through Avalonia, which clears its flag; AppKit's
            // sets this again.
            IsIgnoredByAvalonia = false;
        }
        else if (wasFullScreen && !byApp)
        {
            IsIgnoredByAvalonia = true;
        }
    }

    /// <summary>The app changed the decorations: applied natively as long as Avalonia ignores them.</summary>
    public void OnDecorationsChanged(bool framed, bool resizable, bool miniaturizable)
    {
        if (IsIgnoredByAvalonia)
        {
            native.Apply(framed, resizable, miniaturizable);
        }
    }

    /// <summary>
    /// The style mask Avalonia.Native gives a window without an extended client area for <c>Full</c>
    /// (<paramref name="framed"/>) or <c>None</c>: the decoration flags replaced, every other flag of
    /// <paramref name="current"/> kept.
    /// </summary>
    public static nuint StyleMask(nuint current, bool framed, bool resizable, bool miniaturizable)
    {
        nuint mask = current & ~(FullSizeContentView | Titled | Closable | Resizable | Miniaturizable | TexturedBackground);
        mask |= framed ? Titled | Closable | (resizable ? Resizable : 0) : FullSizeContentView;
        return miniaturizable ? mask | Miniaturizable : mask;
    }
}

/// <summary>
/// <see cref="IWindowDecorations"/> of a real <c>NSWindow</c> through the Objective-C runtime (<see cref="ObjCRuntime"/>,
/// <c>[LibraryImport]</c>, no new package): what <c>WindowImpl::SetDecorations</c> of Avalonia.Native 12.1.6 does for
/// <c>Full</c> and <c>None</c> (style mask, shadow, title, title bar, window buttons). The pointer is not retained: the
/// caller uses it only while its Avalonia window is open. Without AppKit nothing is changed.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class NativeWindowDecorations : IWindowDecorations
{
    private const nint TitleVisible = 0;
    private const nint TitleHidden = 1;

    // NSWindowCloseButton, NSWindowMiniaturizeButton, NSWindowZoomButton.
    private static readonly nint[] StandardButtons = [0, 1, 2];

    private readonly nint _window;

    private NativeWindowDecorations(nint window) => _window = window;

    /// <summary>The native window of an Avalonia platform handle; <see langword="null"/> for any other handle (headless).</summary>
    public static NativeWindowDecorations? Of(IPlatformHandle? handle)
    {
        nint window = NativeWindowCollectionBehavior.WindowOf(handle);
        try
        {
            return window == 0 || !ObjCRuntime.RespondsTo(window, "setStyleMask:") ? null : new NativeWindowDecorations(window);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Apply(bool framed, bool resizable, bool miniaturizable)
    {
        try
        {
            nuint current = (nuint)ObjCRuntime.Send(_window, ObjCRuntime.Selector("styleMask"));
            nuint mask = MacFullScreenDecorations.StyleMask(current, framed, resizable, miniaturizable);
            if (mask != current)
            {
                _ = ObjCRuntime.Send(_window, ObjCRuntime.Selector("setStyleMask:"), (nint)mask);
            }

            byte yes = framed ? (byte)1 : (byte)0;
            ObjCRuntime.SendVoid(_window, ObjCRuntime.Selector("setHasShadow:"), yes);
            _ = ObjCRuntime.Send(_window, ObjCRuntime.Selector("setTitleVisibility:"), framed ? TitleVisible : TitleHidden);
            ObjCRuntime.SendVoid(_window, ObjCRuntime.Selector("setTitlebarAppearsTransparent:"), (byte)(1 - yes));
            nint standardButton = ObjCRuntime.Selector("standardWindowButton:");
            nint setHidden = ObjCRuntime.Selector("setHidden:");
            foreach (nint kind in StandardButtons)
            {
                nint button = ObjCRuntime.Send(_window, standardButton, kind);
                if (button != 0)
                {
                    ObjCRuntime.SendVoid(button, setHidden, (byte)(1 - yes));
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an AppKit process: nothing to change.
        }
    }
}
