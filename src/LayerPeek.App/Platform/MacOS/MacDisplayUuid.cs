using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The persistent UUID macOS keeps for a display (T-038), from ColorSync <c>CGDisplayCreateUUIDFromDisplayID</c>: the
/// identifier System Settings and the display preferences use for a physical monitor, stable across restarts and
/// reconnects. It is the camera window's device path on macOS (<see cref="Core.Cameras.CameraScreen.DevicePath"/>),
/// because Avalonia's <c>Screen.DisplayName</c> (<c>NSScreen.localizedName</c>) is not stable: macOS numbers identical
/// monitors ("DELL U2723QE (1)", "(2)") in an order that may change at the next start or reconnect, and the built-in
/// display's name follows the system language. Loaded by absolute path; blittable signatures (<c>[LibraryImport]</c>,
/// Native AOT safe).
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacDisplayUuid
{
    private const string ColorSync = "/System/Library/Frameworks/ColorSync.framework/ColorSync";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary><c>kCFStringEncodingASCII</c>: a UUID string has only hex digits and hyphens.</summary>
    private const uint AsciiEncoding = 0x0600;

    /// <summary>
    /// The UUID of the display <paramref name="displayId"/> (a <c>CGDirectDisplayID</c>) as upper-case
    /// <c>XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX</c>; <see langword="null"/> when macOS has none for it (an unknown or
    /// disconnected display, <c>kCGNullDirectDisplay</c>).
    /// </summary>
    public static string? Get(uint displayId)
    {
        // kCGNullDirectDisplay: ColorSync answers it with the UUID of the main display (seen on macOS 27), which would
        // give an unknown screen the identity of another one.
        if (displayId == 0)
        {
            return null;
        }

        nint uuid = CGDisplayCreateUUIDFromDisplayID(displayId);
        if (uuid == 0)
        {
            return null;
        }

        nint text = 0;
        try
        {
            text = CFUUIDCreateString(0, uuid);
            if (text == 0)
            {
                return null;
            }

            // 36 characters and the terminating NUL; a little room so that an unexpected longer text fails the parse below.
            byte* buffer = stackalloc byte[64];
            return CFStringGetCString(text, buffer, 64, AsciiEncoding) != 0
                ? Normalize(Marshal.PtrToStringAnsi((nint)buffer))
                : null;
        }
        finally
        {
            if (text != 0)
            {
                CFRelease(text);
            }

            CFRelease(uuid);
        }
    }

    /// <summary>
    /// <paramref name="text"/> as an upper-case UUID when it is one (and not the all-zero UUID), else <see langword="null"/>.
    /// </summary>
    internal static string? Normalize(string? text) =>
        Guid.TryParseExact(text, "D", out Guid uuid) && uuid != Guid.Empty ? uuid.ToString("D").ToUpperInvariant() : null;

    // CFUUIDRef CGDisplayCreateUUIDFromDisplayID(uint32_t displayID); (ColorSync, "Create" rule: the caller releases it)
    [LibraryImport(ColorSync)]
    private static partial nint CGDisplayCreateUUIDFromDisplayID(uint displayId);

    // CFStringRef CFUUIDCreateString(CFAllocatorRef alloc, CFUUIDRef uuid);
    [LibraryImport(CoreFoundation)]
    private static partial nint CFUUIDCreateString(nint allocator, nint uuid);

    // Boolean CFStringGetCString(CFStringRef theString, char *buffer, CFIndex bufferSize, CFStringEncoding encoding);
    [LibraryImport(CoreFoundation)]
    private static partial byte CFStringGetCString(nint text, byte* buffer, nint bufferSize, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint cf);
}
