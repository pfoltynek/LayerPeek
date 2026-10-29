using System.Net.Sockets;
using System.Runtime.Versioning;

using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.SingleInstance;

/// <summary>What a path is, as <c>lstat</c> sees it (a symbolic link is reported as one, never followed).</summary>
internal enum UnixEntryKind
{
    Directory,
    SymbolicLink,
    Other,
}

/// <summary>An existing path: its kind, permission bits and owner.</summary>
internal readonly record struct UnixEntry(UnixEntryKind Kind, UnixFileMode Mode, uint OwnerId);

/// <summary>
/// Who this process is and who is on the other end of its socket or owns its folder (T-053, finding L2). The seam of the
/// Unix socket transport's checks, so that tests can play another user.
/// </summary>
internal interface IUnixCredentials
{
    /// <summary>The effective user id of this process: the only user whose connections and folders are trusted.</summary>
    uint EffectiveUserId { get; }

    /// <summary>The effective user id of the process connected through <paramref name="socket"/>.</summary>
    /// <returns><see langword="false"/> when the OS cannot tell (the connection is then not trusted).</returns>
    bool TryGetPeerUserId(Socket socket, out uint userId);

    /// <summary>The path itself (a symbolic link is not followed).</summary>
    /// <returns><see langword="null"/> when nothing exists at <paramref name="path"/>.</returns>
    /// <exception cref="IOException">The path cannot be examined.</exception>
    UnixEntry? GetEntry(string path);
}

/// <summary>
/// The real credentials on macOS: <c>geteuid</c>, <c>getpeereid</c> and <c>lstat</c> from libc. <c>getpeereid</c> was
/// chosen over reading <c>LOCAL_PEERCRED</c> with <see cref="Socket.GetRawSocketOption"/>: it is the documented API with
/// a fixed signature (macOS builds it on <c>LOCAL_PEERCRED</c> itself), so no <c>struct xucred</c> layout is hard-coded here.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacUnixCredentials : IUnixCredentials
{
    // S_IFMT and its values from <sys/stat.h>.
    private const uint TypeMask = 0xF000;
    private const uint TypeDirectory = 0x4000;
    private const uint TypeSymbolicLink = 0xA000;
    private const uint PermissionMask = 0x0FFF;

    private MacUnixCredentials()
    {
        EffectiveUserId = MacUnixNative.EffectiveUserId();
    }

    public static MacUnixCredentials Current { get; } = new();

    public uint EffectiveUserId { get; }

    public bool TryGetPeerUserId(Socket socket, out uint userId)
    {
        ArgumentNullException.ThrowIfNull(socket);
        return MacUnixNative.TryGetPeerUserId(socket.SafeHandle, out userId) == 0;
    }

    public UnixEntry? GetEntry(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        int error = MacUnixNative.LStat(path, out uint mode, out uint ownerId);
        if (error == MacUnixNative.NoSuchFile)
        {
            return null;
        }

        if (error != 0)
        {
            throw new IOException($"The path '{path}' cannot be examined (errno {error}).", error);
        }

        UnixEntryKind kind = (mode & TypeMask) switch
        {
            TypeDirectory => UnixEntryKind.Directory,
            TypeSymbolicLink => UnixEntryKind.SymbolicLink,
            _ => UnixEntryKind.Other,
        };
        return new UnixEntry(kind, (UnixFileMode)(mode & PermissionMask), ownerId);
    }
}
