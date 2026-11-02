using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// ⌘. reaches the windows as a key (T-141). AppKit does not send ⌘. to the view as <c>keyDown:</c> like ⌘W or Esc: after
/// <c>performKeyEquivalent:</c> it sends the action <c>cancelOperation:</c> up the responder chain, and the view of
/// Avalonia (<c>AvnView</c> in <c>libAvaloniaNative</c> 12.1.4) has no such method, so the shortcut of T-100
/// (<see cref="Windows.MacShortcuts.IsCancel"/>) never fired. The view gets a <c>cancelOperation:</c> that hands the
/// current ⌘ key event to its own <c>keyDown:</c>, where Avalonia raises <c>KeyDown</c> as for any other key.
/// </summary>
/// <remarks>
/// Only a key-down event with ⌘ is passed on: <c>cancelOperation:</c> also comes from other senders (a menu item, Esc
/// through the text input system), which Avalonia handles already or which are no key. Nothing changes when a later
/// Avalonia implements the method itself. Main thread only (AppKit calls it there). Never throws.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacCommandPeriod
{
    private const string ViewClass = "AvnView";
    private const nint KeyDownType = 10;
    private const nint CommandFlag = 1 << 20;

    // keyDown: of the view can lead to cancelOperation: again (text input system): pass each event on once.
    private static bool _forwarding;

    /// <summary>Adds <c>cancelOperation:</c> to the view class of Avalonia (once; later calls do nothing).</summary>
    public static void Install(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            if (ObjCRuntime.AddMethod(ViewClass, "cancelOperation:", (nint)(delegate* unmanaged<nint, nint, nint, void>)&CancelOperation, "v@:@"))
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

    /// <summary>Whether an event of that type and those modifier flags is a key pressed with ⌘.</summary>
    internal static bool IsCommandKeyDown(nint type, nint modifierFlags) => type == KeyDownType && (modifierFlags & CommandFlag) != 0;

    [UnmanagedCallersOnly]
    private static void CancelOperation(nint view, nint selector, nint sender)
    {
        if (_forwarding)
        {
            return;
        }

        try
        {
            _forwarding = true;
            nint application = ObjCRuntime.Send(ObjCRuntime.Class("NSApplication"), ObjCRuntime.Selector("sharedApplication"));
            nint current = application == 0 ? 0 : ObjCRuntime.Send(application, ObjCRuntime.Selector("currentEvent"));
            if (current != 0
                && IsCommandKeyDown(ObjCRuntime.Send(current, ObjCRuntime.Selector("type")), ObjCRuntime.Send(current, ObjCRuntime.Selector("modifierFlags"))))
            {
                _ = ObjCRuntime.Send(view, ObjCRuntime.Selector("keyDown:"), current);
            }
        }
        catch (Exception)
        {
            // An exception must not cross into AppKit; the key is then just not handled.
        }
        finally
        {
            _forwarding = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cmd-. reaches the windows as a key (cancelOperation: of {ViewClass}).")]
    private static partial void LogInstalled(ILogger logger, string viewClass);

    [LoggerMessage(Level = LogLevel.Debug, Message = "cancelOperation: not added: {ViewClass} is missing or implements it.")]
    private static partial void LogNotInstalled(ILogger logger, string viewClass);
}
