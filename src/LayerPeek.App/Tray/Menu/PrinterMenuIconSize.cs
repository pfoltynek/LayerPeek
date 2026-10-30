using LayerPeek.App.Printers;

namespace LayerPeek.App.Tray.Menu;

/// <summary>
/// How large the printer icons of the tray menu are (T-039 Windows, T-116 macOS) and how many pixels their bitmaps have.
/// The menu shows one bitmap per item at <see cref="Size"/>, so the bitmap has <see cref="Size"/> × <see cref="Scaling"/>
/// physical pixels, rounded up to a pre-rendered size (<see cref="PrinterIllustrations.SelectPixelSize"/>: never enlarged).
/// <list type="bullet">
/// <item>Windows: Avalonia draws the tray menu itself and fits the item icon into 16 × 16 DIP; the scaling is the system
/// scaling (16 / 20 / 24 / 32 px at 100 / 125 / 150 / 200 %).</item>
/// <item>macOS: the menu is a native <c>NSMenu</c>. Avalonia.Native 12.1.4 (<c>AvnAppMenuItem::SetIcon</c> in
/// <c>libAvaloniaNative.dylib</c>; the managed side passes the bitmap as PNG) makes an <c>NSImage</c> of the PNG and sets
/// its size in points to <c>floor(menuFont.pointSize × 1.333333)</c> high, the width in proportion, whatever the pixel
/// size of the bitmap is. The default menu font has 14 pt, so the icon is 18 × 18 pt: 18 px on an @1x display, 36 px on
/// Retina (@2x). The image has a single representation, so the bitmap is made for the highest backing scale factor of the
/// displays (AppKit scales it down on an @1x display).</item>
/// </list>
/// </summary>
internal readonly record struct PrinterMenuIconSize
{
    /// <summary>Icon size in the Windows tray menu, in DIP.</summary>
    public const double WindowsSize = 16;

    /// <summary>Default size of the macOS menu font (<c>[NSFont menuFontOfSize:0]</c>) when it cannot be read.</summary>
    public const double MacDefaultMenuFontSize = 14;

    /// <summary>Backing scale factor when macOS reports no display (T-099: start without an active display): Retina.</summary>
    public const double MacFallbackScaling = 2;

    /// <summary>The factor Avalonia.Native 12.1.4 applies to the menu font size to get the icon height in points.</summary>
    internal const double AvaloniaNativeIconToMenuFont = 1.333333;

    private PrinterMenuIconSize(double size, double scaling)
    {
        Size = size;
        Scaling = Valid(scaling) ? scaling : 1;
        PixelSize = PrinterIllustrations.SelectPixelSize(Size, Scaling);
    }

    /// <summary>Icon size in the menu: DIP on Windows, points on macOS.</summary>
    public double Size { get; }

    /// <summary>Physical pixels per DIP / point the bitmap is made for.</summary>
    public double Scaling { get; }

    /// <summary>Pixel size (width = height) of every icon bitmap.</summary>
    public int PixelSize { get; }

    /// <summary>The Windows tray menu at the system scaling <paramref name="scaling"/> (1.0 = 100 %).</summary>
    public static PrinterMenuIconSize Windows(double scaling) => new(WindowsSize, scaling);

    /// <summary>
    /// The macOS menu bar menu with the menu font of <paramref name="menuFontPointSize"/> points and the highest backing
    /// scale factor <paramref name="highestBackingScale"/> of the displays; either one <see langword="null"/> (or not a
    /// positive number) when macOS cannot tell: <see cref="MacDefaultMenuFontSize"/> / <see cref="MacFallbackScaling"/>.
    /// </summary>
    public static PrinterMenuIconSize MacOS(double? menuFontPointSize, double? highestBackingScale) =>
        new(MacIconPoints(menuFontPointSize is { } font && Valid(font) ? font : MacDefaultMenuFontSize),
            highestBackingScale is { } scale && Valid(scale) ? scale : MacFallbackScaling);

    /// <summary>The height in points Avalonia.Native gives a menu item image for the menu font <paramref name="menuFontPointSize"/>.</summary>
    public static double MacIconPoints(double menuFontPointSize) => Math.Floor(menuFontPointSize * AvaloniaNativeIconToMenuFont);

    private static bool Valid(double value) => double.IsFinite(value) && value > 0;
}
