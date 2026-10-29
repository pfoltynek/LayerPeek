using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The bundle identifier of the running process on macOS (T-020), read through CoreFoundation (<c>CFBundleGetMainBundle</c>,
/// no Objective-C, nothing is loaded or registered). An app started from its <c>.app</c> (also its executable started
/// directly) has the <c>CFBundleIdentifier</c> of its <c>Info.plist</c>; a development build (<c>dotnet run</c>) and the test
/// host have none. The User Notifications framework needs one: without it the notification center raises an
/// Objective-C exception, which would end the process.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacMainBundle
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>The identifier of the main bundle, or <see langword="null"/> when the process has none.</summary>
    public static string? Identifier()
    {
        nint bundle = CFBundleGetMainBundle();
        nint identifier = bundle == 0 ? 0 : CFBundleGetIdentifier(bundle);
        if (identifier == 0)
        {
            return null;
        }

        // Both are "Get" references: owned by the bundle, never released here.
        nint length = CFStringGetLength(identifier);
        if (length <= 0 || length > 1024)
        {
            return null;
        }

        char[] characters = new char[length];
        fixed (char* buffer = characters)
        {
            CFStringGetCharacters(identifier, new CFRange(0, length), buffer);
        }

        return new string(characters);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CFRange(nint location, nint length)
    {
        public readonly nint Location = location;
        public readonly nint Length = length;
    }

    [LibraryImport(CoreFoundation)]
    private static partial nint CFBundleGetMainBundle();

    [LibraryImport(CoreFoundation)]
    private static partial nint CFBundleGetIdentifier(nint bundle);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(nint text);

    [LibraryImport(CoreFoundation)]
    private static partial void CFStringGetCharacters(nint text, CFRange range, char* buffer);
}
