using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The few libc calls of the macOS single-instance guard (T-053, finding L2): who this process is, who is on the other end
/// of a Unix domain socket, and who owns a folder. Loaded by absolute path from libSystem (where libc lives on macOS),
/// never by a bare name that would first be looked up next to the app. Blittable signatures only (<c>[LibraryImport]</c>,
/// Native AOT safe).
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacUnixNative
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    /// <summary><c>ENOENT</c>: the path does not exist.</summary>
    public const int NoSuchFile = 2;

    /// <summary>
    /// Bytes reserved for <c>struct stat</c> (144 bytes with 64-bit inodes, the only layout on arm64 and the one of
    /// <c>lstat$INODE64</c> on x64); a margin above it so that a larger future layout cannot overrun the buffer.
    /// </summary>
    private const int StatBufferBytes = 256;

    // Offsets in the 64-bit-inode struct stat: dev_t st_dev (4), mode_t st_mode (2), nlink_t st_nlink (2), ino_t st_ino (8),
    // uid_t st_uid (4)...
    private const int StatModeOffset = 4;
    private const int StatOwnerOffset = 16;

    /// <summary>The effective user id of this process (never fails).</summary>
    public static uint EffectiveUserId() => GetEUid();

    /// <summary>The effective user id of the process on the other end of a connected Unix domain socket.</summary>
    /// <returns>0, or the <c>errno</c> of the failure.</returns>
    public static int TryGetPeerUserId(SafeHandle socket, out uint userId)
    {
        ArgumentNullException.ThrowIfNull(socket);
        bool added = false;
        try
        {
            socket.DangerousAddRef(ref added);
            // A socket handle on Unix is the file descriptor.
            int descriptor = (int)socket.DangerousGetHandle();
            if (GetPeerEid(descriptor, out userId, out _) == 0)
            {
                return 0;
            }

            userId = 0;
            return Marshal.GetLastPInvokeError();
        }
        finally
        {
            if (added)
            {
                socket.DangerousRelease();
            }
        }
    }

    /// <summary><c>lstat</c> of <paramref name="path"/> (a symbolic link itself, not its target).</summary>
    /// <param name="mode">The whole <c>st_mode</c>: the file type (<c>S_IFMT</c>) and the permission bits.</param>
    /// <returns>0, or the <c>errno</c> of the failure (<see cref="NoSuchFile"/>: no such path).</returns>
    public static int LStat(string path, out uint mode, out uint ownerId)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        byte* buffer = stackalloc byte[StatBufferBytes];
        int result = RuntimeInformation.ProcessArchitecture == Architecture.X64
            ? LStatInode64(path, buffer)
            : LStatPlain(path, buffer);
        if (result != 0)
        {
            mode = 0;
            ownerId = 0;
            return Marshal.GetLastPInvokeError();
        }

        mode = *(ushort*)(buffer + StatModeOffset);
        ownerId = *(uint*)(buffer + StatOwnerOffset);
        return 0;
    }

    /// <summary><c>EROFS</c>: the file system is read-only (a mounted disk image, a translocated app).</summary>
    public const int ReadOnlyFileSystem = 30;

    /// <summary><c>access(path, W_OK)</c> (T-019): 0 when this process may write <paramref name="path"/>, otherwise the <c>errno</c>.</summary>
    public static int TryAccessWritable(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Access(path, WriteOk) == 0 ? 0 : Marshal.GetLastPInvokeError();
    }

    private const int WriteOk = 2;

    [LibraryImport(LibSystem, EntryPoint = "access", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Access(string path, int mode);

    [LibraryImport(LibSystem, EntryPoint = "geteuid")]
    private static partial uint GetEUid();

    [LibraryImport(LibSystem, EntryPoint = "getpeereid", SetLastError = true)]
    private static partial int GetPeerEid(int socket, out uint userId, out uint groupId);

    // arm64 has only the 64-bit-inode stat, exported as plain lstat.
    [LibraryImport(LibSystem, EntryPoint = "lstat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStatPlain(string path, byte* buffer);

    // x64 exports the 64-bit-inode stat under this name (plain lstat there is the legacy 32-bit-inode layout).
    [LibraryImport(LibSystem, EntryPoint = "lstat$INODE64", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int LStatInode64(string path, byte* buffer);
}
