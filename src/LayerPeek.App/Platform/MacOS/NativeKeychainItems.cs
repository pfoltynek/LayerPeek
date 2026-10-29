using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

using LayerPeek.Core.Secrets;

using static LayerPeek.App.Platform.MacOS.KeychainNative;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// The Keychain calls of <see cref="KeychainSecretStore"/> (generic passwords by service and account), synchronous: they
/// may wait for a Keychain dialog, so the store calls them on the thread pool. Arguments are already validated.
/// </summary>
internal interface IKeychainItems
{
    /// <returns>The password, or <see langword="null"/> when there is no such item.</returns>
    /// <exception cref="SecretStoreException">The Keychain failed, or the item is not valid UTF-8.</exception>
    string? Read(string service, string account);

    /// <summary>Creates the item or replaces its password.</summary>
    /// <param name="byteCount">The strict UTF-8 size of <paramref name="secret"/> (at most <see cref="KeychainSecretStore.MaxSecretBytes"/>).</param>
    /// <exception cref="SecretStoreException">The Keychain failed.</exception>
    void Write(string service, string account, string secret, int byteCount);

    /// <returns><see langword="true"/> when an item was deleted, <see langword="false"/> when none existed.</returns>
    /// <exception cref="SecretStoreException">The Keychain failed.</exception>
    bool Delete(string service, string account);
}

/// <summary>
/// <see cref="IKeychainItems"/> over the SecItem API in the default file-based keychain (the login keychain) of the
/// current user. Local only (no <c>kSecAttrSynchronizable</c>); the file-based keychain has no accessibility classes
/// (<c>kSecAttrAccessible</c> belongs to the data protection keychain, which ad-hoc signed apps cannot use), so an item is
/// readable while the login keychain is unlocked (decided 2026-10-29).
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class NativeKeychainItems : IKeychainItems
{
    // Strict: a foreign item that is not UTF-8 must be reported, not silently altered to U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private NativeKeychainItems()
    {
    }

    public static NativeKeychainItems Instance { get; } = new();

    public string? Read(string service, string account)
    {
        Constants c = Const;
        nint serviceRef = 0;
        nint accountRef = 0;
        nint query = 0;
        nint result = 0;
        try
        {
            serviceRef = CreateString(service);
            accountRef = CreateString(account);
            query = CreateDictionary(
                [c.Class, c.AttrService, c.AttrAccount, c.ReturnData, c.MatchLimit],
                [c.ClassGenericPassword, serviceRef, accountRef, c.True, c.MatchLimitOne]);
            int status = SecItemCopyMatching(query, out result);
            if (status == ItemNotFound)
            {
                return null;
            }

            if (status != Success)
            {
                throw Failure("read", status);
            }

            if (!TryGetBytes(result, out Span<byte> blob))
            {
                throw new SecretStoreException("The macOS Keychain returned an item without password data.");
            }

            try
            {
                return StrictUtf8.GetString(blob);
            }
            catch (DecoderFallbackException)
            {
                // Written by something else than this store; do not guess what the secret was. Not chained: its message
                // quotes the offending bytes of the secret, and logged exceptions go to the log file whole (T-024).
                throw new SecretStoreException("The stored Keychain item is not valid UTF-8.");
            }
            finally
            {
                // CFRelease does not wipe the buffer; clear the plaintext copy owned by this process.
                CryptographicOperations.ZeroMemory(blob);
            }
        }
        finally
        {
            Release(result);
            Release(query);
            Release(accountRef);
            Release(serviceRef);
        }
    }

    public void Write(string service, string account, string secret, int byteCount)
    {
        Constants c = Const;
        nint data;
        // MaxSecretBytes (2.5 kB) is small enough for the stack and avoids a heap copy of the plaintext.
        Span<byte> blob = stackalloc byte[byteCount];
        try
        {
            StrictUtf8.GetBytes(secret, blob);
            data = CreateWipeableData(blob);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }

        nint serviceRef = 0;
        nint accountRef = 0;
        nint add = 0;
        nint query = 0;
        nint update = 0;
        try
        {
            serviceRef = CreateString(service);
            accountRef = CreateString(account);
            add = CreateDictionary(
                [c.Class, c.AttrService, c.AttrAccount, c.ValueData],
                [c.ClassGenericPassword, serviceRef, accountRef, data]);
            int status = SecItemAdd(add, null);
            if (status == DuplicateItem)
            {
                // Replaces only the password of the existing item; its other attributes stay as they are.
                query = CreateDictionary([c.Class, c.AttrService, c.AttrAccount], [c.ClassGenericPassword, serviceRef, accountRef]);
                update = CreateDictionary([c.ValueData], [data]);
                status = SecItemUpdate(query, update);
            }

            if (status != Success)
            {
                throw Failure("write", status);
            }
        }
        finally
        {
            Release(update);
            Release(query);
            Release(add);
            Release(accountRef);
            Release(serviceRef);
            // Last: the dictionaries above retained it; once they are gone this is its only reference.
            ZeroAndRelease(data);
        }
    }

    public bool Delete(string service, string account)
    {
        Constants c = Const;
        nint serviceRef = 0;
        nint accountRef = 0;
        nint query = 0;
        try
        {
            serviceRef = CreateString(service);
            accountRef = CreateString(account);
            query = CreateDictionary([c.Class, c.AttrService, c.AttrAccount], [c.ClassGenericPassword, serviceRef, accountRef]);
            int status = SecItemDelete(query);
            return status switch
            {
                Success => true,
                ItemNotFound => false,
                _ => throw Failure("delete", status),
            };
        }
        finally
        {
            Release(query);
            Release(accountRef);
            Release(serviceRef);
        }
    }

    private static SecretStoreException Failure(string operation, int status) =>
        new($"The macOS Keychain failed to {operation} an item (OSStatus {status}).", new KeychainException(status, ErrorMessage(status)));
}
