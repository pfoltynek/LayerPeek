using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The few Objective-C runtime calls of the macOS menu bar app (T-019): bringing the app to the front, the Login Item
/// of <c>SMAppService</c> and (T-020) the User Notifications framework with its delegate class registered at run time. Native AOT safe: <c>[LibraryImport]</c> by absolute path with blittable signatures, one entry
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

    /// <summary>
    /// A new autoreleased <c>NSString</c> with the UTF-16 text of <paramref name="text"/> (any character, also a NUL);
    /// call inside an autorelease pool. Foundation must be loaded.
    /// </summary>
    public static nint NSString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        fixed (char* characters = text)
        {
            return Send(Class("NSString"), Selector("stringWithCharacters:length:"), (nint)characters, text.Length);
        }
    }

    /// <summary><c>[receiver release]</c> for an object this code owns (0 is ignored).</summary>
    public static void Release(nint receiver)
    {
        if (receiver != 0)
        {
            SendVoid(receiver, Selector("release"));
        }
    }

    /// <summary>Whether <paramref name="receiver"/> is an instance of the class <paramref name="className"/> or of a subclass.</summary>
    public static bool IsKindOf(nint receiver, string className)
    {
        nint type = Class(className);
        return receiver != 0 && type != 0 && SendBool(receiver, Selector("isKindOfClass:"), type);
    }

    /// <summary>
    /// The class <paramref name="name"/> as a subclass of <c>NSObject</c> with the given methods, registered at run time;
    /// the existing one when this process registered it already (a class pair can exist once per process). Never freed.
    /// </summary>
    /// <param name="protocol">A protocol the class adopts (when the runtime knows it); <see langword="null"/>: none.</param>
    /// <param name="methods">Selector, implementation (an <c>UnmanagedCallersOnly</c> function) and type encoding.</param>
    /// <exception cref="InvalidOperationException">The runtime refused the class or one of its methods.</exception>
    public static nint DefineClass(string name, string? protocol, ReadOnlySpan<(string Selector, nint Implementation, string Types)> methods)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        nint existing = GetClass(name);
        if (existing != 0)
        {
            return existing;
        }

        nint type = AllocateClassPair(GetClass("NSObject"), name, 0);
        if (type == 0)
        {
            throw new InvalidOperationException($"The Objective-C runtime refused the class {name}.");
        }

        foreach ((string selector, nint implementation, string types) in methods)
        {
            if (!AddMethod(type, RegisterName(selector), implementation, types))
            {
                DisposeClassPair(type);
                throw new InvalidOperationException($"The Objective-C runtime refused the method {selector} of {name}.");
            }
        }

        if (protocol is not null && GetProtocol(protocol) is var adopted && adopted != 0)
        {
            _ = AddProtocol(type, adopted);
        }

        RegisterClassPair(type);
        return type;
    }

    [LibraryImport(LibObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(LibObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterName(string name);

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPush")]
    private static partial nint AutoreleasePoolPush();

    [LibraryImport(LibObjC, EntryPoint = "objc_autoreleasePoolPop")]
    private static partial void AutoreleasePoolPop(nint pool);

    [LibraryImport(LibObjC, EntryPoint = "objc_allocateClassPair", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint AllocateClassPair(nint superclass, string name, nuint extraBytes);

    [LibraryImport(LibObjC, EntryPoint = "objc_registerClassPair")]
    private static partial void RegisterClassPair(nint type);

    [LibraryImport(LibObjC, EntryPoint = "objc_disposeClassPair")]
    private static partial void DisposeClassPair(nint type);

    [LibraryImport(LibObjC, EntryPoint = "class_addMethod", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AddMethod(nint type, nint selector, nint implementation, string types);

    [LibraryImport(LibObjC, EntryPoint = "objc_getProtocol", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetProtocol(string name);

    [LibraryImport(LibObjC, EntryPoint = "class_addProtocol")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool AddProtocol(nint type, nint protocol);

    /// <summary><c>id</c> / <c>NSInteger</c> result, no arguments.</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector);

    /// <summary><c>id</c> / <c>NSInteger</c> result (or none), one pointer-sized argument (<c>setTitle:</c>, <c>objectForKey:</c>...).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint argument);

    /// <summary><c>id</c> result (or none), two pointer-sized arguments (<c>dictionaryWithObject:forKey:</c>...).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint first, nint second);

    /// <summary><c>id</c> result, three pointer-sized arguments (<c>requestWithIdentifier:content:trigger:</c>).</summary>
    [LibraryImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static partial nint Send(nint receiver, nint selector, nint first, nint second, nint third);

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
