using System.Runtime.Versioning;

using Microsoft.Extensions.Logging;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The Dock menu lists the titled windows of the app (T-241, as T-102 expects). AppKit puts the windows into the Dock menu
/// from the list of its Window menu (<c>NSApp.windowsMenu</c>), and an app without one gets only the system items there;
/// Avalonia (<c>libAvaloniaNative</c> 12.1.6) never sets a Window menu. The app gets a Window menu that is in no menu bar:
/// AppKit keeps its window items up to date (as for any Window menu) and the Dock lists them; choosing one brings that
/// window to the front with the focus. Nothing of it is visible in the menu bar.
/// </summary>
/// <remarks>
/// A tiny AppKit app that switches from accessory to regular as LayerPeek does (T-102) showed the same: no window in the
/// Dock menu without a Window menu, the window with one (also when that menu is in no menu bar). A Window menu that
/// is already set (a later Avalonia) is kept. Only an existing <c>NSApp</c> is changed (none in the headless UI tests).
/// The menu lives as long as the app (one small object, never released). Main thread only. Never throws.
/// </remarks>
[SupportedOSPlatform("macos")]
internal static partial class MacWindowsMenu
{
    /// <summary>Gives the app a Window menu outside the menu bar when it has none (later calls do nothing).</summary>
    public static void Install(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            nint application = MacAppActivation.RunningApplication();
            if (application == 0 || ObjCRuntime.Send(application, ObjCRuntime.Selector("windowsMenu")) != 0)
            {
                return;
            }

            // The title is never shown (the menu is in no menu bar).
            nint menu = ObjCRuntime.InAutoreleasePool(() => ObjCRuntime.Send(
                ObjCRuntime.Send(ObjCRuntime.Class("NSMenu"), ObjCRuntime.Selector("alloc")),
                ObjCRuntime.Selector("initWithTitle:"),
                ObjCRuntime.NSString("Window")));
            if (menu == 0)
            {
                return;
            }

            _ = ObjCRuntime.Send(application, ObjCRuntime.Selector("setWindowsMenu:"), menu);
            LogInstalled(logger);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not an AppKit process: there is no Dock menu.
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "The app has a Window menu outside the menu bar: the Dock menu lists its windows.")]
    private static partial void LogInstalled(ILogger logger);
}
