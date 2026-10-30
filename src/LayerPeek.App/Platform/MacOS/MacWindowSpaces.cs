using System.Runtime.Versioning;

using Avalonia.Platform;

using LayerPeek.Core.Cameras;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The <c>collectionBehavior</c> of one native window (macOS, T-038): which Spaces it appears on and how it behaves next
/// to an app in full screen. Calls never throw; UI (main) thread only.
/// </summary>
internal interface IWindowCollectionBehavior
{
    /// <summary>The current <c>NSWindowCollectionBehavior</c> flags, or <see langword="null"/> when they cannot be read.</summary>
    nuint? Read();

    /// <summary>Sets the flags (must be a valid combination, see <see cref="MacWindowSpaces.Joined"/>).</summary>
    void Write(nuint behavior);
}

/// <summary>
/// The camera mini preview that is always on top is visible on every Space and above apps in full screen (T-038 question 1,
/// user decision 2026-10-30: option b), like a picture-in-picture window: while it is in that state its native window gets
/// <c>NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary</c>. Avalonia 12.1.4 gives
/// a window with <c>Topmost</c> only <c>NSFloatingWindowLevel</c>, which keeps it above the windows of its own Space. The
/// normal window and full screen are unchanged; leaving the mini preview or switching "always on top" off restores the
/// flags the window had before. Platform independent logic (tested with fakes); the native side is
/// <see cref="NativeWindowCollectionBehavior"/>.
/// </summary>
internal sealed class MacWindowSpaces
{
    /// <summary><c>NSWindowCollectionBehaviorCanJoinAllSpaces</c>.</summary>
    internal const nuint CanJoinAllSpaces = 1 << 0;

    /// <summary><c>NSWindowCollectionBehaviorMoveToActiveSpace</c> (excludes <see cref="CanJoinAllSpaces"/>).</summary>
    internal const nuint MoveToActiveSpace = 1 << 1;

    /// <summary><c>NSWindowCollectionBehaviorFullScreenPrimary</c> (what Avalonia gives a normal window).</summary>
    internal const nuint FullScreenPrimary = 1 << 7;

    /// <summary><c>NSWindowCollectionBehaviorFullScreenAuxiliary</c>.</summary>
    internal const nuint FullScreenAuxiliary = 1 << 8;

    /// <summary><c>NSWindowCollectionBehaviorFullScreenNone</c>.</summary>
    internal const nuint FullScreenNone = 1 << 9;

    /// <summary><c>NSWindowCollectionBehaviorFullScreenAllowsTiling</c> (only for a window that can be in full screen itself).</summary>
    internal const nuint FullScreenAllowsTiling = 1 << 11;

    // Flags AppKit does not accept together with the two set here (it raises an Objective-C exception, which would end
    // the process): only one of CanJoinAllSpaces / MoveToActiveSpace and one of the full screen kinds.
    private const nuint Conflicting = MoveToActiveSpace | FullScreenPrimary | FullScreenNone | FullScreenAllowsTiling;

    private readonly IWindowCollectionBehavior _window;

    // The flags before joining every Space; null while not joined.
    private nuint? _saved;

    public MacWindowSpaces(IWindowCollectionBehavior window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
    }

    /// <summary>The window is on every Space now (the flags were set and not yet restored).</summary>
    public bool IsOnAllSpaces => _saved is not null;

    /// <summary>Whether a camera window in <paramref name="mode"/> joins every Space: only the mini preview that is always on top.</summary>
    public static bool JoinsAllSpaces(CameraWindowMode mode, bool topmost) => mode == CameraWindowMode.Mini && topmost;

    /// <summary><paramref name="previous"/> with the flags of a window on every Space, without the flags that exclude them.</summary>
    public static nuint Joined(nuint previous) => (previous & ~Conflicting) | CanJoinAllSpaces | FullScreenAuxiliary;

    /// <summary>
    /// Joins every Space (remembering the flags before) or restores them; nothing when the window is already in that
    /// state, or when its flags cannot be read.
    /// </summary>
    public void Apply(bool onAllSpaces)
    {
        if (onAllSpaces)
        {
            if (_saved is not null || _window.Read() is not { } previous)
            {
                return;
            }

            _saved = previous;
            _window.Write(Joined(previous));
        }
        else if (_saved is { } previous)
        {
            _saved = null;
            _window.Write(previous);
        }
    }
}

/// <summary>
/// <see cref="IWindowCollectionBehavior"/> of a real <c>NSWindow</c> through the Objective-C runtime (<see cref="ObjCRuntime"/>,
/// <c>[LibraryImport]</c>, no new package). The pointer is not retained: the caller uses it only while its Avalonia window is
/// open. Without AppKit nothing is read or written.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class NativeWindowCollectionBehavior : IWindowCollectionBehavior
{
    private readonly nint _window;

    private NativeWindowCollectionBehavior(nint window) => _window = window;

    /// <summary>
    /// The native window of an Avalonia platform handle: <c>NSWindow</c> directly, or the window of an <c>NSView</c>;
    /// <see langword="null"/> for any other handle (the headless platform of the UI tests) or without a window.
    /// </summary>
    public static NativeWindowCollectionBehavior? Of(IPlatformHandle? handle)
    {
        try
        {
            nint window = handle switch
            {
                { HandleDescriptor: "NSWindow", Handle: var pointer } => pointer,
                { HandleDescriptor: "NSView", Handle: not 0 and var view } => ObjCRuntime.Send(view, ObjCRuntime.Selector("window")),
                _ => 0,
            };
            return window == 0 || !ObjCRuntime.RespondsTo(window, "setCollectionBehavior:") ? null : new NativeWindowCollectionBehavior(window);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public nuint? Read()
    {
        try
        {
            return (nuint)ObjCRuntime.Send(_window, ObjCRuntime.Selector("collectionBehavior"));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public void Write(nuint behavior)
    {
        try
        {
            _ = ObjCRuntime.Send(_window, ObjCRuntime.Selector("setCollectionBehavior:"), (nint)behavior);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an AppKit process: nothing to change.
        }
    }
}
