using System.Runtime.InteropServices;

namespace LayerPeek.App.UiTests.SystemFont;

/// <summary>
/// Width of a single-line text in the macOS system font as AppKit lays it out (T-214 calibration): a CTLine of the
/// system UI font, which is what <c>NSString.size(withAttributes:)</c> with <c>NSFont.systemFont(ofSize:weight:)</c>
/// measures (CoreText applies the optical size and the tracking of the size, as AppKit does). macOS only.
/// </summary>
internal static partial class CoreTextWidth
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";

    /// <summary>kCTFontUIFontSystem: <c>NSFont.systemFont(ofSize:)</c>.</summary>
    private const uint SystemUiFont = 2;

    /// <summary>kCFNumberDoubleType.</summary>
    private const int DoubleNumber = 13;

    /// <summary>
    /// The width in points (= DIP) of <paramref name="text"/> in the system font of <paramref name="size"/> points and
    /// the AppKit weight <paramref name="weight"/> (<c>NSFont.Weight</c>: 0 regular, 0.3 semibold, 0.4 bold).
    /// </summary>
    public static unsafe double Measure(string text, double size, double weight)
    {
        ArgumentNullException.ThrowIfNull(text);
        nint coreFoundation = NativeLibrary.Load(CoreFoundation);
        nint coreText = NativeLibrary.Load(CoreText);
        nint keyCallbacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks");
        nint valueCallbacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks");
        nint fontAttribute = *(nint*)NativeLibrary.GetExport(coreText, "kCTFontAttributeName");
        nint traitsAttribute = *(nint*)NativeLibrary.GetExport(coreText, "kCTFontTraitsAttribute");
        nint weightTrait = *(nint*)NativeLibrary.GetExport(coreText, "kCTFontWeightTrait");

        List<nint> owned = [];
        try
        {
            nint systemFont = Own(owned, CTFontCreateUIFontForLanguage(SystemUiFont, size, 0));
            nint weightNumber = Own(owned, CFNumberCreate(0, DoubleNumber, &weight));
            nint traits = Own(owned, Dictionary(weightTrait, weightNumber, keyCallbacks, valueCallbacks));
            nint attributes = Own(owned, Dictionary(traitsAttribute, traits, keyCallbacks, valueCallbacks));
            nint descriptor = Own(owned, CTFontDescriptorCreateWithAttributes(attributes));
            nint font = Own(owned, CTFontCreateCopyWithAttributes(systemFont, size, 0, descriptor));
            nint stringAttributes = Own(owned, Dictionary(fontAttribute, font, keyCallbacks, valueCallbacks));
            nint characters;
            fixed (char* chars = text)
            {
                characters = Own(owned, CFStringCreateWithCharacters(0, chars, text.Length));
            }

            nint attributed = Own(owned, CFAttributedStringCreate(0, characters, stringAttributes));
            nint line = Own(owned, CTLineCreateWithAttributedString(attributed));
            return CTLineGetTypographicBounds(line, null, null, null);
        }
        finally
        {
            for (int i = owned.Count - 1; i >= 0; i--)
            {
                CFRelease(owned[i]);
            }
        }
    }

    private static unsafe nint Dictionary(nint key, nint value, nint keyCallbacks, nint valueCallbacks) =>
        CFDictionaryCreate(0, &key, &value, 1, keyCallbacks, valueCallbacks);

    private static nint Own(List<nint> owned, nint reference)
    {
        if (reference == 0)
        {
            throw new InvalidOperationException("A CoreText / CoreFoundation call returned NULL.");
        }

        owned.Add(reference);
        return reference;
    }

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint reference);

    [LibraryImport(CoreFoundation)]
    private static unsafe partial nint CFStringCreateWithCharacters(nint allocator, char* characters, nint length);

    [LibraryImport(CoreFoundation)]
    private static unsafe partial nint CFNumberCreate(nint allocator, int type, double* value);

    [LibraryImport(CoreFoundation)]
    private static unsafe partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallbacks, nint valueCallbacks);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFAttributedStringCreate(nint allocator, nint text, nint attributes);

    [LibraryImport(CoreText)]
    private static partial nint CTFontCreateUIFontForLanguage(uint type, double size, nint language);

    [LibraryImport(CoreText)]
    private static partial nint CTFontDescriptorCreateWithAttributes(nint attributes);

    [LibraryImport(CoreText)]
    private static partial nint CTFontCreateCopyWithAttributes(nint font, double size, nint matrix, nint descriptor);

    [LibraryImport(CoreText)]
    private static partial nint CTLineCreateWithAttributedString(nint text);

    [LibraryImport(CoreText)]
    private static unsafe partial double CTLineGetTypographicBounds(nint line, double* ascent, double* descent, double* leading);
}
