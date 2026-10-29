using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The few Objective-C runtime calls of the macOS menu bar app (T-019): bringing the app to the front and the Login Item
/// of <c>SMAppService</c>. Native AOT safe: <c>[LibraryImport]</c> by absolute path with blittable signatures, one entry
/// point per <c>objc_msgSend</c> shape (on arm64 every message must be sent through a prototype of exactly its arguments),
/// no Objective-C bridge, no reflection. Every object is a plain <see cref="nint"/>.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class ObjCRuntime
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    /// <summary>The class of that name, or 0 when no loaded image defines it (nothing is loaded for it).</summary>
    public static nint Class(string name) => GetClass(name);

    /// <summary>The selector of that name (registered once by the runtime, never freed).</summary>
    public static nint Selector(string name) => RegisterName(name);

    /// <summary>Whether <paramref name="receiver"/> implements <paramref name="selector"/>.</summary>
    public static bool RespondsTo(nint receiver, string selector) =>
        receiver != 0 && SendBool(receiver, Selector("respondsToSelector:"), Selector(selector));

    /// <summary>The text of an <c>NSString</c>; <see langword="null"/> for 0.</summary>
    public static string? ToText(nint nsString) =>
        nsString == 0 ? null : Marshal.PtrToStringUTF8(Send(nsString, Selector("UTF8String")));

    /// <summary>
    /// Code and description of an <c>NSError</c> (0: none); for messages and logs only, never shown with secrets (the
    /// errors of these calls carry none).
    /// </summary>
    public static (long Code, string? Description) Describe(nint nsError) =>
        nsError == 0 ? (0, null) : ((long)Send(nsError, Selector("code")), ToText(Send(nsError, Selector("localizedDescription"))));

    /// <summary>
    /// Runs <paramref name="action"/> inside an autorelease pool: the objects the calls return autoreleased are freed at
    /// the end, also on thread pool threads, which have no pool of their own.
    /// </summary>
    public static T InAutoreleasePool<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        nint pool = AutoreleasePoolPush();
        try
        {
            return action();
        }
        finally
        {
            AutoreleasePoolPop(pool);
        }
    }

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterName(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPush")]
    private static partial nint AutoreleasePoolPush();

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPop")]
    private static partial void AutoreleasePoolPop(nint pool);

    /// <summary><c>id</c> / <c>NSInteger</c> result, no arguments.</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector);

    /// <summary>No result, one <c>BOOL</c> argument (<c>activateIgnoringOtherApps:</c>).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoid(nint receiver, nint selector, byte argument);

    /// <summary>No result, no arguments (<c>activate</c>).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial void SendVoid(nint receiver, nint selector);

    /// <summary><c>BOOL</c> result, one pointer argument (<c>respondsToSelector:</c>, <c>registerAndReturnError:</c>).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBool(nint receiver, nint selector, nint argument);

    /// <summary><c>BOOL</c> result, an <c>NSError **</c> out argument.</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SendBool(nint receiver, nint selector, nint* error);
}
