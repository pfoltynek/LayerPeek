using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.Windows;

/// <summary>
/// Windows Credential Manager API (wincred.h).
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class CredentialManagerNative
{
    public const uint CredTypeGeneric = 1;

    /// <summary>Kept only for the current logon session; never written to disk or roamed.</summary>
    public const uint CredPersistSession = 1;

    /// <summary>Persisted for the current user and roamed with a roaming/domain profile (like %APPDATA%).</summary>
    public const uint CredPersistEnterprise = 3;

    /// <summary>CRED_MAX_CREDENTIAL_BLOB_SIZE.</summary>
    public const int MaxCredentialBlobSize = 5 * 512;

    public const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential)]
    public struct Credential
    {
        public uint Flags;
        public uint Type;
        public char* TargetName;
        public char* Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredWrite(Credential* credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredRead(string targetName, uint type, uint flags, out Credential* credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CredDelete(string targetName, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    public static partial void CredFree(void* buffer);
}
