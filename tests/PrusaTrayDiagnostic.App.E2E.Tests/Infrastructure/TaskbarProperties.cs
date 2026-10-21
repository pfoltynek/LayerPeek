using System.Runtime.InteropServices;

namespace PrusaTrayDiagnostic.App.E2E.Tests.Infrastructure;

/// <summary>
/// Reads back the taskbar properties of another process's window (T-043): <c>System.AppUserModel.ID</c> and
/// <c>System.AppUserModel.PreventPinning</c> through <c>SHGetPropertyStoreForWindow</c>. Read-only.
/// </summary>
internal static partial class TaskbarProperties
{
    private const ushort VtEmpty = 0;
    private const ushort VtBool = 11;
    private const ushort VtLpwstr = 31;

    private static readonly Guid AppUserModelFormat = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    private static readonly Guid PropertyStoreId = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary>The window's AppUserModelID, or <see langword="null"/> when it has none.</summary>
    public static string? AppUserModelId(nint hwnd) => Read(hwnd, 5, static (in PropVariant value) =>
        value.Type == VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) : null);

    /// <summary>The window's PreventPinning value, or <see langword="null"/> when it is not set.</summary>
    public static bool? PreventPinning(nint hwnd) => Read(hwnd, 9, static (in PropVariant value) =>
        value.Type == VtBool ? (bool?)(value.Short != 0) : value.Type == VtEmpty ? null : throw new InvalidOperationException($"Unexpected VARTYPE {value.Type}."));

    private delegate T Reader<T>(in PropVariant value);

    private static T Read<T>(nint hwnd, uint propertyId, Reader<T> reader)
    {
        Guid iid = PropertyStoreId;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd, ref iid, out IPropertyStore store));
        try
        {
            PropertyKey key = new() { FormatId = AppUserModelFormat, PropertyId = propertyId };
            PropVariant value = default;
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out value));
            try
            {
                return reader(in value);
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(store);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public short Short;

        [FieldOffset(8)]
        public nint Pointer;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out PropertyKey key);

        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);

        [PreserveSig]
        int SetValue(ref PropertyKey key, ref PropVariant value);

        [PreserveSig]
        int Commit();
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid iid, out IPropertyStore store);

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);
}
