using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>COM / WinRT runtime functions (ole32, combase, shell32). Source-generated marshalling only (Native AOT).</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class ComNative
{
    public const int SOk = 0;
    public const int SFalse = 1;
    public const int RpcEChangedMode = unchecked((int)0x80010106);
    public const uint CoInitMultithreaded = 0;
    public const uint ClsCtxInprocServer = 1;
    public const uint ClsCtxLocalServer = 4;

    /// <summary>REGCLS_MULTIPLEUSE: one registered class object serves every activation.</summary>
    public const uint RegClsMultipleUse = 1;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);

    [LibraryImport("ole32.dll")]
    public static partial int PropVariantClear(PropVariant* value);

    [LibraryImport("ole32.dll")]
    public static partial int CoGetClassObject(in Guid classId, uint context, nint serverInfo, in Guid interfaceId, out nint classObject);

    [LibraryImport("ole32.dll")]
    public static partial int CoRegisterClassObject(in Guid classId, nint unknown, uint context, uint flags, out uint cookie);

    [LibraryImport("ole32.dll")]
    public static partial int CoRevokeClassObject(uint cookie);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int WindowsCreateString(string? source, uint length, out nint hstring);

    [LibraryImport("combase.dll")]
    public static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    public static partial char* WindowsGetStringRawBuffer(nint hstring, out uint length);

    [LibraryImport("combase.dll")]
    public static partial int RoGetActivationFactory(nint activatableClassId, in Guid interfaceId, out nint factory);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}

/// <summary>
/// Joins the calling thread to the multithreaded COM apartment for the lifetime of the scope (balanced
/// <c>CoUninitialize</c>). A thread that is already a single-threaded apartment (e.g. the UI thread) stays one.
/// Dispose on the thread that created it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ComApartment : IDisposable
{
    private bool _initialized;

    private ComApartment(bool initialized) => _initialized = initialized;

    public static ComApartment EnterMultithreaded()
    {
        int hr = ComNative.CoInitializeEx(0, ComNative.CoInitMultithreaded);
        if (hr == ComNative.RpcEChangedMode)
        {
            // Already STA: COM works, the thread's owner uninitializes it.
            return new ComApartment(initialized: false);
        }

        Marshal.ThrowExceptionForHR(hr);
        // S_OK and S_FALSE both need a matching CoUninitialize.
        return new ComApartment(initialized: true);
    }

    public void Dispose()
    {
        if (_initialized)
        {
            _initialized = false;
            ComNative.CoUninitialize();
        }
    }
}

/// <summary>A WinRT string (HSTRING) owned by the scope.</summary>
[SupportedOSPlatform("windows")]
internal readonly struct HString : IDisposable
{
    private HString(nint handle) => Handle = handle;

    /// <summary>0 for the empty string (a valid HSTRING).</summary>
    public nint Handle { get; }

    public static HString Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Marshal.ThrowExceptionForHR(ComNative.WindowsCreateString(value, (uint)value.Length, out nint handle));
        return new HString(handle);
    }

    /// <summary>Copies <paramref name="handle"/> into a managed string and deletes the HSTRING.</summary>
    public static unsafe string? MoveToManaged(nint handle)
    {
        if (handle == 0)
        {
            return string.Empty;
        }

        try
        {
            char* buffer = ComNative.WindowsGetStringRawBuffer(handle, out uint length);
            return buffer is null ? null : new string(buffer, 0, checked((int)length));
        }
        finally
        {
            _ = ComNative.WindowsDeleteString(handle);
        }
    }

    public void Dispose()
    {
        if (Handle != 0)
        {
            _ = ComNative.WindowsDeleteString(Handle);
        }
    }
}

/// <summary>
/// Creates and releases runtime callable wrappers through source-generated COM (<see cref="StrategyBasedComWrappers"/>).
/// Every wrapper is a unique instance released deterministically by <see cref="Release"/>, not by the GC.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ComObjects
{
    public static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Wraps <paramref name="unknown"/> as <typeparamref name="T"/>, taking over the caller's reference.</summary>
    public static T Wrap<T>(nint unknown)
        where T : class
    {
        if (unknown == 0)
        {
            throw new InvalidOperationException("The COM call returned no object.");
        }

        try
        {
            return Borrow<T>(unknown);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>Wraps <paramref name="unknown"/> with a reference of its own; the caller keeps its reference.</summary>
    public static T Borrow<T>(nint unknown)
        where T : class
    {
        if (unknown == 0)
        {
            throw new InvalidOperationException("The COM call returned no object.");
        }

        object wrapper = Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
        if (wrapper is T typed)
        {
            return typed;
        }

        Release(wrapper);
        throw new InvalidCastException($"The COM object does not implement {typeof(T).Name}.");
    }

    /// <summary>Releases the native object of a wrapper now; safe for <see langword="null"/>.</summary>
    public static void Release(object? wrapper)
    {
        if (wrapper is ComObject comObject)
        {
            comObject.FinalRelease();
        }
    }

    /// <summary>The native interface pointer of a managed COM class instance (one reference, release it).</summary>
    public static nint GetInterfacePointer(object managed, in Guid interfaceId)
    {
        nint unknown = Wrappers.GetOrCreateComInterfaceForObject(managed, CreateComInterfaceFlags.None);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in interfaceId, out nint pointer));
            return pointer;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}

/// <summary>PROPVARIANT (propidl.h); only the string case is used.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public const ushort VtEmpty = 0;
    public const ushort VtLpwstr = 31;
    public const ushort VtClsid = 72;

    public ushort VarType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public nint Value;
    public nint Value2;
}

/// <summary>PROPERTYKEY (wtypes.h).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}
