using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LayerPeek.Core.Secrets;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The SecItem API of Security.framework (SecItem.h) and the few CoreFoundation calls needed to build its queries.
/// Both frameworks are loaded by absolute path (never a bare name that would first be looked up next to the app).
/// Blittable signatures only (<c>[LibraryImport]</c>, Native AOT safe); every CF object is a plain <see cref="nint"/>
/// that the caller releases with <see cref="Release"/>. A CF object that cannot be created (out of memory) is a
/// <see cref="SecretStoreException"/>, like a failed Keychain call.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class KeychainNative
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary><c>errSecSuccess</c>.</summary>
    public const int Success = 0;

    /// <summary><c>errSecItemNotFound</c>.</summary>
    public const int ItemNotFound = -25300;

    /// <summary><c>errSecDuplicateItem</c>.</summary>
    public const int DuplicateItem = -25299;

    /// <summary><c>errSecInteractionNotAllowed</c>: the operation needs a dialog (e.g. unlocking the keychain) that may not be shown.</summary>
    public const int InteractionNotAllowed = -25308;

    // Not readonly fields of a static constructor: a missing export must surface where it is used, not as a type
    // initialization failure of the whole class.
    private static readonly Lazy<Constants> LazyConstants = new(Constants.Load);

    /// <summary>The exported constants (<c>kSec*</c> keys and values, <c>kCF*</c> callbacks), resolved once.</summary>
    public static Constants Const => LazyConstants.Value;

    /// <summary><c>CFRelease</c> of a non-null CF object (null is ignored, unlike in CFRelease itself).</summary>
    public static void Release(nint cf)
    {
        if (cf != 0)
        {
            CFRelease(cf);
        }
    }

    /// <summary>A new CFString with the UTF-16 code units of <paramref name="value"/> (release it).</summary>
    public static nint CreateString(string value)
    {
        fixed (char* chars = value)
        {
            nint result = CFStringCreateWithCharacters(0, chars, value.Length);
            return result != 0 ? result : throw new SecretStoreException("CFStringCreateWithCharacters failed.");
        }
    }

    /// <summary>The managed copy of a CFString (<see langword="null"/> for anything that is not a CFString).</summary>
    public static string? ToManagedString(nint cfString)
    {
        if (cfString == 0 || CFGetTypeID(cfString) != CFStringGetTypeID())
        {
            return null;
        }

        nint length = CFStringGetLength(cfString);
        if (length == 0)
        {
            return string.Empty;
        }

        return string.Create(checked((int)length), cfString, static (span, source) =>
        {
            fixed (char* buffer = span)
            {
                CFStringGetCharacters(source, new CFRange(0, span.Length), buffer);
            }
        });
    }

    /// <summary>A new CFDictionary that retains its keys and values (release it; the caller still owns its own references).</summary>
    public static nint CreateDictionary(ReadOnlySpan<nint> keys, ReadOnlySpan<nint> values)
    {
        if (keys.Length != values.Length)
        {
            throw new ArgumentException("Every key needs exactly one value.", nameof(values));
        }

        Constants constants = Const;
        fixed (nint* keyPointer = keys)
        fixed (nint* valuePointer = values)
        {
            nint result = CFDictionaryCreate(0, keyPointer, valuePointer, keys.Length, constants.DictionaryKeyCallBacks, constants.DictionaryValueCallBacks);
            return result != 0 ? result : throw new SecretStoreException("CFDictionaryCreate failed.");
        }
    }

    /// <summary>
    /// A new mutable CFData with a copy of <paramref name="bytes"/>. Mutable so that the owner can wipe the copy before
    /// releasing it (<see cref="ZeroAndRelease"/>).
    /// </summary>
    public static nint CreateWipeableData(ReadOnlySpan<byte> bytes)
    {
        nint data = CFDataCreateMutable(0, bytes.Length);
        if (data == 0)
        {
            throw new SecretStoreException("CFDataCreateMutable failed.");
        }

        fixed (byte* pointer = bytes)
        {
            CFDataAppendBytes(data, pointer, bytes.Length);
        }

        return data;
    }

    /// <summary>Overwrites the bytes of a mutable CFData with zeros and releases it.</summary>
    public static void ZeroAndRelease(nint mutableData)
    {
        if (mutableData == 0)
        {
            return;
        }

        nint length = CFDataGetLength(mutableData);
        if (length > 0)
        {
            new Span<byte>(CFDataGetMutableBytePtr(mutableData), checked((int)length)).Clear();
        }

        CFRelease(mutableData);
    }

    /// <summary>
    /// The bytes of a CFData (not copied; valid until it is released), or <see langword="false"/> when
    /// <paramref name="cf"/> is not a CFData.
    /// </summary>
    public static bool TryGetBytes(nint cf, out Span<byte> bytes)
    {
        if (cf == 0 || CFGetTypeID(cf) != CFDataGetTypeID())
        {
            bytes = default;
            return false;
        }

        nint length = CFDataGetLength(cf);
        // CFDataGetBytePtr is const, but the data returned by SecItemCopyMatching belongs to the caller alone, so its
        // plaintext may (and must) be wiped before the release.
        bytes = length == 0 ? default : new Span<byte>(CFDataGetBytePtr(cf), checked((int)length));
        return true;
    }

    /// <summary>The number of elements of a CFArray (0 for anything that is not a CFArray).</summary>
    public static int ArrayCount(nint cf) =>
        cf != 0 && CFGetTypeID(cf) == CFArrayGetTypeID() ? checked((int)CFArrayGetCount(cf)) : 0;

    /// <summary>An element of a CFArray (not retained).</summary>
    public static nint ArrayItem(nint array, int index) => CFArrayGetValueAtIndex(array, index);

    /// <summary>The value of <paramref name="key"/> in a CFDictionary (not retained), or 0.</summary>
    public static nint DictionaryValue(nint dictionary, nint key) =>
        dictionary != 0 && CFGetTypeID(dictionary) == CFDictionaryGetTypeID() ? CFDictionaryGetValue(dictionary, key) : 0;

    /// <summary>The system's description of an OSStatus (e.g. "The specified item could not be found in the keychain.").</summary>
    public static string? ErrorMessage(int status)
    {
        nint message = SecCopyErrorMessageString(status, 0);
        try
        {
            return ToManagedString(message);
        }
        finally
        {
            Release(message);
        }
    }

    [LibraryImport(Security)]
    public static partial int SecItemAdd(nint attributes, nint* result);

    [LibraryImport(Security)]
    public static partial int SecItemCopyMatching(nint query, out nint result);

    [LibraryImport(Security)]
    public static partial int SecItemUpdate(nint query, nint attributesToUpdate);

    [LibraryImport(Security)]
    public static partial int SecItemDelete(nint query);

    [LibraryImport(Security)]
    private static partial nint SecCopyErrorMessageString(int status, nint reserved);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint cf);

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFGetTypeID(nint cf);

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFStringGetTypeID();

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFDataGetTypeID();

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFArrayGetTypeID();

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFDictionaryGetTypeID();

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringCreateWithCharacters(nint allocator, char* chars, nint numChars);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(nint theString);

    [LibraryImport(CoreFoundation)]
    private static partial void CFStringGetCharacters(nint theString, CFRange range, char* buffer);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint numValues, nint keyCallBacks, nint valueCallBacks);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDictionaryGetValue(nint theDict, nint key);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataCreateMutable(nint allocator, nint capacity);

    [LibraryImport(CoreFoundation)]
    private static partial void CFDataAppendBytes(nint theData, byte* bytes, nint length);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetLength(nint theData);

    [LibraryImport(CoreFoundation)]
    private static partial byte* CFDataGetBytePtr(nint theData);

    [LibraryImport(CoreFoundation)]
    private static partial byte* CFDataGetMutableBytePtr(nint theData);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetCount(nint theArray);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFArrayGetValueAtIndex(nint theArray, nint index);

    // Only used to build the CFStringGetCharacters argument.
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CFRange(nint location, nint length)
    {
        public readonly nint Location = location;
        public readonly nint Length = length;
    }

    /// <summary>
    /// Exported constants. The <c>kSec*</c>/<c>kCFBoolean*</c> exports are variables holding a CF object (read once);
    /// the dictionary callbacks are structs whose address is passed on. The frameworks are part of the OS and never
    /// unloaded, so the handles are not freed.
    /// </summary>
    internal sealed class Constants
    {
        private Constants(nint security, nint coreFoundation)
        {
            Class = Read(security, "kSecClass");
            ClassGenericPassword = Read(security, "kSecClassGenericPassword");
            AttrService = Read(security, "kSecAttrService");
            AttrAccount = Read(security, "kSecAttrAccount");
            ValueData = Read(security, "kSecValueData");
            ReturnData = Read(security, "kSecReturnData");
            ReturnAttributes = Read(security, "kSecReturnAttributes");
            MatchLimit = Read(security, "kSecMatchLimit");
            MatchLimitOne = Read(security, "kSecMatchLimitOne");
            MatchLimitAll = Read(security, "kSecMatchLimitAll");
            True = Read(coreFoundation, "kCFBooleanTrue");
            DictionaryKeyCallBacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryKeyCallBacks");
            DictionaryValueCallBacks = NativeLibrary.GetExport(coreFoundation, "kCFTypeDictionaryValueCallBacks");
        }

        public nint Class { get; }

        public nint ClassGenericPassword { get; }

        public nint AttrService { get; }

        public nint AttrAccount { get; }

        public nint ValueData { get; }

        public nint ReturnData { get; }

        public nint ReturnAttributes { get; }

        public nint MatchLimit { get; }

        public nint MatchLimitOne { get; }

        public nint MatchLimitAll { get; }

        public nint True { get; }

        public nint DictionaryKeyCallBacks { get; }

        public nint DictionaryValueCallBacks { get; }

        internal static Constants Load() => new(NativeLibrary.Load(Security), NativeLibrary.Load(CoreFoundation));

        private static nint Read(nint library, string name) => *(nint*)NativeLibrary.GetExport(library, name);
    }
}
