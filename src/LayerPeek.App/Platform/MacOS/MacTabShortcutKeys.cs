using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// ⌘⇧[ and ⌘⇧] reach the windows as keys on every keyboard layout (T-240). AppKit takes ⌘⇧/ (⌘?) as the system shortcut
/// "Show Help menu" by its character: on the Czech layout the key at the US position of [ types / with ⇧, so
/// <c>-[NSApplication _handleKeyEquivalent:]</c> consumed the key down (with no Help menu nothing happens) and the view of
/// Avalonia (<c>AvnView</c>) never got its <c>keyDown:</c>; only the key up arrived. The view gets a
/// <c>performKeyEquivalent:</c> (AppKit asks the views of the key window before the application) that hands these two keys
/// to its own <c>keyDown:</c>, where Avalonia raises <c>KeyDown</c> as on the US layout
/// (<see cref="Windows.MacShortcuts.MainTabStep"/> matches the physical key).
/// </summary>
/// <remarks>
/// Only a key down of the two physical keys with exactly ⌘⇧ in the first responder of the key window is passed on; every
/// other key goes to the inherited <c>performKeyEquivalent:</c>. On the US layout the same keys reach <c>keyDown:</c>
/// anyway, so nothing changes there; LayerPeek has no Help menu the shortcut could open. Nothing changes when a later
/// Avalonia implements the method itself. Main thread only (AppKit calls it there). Never throws.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacTabShortcutKeys
{
    private const string ViewClass = "AvnView";
    private const string Selector = "performKeyEquivalent:";
    private const nint KeyDownType = 10;

    // kVK_ANSI_LeftBracket and kVK_ANSI_RightBracket (the keys right of P).
    private const nint LeftBracketKey = 33;
    private const nint RightBracketKey = 30;

    // NSEventModifierFlags: Shift, Control, Option and Command.
    private const nint Shift = 1 << 17;
    private const nint Command = 1 << 20;
    private const nint Modifiers = Shift | (1 << 18) | (1 << 19) | Command;

    // The superclass of AvnView, for the inherited performKeyEquivalent: (set before the method is added).
    private static nint _superclass;

    /// <summary>Adds <c>performKeyEquivalent:</c> to the view class of Avalonia (once; later calls do nothing).</summary>
    public static void Install(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            // The same class on every call: AvnView's superclass does not change when a method is added.
            _superclass = ObjCRuntime.SuperclassOf(ViewClass);
            if (_superclass != 0
                && ObjCRuntime.AddMethod(ViewClass, Selector, (nint)(delegate* unmanaged<nint, nint, nint, byte>)&PerformKeyEquivalent, "c@:@"))
            {
                LogInstalled(logger, ViewClass);
            }
            else
            {
                LogNotInstalled(logger, ViewClass);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an AppKit process.
        }
    }

    /// <summary>Whether an event of that type, key code and modifier flags is ⌘⇧[ or ⌘⇧] by the physical key.</summary>
    internal static bool IsTabShortcutKeyDown(nint type, nint keyCode, nint modifierFlags) =>
        type == KeyDownType && (keyCode is LeftBracketKey or RightBracketKey) && (modifierFlags & Modifiers) == (Command | Shift);

    [UnmanagedCallersOnly]
    private static byte PerformKeyEquivalent(nint view, nint selector, nint e)
    {
        try
        {
            if (e != 0 && IsOwnTabShortcut(view, e))
            {
                _ = ObjCRuntime.Send(view, ObjCRuntime.Selector("keyDown:"), e);
                return 1;
            }

            return ObjCRuntime.SendSuperBool(view, _superclass, selector, e) ? (byte)1 : (byte)0;
        }
        catch (Exception)
        {
            // An exception must not cross into AppKit; the key is then just not handled here.
            return 0;
        }
    }

    // One of the two keys, and the view is what takes the keys of the key window (not a view of another window).
    private static bool IsOwnTabShortcut(nint view, nint e)
    {
        // keyCode raises an exception for an event that is no key event: the type first.
        nint type = ObjCRuntime.Send(e, ObjCRuntime.Selector("type"));
        if (type != KeyDownType
            || !IsTabShortcutKeyDown(type, ObjCRuntime.Send(e, ObjCRuntime.Selector("keyCode")), ObjCRuntime.Send(e, ObjCRuntime.Selector("modifierFlags"))))
        {
            return false;
        }

        nint window = ObjCRuntime.Send(view, ObjCRuntime.Selector("window"));
        return window != 0
            && ObjCRuntime.SendBool(window, ObjCRuntime.Selector("isKeyWindow"))
            && ObjCRuntime.Send(window, ObjCRuntime.Selector("firstResponder")) == view;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cmd-Shift-[ and Cmd-Shift-] reach the windows as keys (performKeyEquivalent: of {ViewClass}).")]
    private static partial void LogInstalled(ILogger logger, string viewClass);

    [LoggerMessage(Level = LogLevel.Debug, Message = "performKeyEquivalent: not added: {ViewClass} is missing or implements it.")]
    private static partial void LogNotInstalled(ILogger logger, string viewClass);
}
