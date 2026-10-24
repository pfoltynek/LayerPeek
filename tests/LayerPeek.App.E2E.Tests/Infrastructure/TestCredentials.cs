using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

using LayerPeek.Tests.Shared;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>
/// The test entries of the real Windows Credential Manager (T-067, T-073): generic credentials whose target starts with
/// <see cref="AppSwitches.TestCredentialPrefixRoot"/>. Everything here is limited to that root: the enumeration asks
/// Windows only for targets under it (<c>CredEnumerate</c> filter) and checks every result again, so the user's own
/// entries are never read, changed or deleted. Secrets are only compared, never returned or logged.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class TestCredentials
{
    /// <summary>CRED_TYPE_GENERIC.</summary>
    private const uint CredTypeGeneric = 1;

    /// <summary>CRED_PERSIST_SESSION: what the test mode writes (T-067, never on disk, gone at logoff).</summary>
    public const uint PersistSession = 1;

    private const int ErrorNotFound = 1168;

    /// <summary>A prefix of its own for one test: <c>LayerPeek.E2E/&lt;worktree&gt;/&lt;random&gt;</c>.</summary>
    public static string NewPrefix() =>
        $"{AppSwitches.TestCredentialPrefixRoot}{TestWorktree.Id}/{Guid.NewGuid().ToString("N")[..12]}";

    /// <summary>
    /// Whether <paramref name="prefix"/> lies under the test root with a name of its own after it (the same rule the app
    /// applies to <see cref="AppSwitches.TestCredentialPrefixVariable"/>): only such a prefix is ever enumerated or deleted.
    /// </summary>
    public static bool IsTestPrefix(string prefix) =>
        prefix.StartsWith(AppSwitches.TestCredentialPrefixRoot, StringComparison.Ordinal)
        && !prefix.Any(char.IsWhiteSpace)
        && !prefix.Contains('*', StringComparison.Ordinal);

    /// <summary>The targets under <paramref name="prefix"/> (default: the whole test root), sorted.</summary>
    public static List<string> Targets(string prefix = AppSwitches.TestCredentialPrefixRoot)
    {
        EnsureTestPrefix(prefix);
        List<string> targets = [];
        if (!CredEnumerate(prefix + "*", 0, out uint count, out nint* credentials))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? targets : throw new Win32Exception(error, "CredEnumerate failed.");
        }

        try
        {
            for (uint i = 0; i < count; i++)
            {
                NativeCredential* credential = (NativeCredential*)credentials[i];
                string target = new(credential->TargetName);
                // The filter already does this; checked again so that nothing outside the prefix is ever touched.
                if (credential->Type == CredTypeGeneric && target.StartsWith(prefix, StringComparison.Ordinal))
                {
                    targets.Add(target);
                }
            }
        }
        finally
        {
            CredFree(credentials);
        }

        targets.Sort(StringComparer.Ordinal);
        return targets;
    }

    /// <summary>The CRED_PERSIST value of the test entry <paramref name="target"/>, or <see langword="null"/> when it does not exist.</summary>
    public static uint? Persist(string target) => ReadEntry(target, secret: null).Persist;

    /// <summary>Whether the test entry <paramref name="target"/> holds <paramref name="secret"/> (UTF-8, as the app writes it).</summary>
    public static bool Holds(string target, string secret) => ReadEntry(target, secret).Matches;

    /// <summary>Deletes every test entry under <paramref name="prefix"/> (default: the whole test root); returns their targets.</summary>
    public static List<string> DeleteAll(string prefix = AppSwitches.TestCredentialPrefixRoot)
    {
        List<string> deleted = [];
        List<string> failed = [];
        foreach (string target in Targets(prefix))
        {
            int error = CredDelete(target, CredTypeGeneric, 0) ? 0 : Marshal.GetLastPInvokeError();
            if (error is 0 or ErrorNotFound)
            {
                deleted.Add(target);
            }
            else
            {
                failed.Add($"{target} ({new Win32Exception(error).Message})");
            }
        }

        return failed.Count == 0
            ? deleted
            : throw new InvalidOperationException($"Test entries of Credential Manager could not be deleted: {string.Join(", ", failed)}.");
    }

    private static (uint? Persist, bool Matches) ReadEntry(string target, string? secret)
    {
        EnsureTestPrefix(target);
        if (!CredRead(target, CredTypeGeneric, 0, out NativeCredential* credential))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? (null, false) : throw new Win32Exception(error, "CredRead failed.");
        }

        try
        {
            ReadOnlySpan<byte> blob = new(credential->CredentialBlob, checked((int)credential->CredentialBlobSize));
            bool matches = secret is not null && CryptographicOperations.FixedTimeEquals(blob, Encoding.UTF8.GetBytes(secret));
            return (credential->Persist, matches);
        }
        finally
        {
            CredFree(credential);
        }
    }

    private static void EnsureTestPrefix(string prefix)
    {
        if (!IsTestPrefix(prefix))
        {
            throw new ArgumentException($"Only targets under '{AppSwitches.TestCredentialPrefixRoot}' are touched by the tests.", nameof(prefix));
        }
    }

    /// <summary>CREDENTIALW (wincred.h).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
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

    [LibraryImport("advapi32.dll", EntryPoint = "CredEnumerateW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerate(string filter, uint flags, out uint count, out nint* credentials);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out NativeCredential* credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(void* buffer);
}
