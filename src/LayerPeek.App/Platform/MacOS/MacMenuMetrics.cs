using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// What macOS tells about the menu bar menu for the size of its printer icons (T-116, see
/// <see cref="Tray.Menu.PrinterMenuIconSize.MacOS"/>): the menu font size and the highest backing scale factor of the
/// displays. Through the Objective-C runtime (<see cref="ObjCRuntime"/>, Native AOT safe); without AppKit (headless UI
/// tests, unit tests) or without a display the value is <see langword="null"/>. Never throws; UI (main) thread only.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacMenuMetrics
{
    /// <summary><c>[[NSFont menuFontOfSize:0] pointSize]</c> (the default menu font), <see langword="null"/> when it cannot be read.</summary>
    public static double? MenuFontPointSize() => ObjCRuntime.InAutoreleasePool(static double? () =>
    {
        nint fontClass = ObjCRuntime.Class("NSFont");
        if (fontClass == 0)
        {
            return null;
        }

        nint font = ObjCRuntime.SendWithCGFloat(fontClass, ObjCRuntime.Selector("menuFontOfSize:"), 0);
        return font == 0 ? null : Positive(ObjCRuntime.SendCGFloat(font, ObjCRuntime.Selector("pointSize")));
    });

    /// <summary>
    /// The highest <c>backingScaleFactor</c> of <c>[NSScreen screens]</c> (2.0 for Retina), <see langword="null"/> when
    /// there is no display or AppKit is not loaded.
    /// </summary>
    public static double? HighestBackingScaleFactor() => ObjCRuntime.InAutoreleasePool(static double? () =>
    {
        nint screenClass = ObjCRuntime.Class("NSScreen");
        nint screens = screenClass == 0 ? 0 : ObjCRuntime.Send(screenClass, ObjCRuntime.Selector("screens"));
        if (screens == 0)
        {
            return null;
        }

        double? highest = null;
        nint count = ObjCRuntime.Send(screens, ObjCRuntime.Selector("count"));
        for (nint index = 0; index < count; index++)
        {
            nint screen = ObjCRuntime.Send(screens, ObjCRuntime.Selector("objectAtIndex:"), index);
            if (screen != 0 && Positive(ObjCRuntime.SendCGFloat(screen, ObjCRuntime.Selector("backingScaleFactor"))) is { } scale)
            {
                highest = Math.Max(highest ?? 0, scale);
            }
        }

        return highest;
    });

    private static double? Positive(double value) => double.IsFinite(value) && value > 0 ? value : null;
}
