using System.Runtime.Versioning;
using System.Text;

using LayerPeek.Core.Secrets;

namespace LayerPeek.App.Platform.MacOS;

/// <summary>
/// <see cref="ISecretStore"/> backed by the macOS Keychain of the current user (SecItem API, generic passwords in the
/// default file-based keychain, i.e. the login keychain). Each key maps to the item with service = the prefix and
/// account = the normalized key (<see cref="ToAccount"/>). Local only: no <c>kSecAttrSynchronizable</c> (decided
/// 2026-10-29). Argument errors are thrown immediately; cancellation and Keychain failures surface through the returned
/// task.
/// <para>
/// Every Keychain call runs on the thread pool, never on the caller's thread (decided 2026-10-29): macOS may show a
/// dialog in it (unlock the login keychain, allow access after an update of the ad-hoc signed app) and the call waits
/// until the user answers. Called from the UI thread, the windows and the tray menu keep working meanwhile. Dialogs are
/// allowed (never switched off here); only the tests switch them off for their own process.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class KeychainSecretStore : ISecretStore
{
    public const string DefaultServicePrefix = "LayerPeek";

    /// <summary>Maximum secret size in UTF-8 bytes: the limit of the Windows Credential Manager (decided 2026-10-29).</summary>
    public const int MaxSecretBytes = 5 * 512;

    // Strict: an invalid UTF-16 secret (lone surrogate) must be rejected, not silently altered to U+FFFD.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _service;
    private readonly IKeychainItems _items;

    public KeychainSecretStore(string servicePrefix = DefaultServicePrefix)
        : this(servicePrefix, NativeKeychainItems.Instance)
    {
    }

    /// <param name="items">The Keychain calls; tests pass a fake that blocks like a Keychain dialog.</param>
    internal KeychainSecretStore(string servicePrefix, IKeychainItems items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(servicePrefix);
        ArgumentNullException.ThrowIfNull(items);
        _service = servicePrefix;
        _items = items;
    }

    /// <summary>The service attribute of every item of this store (composition tests).</summary>
    internal string Service => _service;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        string account = ToAccount(key);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<string?>(cancellationToken);
        }

        return Task.Run(() => _items.Read(_service, account), cancellationToken);
    }

    public Task SetAsync(string key, string secret, CancellationToken cancellationToken)
    {
        string account = ToAccount(key);
        ArgumentNullException.ThrowIfNull(secret);
        int byteCount = GetSecretByteCount(secret);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        return Task.Run(() => _items.Write(_service, account, secret, byteCount), cancellationToken);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
    {
        string account = ToAccount(key);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<bool>(cancellationToken);
        }

        return Task.Run(() => _items.Delete(_service, account), cancellationToken);
    }

    /// <summary>
    /// The account attribute of <paramref name="key"/>. Keychain attributes are case-sensitive but the keys of
    /// <see cref="ISecretStore"/> are not, so every key is stored in invariant upper case: the same equivalence as
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> (used by the other stores), applied in this one place.
    /// </summary>
    internal static string ToAccount(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return key.ToUpperInvariant();
    }

    private static int GetSecretByteCount(string secret)
    {
        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(secret);
        }
        catch (EncoderFallbackException)
        {
            // The exception message would contain the offending characters, so it is not chained.
            throw new ArgumentException("The secret is not a valid Unicode string.", nameof(secret));
        }

        if (byteCount > MaxSecretBytes)
        {
            throw new ArgumentException($"The secret exceeds {MaxSecretBytes} bytes in UTF-8.", nameof(secret));
        }

        return byteCount;
    }
}
