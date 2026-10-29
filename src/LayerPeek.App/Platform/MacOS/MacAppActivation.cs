using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// Brings the app to the front on macOS (T-019). LayerPeek is an agent app (<c>LSUIElement</c>, no Dock icon, accessory
/// activation policy): a window it shows from the menu bar extra would otherwise open behind the active app without the
/// keyboard focus. Showing an auxiliary window therefore activates the app first (<c>[NSApp activate]</c>, macOS 14+; the
/// deprecated <c>activateIgnoringOtherApps:</c> where it is missing), then the window becomes key (Avalonia).
/// </summary>
/// <remarks>
/// Only an existing <c>NSApp</c> is activated, it is never created: in a process without AppKit (the headless UI tests,
/// the test host) nothing happens. UI (main) thread only, like every AppKit call. Never throws.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static class MacAppActivation
{
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    /// <summary>Activates the running app; does nothing without AppKit or without <c>NSApp</c>.</summary>
    public static void Activate()
    {
        try
        {
            nint application = RunningApplication();
            if (application == 0)
            {
                return;
            }

            if (ObjCRuntime.RespondsTo(application, "activate"))
            {
                ObjCRuntime.SendVoid(application, ObjCRuntime.Selector("activate"));
            }
            else
            {
                ObjCRuntime.SendVoid(application, ObjCRuntime.Selector("activateIgnoringOtherApps:"), 1);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an AppKit process: there is nothing to activate.
        }
    }

    /// <summary>
    /// The global <c>NSApp</c> of AppKit, or 0. AppKit is looked at only when its class is already loaded, so this never
    /// loads AppKit into a process that does not use it.
    /// </summary>
    private static unsafe nint RunningApplication()
    {
        if (ObjCRuntime.Class("NSApplication") == 0 || !NativeLibrary.TryLoad(AppKit, out nint appKit))
        {
            return 0;
        }

        try
        {
            return NativeLibrary.TryGetExport(appKit, "NSApp", out nint variable) ? *(nint*)variable : 0;
        }
        finally
        {
            // Only the reference TryLoad added; AppKit stays loaded for its users.
            NativeLibrary.Free(appKit);
        }
    }
}
