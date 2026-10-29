using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LayerPeek.App.Platform.MacOS;

using static LayerPeek.App.Platform.MacOS.KeychainNative;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// All Keychain items created by tests have a service under <see cref="ServiceRoot"/>, so they can be found and removed
/// even when a previous test run crashed before its own cleanup ran. Only the attributes are ever read (never a
/// password), and nothing outside <see cref="ServiceRoot"/> can be deleted.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class TestKeychain
{
    public const string ServiceRoot = "LayerPeek.Tests";

    /// <summary>Unique service prefix for one test class instance.</summary>
    public static string NewPrefix() => $"{ServiceRoot}/{Guid.NewGuid():N}";

    /// <summary>
    /// Why the Keychain tests cannot run, or <see langword="null"/>: set once by <see cref="ProbeAsync"/> (assembly fixture).
    /// </summary>
    public static string? SkipReason { get; private set; }

    /// <summary>
    /// Switches the Keychain dialogs of this process off: an "allow access" or "unlock keychain" prompt would block a test
    /// run until someone answers it; such a call fails with <see cref="InteractionNotAllowed"/> instead.
    /// </summary>
    public static void DisallowInteraction() => SetInteractionAllowed(false);

    /// <summary>Switches the Keychain dialogs of this process back on (end of the run).</summary>
    public static void AllowInteraction() => SetInteractionAllowed(true);

    /// <summary>
    /// Finds out whether the default keychain (where the store adds its items) is unlocked, and if so, that it accepts a
    /// new item (<see cref="SkipReason"/>). A locked keychain could only be unlocked with a dialog, which the tests never
    /// show; any other failure is an error of the run. Leaves nothing behind.
    /// </summary>
    public static async Task ProbeAsync()
    {
        uint keychainStatus = DefaultKeychainStatus();
        if ((keychainStatus & UnlockStateStatus) == 0)
        {
            SkipReason = $"The default (login) Keychain is locked (SecKeychainGetStatus {keychainStatus}) and the tests never show the "
                + "dialog that would unlock it; the Keychain tests that write are skipped. Unlock it (Keychain Access) and run them again.";
            return;
        }

        KeychainSecretStore probe = new(NewPrefix());
        await probe.SetAsync("probe", "probe", CancellationToken.None);
        if (!await probe.DeleteAsync("probe", CancellationToken.None))
        {
            throw new InvalidOperationException("The Keychain probe item vanished before it was deleted.");
        }

        SkipReason = null;
    }

    private static unsafe uint DefaultKeychainStatus()
    {
        nint keychain = 0;
        try
        {
            int status = SecKeychainCopyDefault(&keychain);
            if (status == Success)
            {
                status = SecKeychainGetStatus(keychain, out uint keychainStatus);
                if (status == Success)
                {
                    return keychainStatus;
                }
            }

            throw new KeychainException(status, ErrorMessage(status));
        }
        finally
        {
            Release(keychain);
        }
    }

    /// <summary>Deletes every generic password whose service is <paramref name="prefix"/> or lies under it.</summary>
    /// <returns>Number of deleted items.</returns>
    public static int DeleteAll(string prefix = ServiceRoot)
    {
        RequireTestPrefix(prefix);
        List<(string Service, string Account)> items = FindItems(prefix);
        Constants c = Const;
        foreach (string service in items.Select(static item => item.Service).Distinct(StringComparer.Ordinal))
        {
            nint serviceRef = CreateString(service);
            nint query = 0;
            try
            {
                // Without a match limit SecItemDelete removes every item of the service (all accounts).
                query = CreateDictionary([c.Class, c.AttrService], [c.ClassGenericPassword, serviceRef]);
                int status = SecItemDelete(query);
                if (status is not Success and not ItemNotFound)
                {
                    throw new KeychainException(status, ErrorMessage(status));
                }
            }
            finally
            {
                Release(query);
                Release(serviceRef);
            }
        }

        return items.Count;
    }

    /// <summary>Service and account of every generic password whose service is <paramref name="prefix"/> or lies under it.</summary>
    public static List<(string Service, string Account)> FindItems(string prefix = ServiceRoot)
    {
        RequireTestPrefix(prefix);
        Constants c = Const;
        nint query = CreateDictionary(
            [c.Class, c.ReturnAttributes, c.MatchLimit],
            [c.ClassGenericPassword, c.True, c.MatchLimitAll]);
        nint result = 0;
        try
        {
            int status = SecItemCopyMatching(query, out result);
            if (status == ItemNotFound)
            {
                return [];
            }

            if (status != Success)
            {
                throw new KeychainException(status, ErrorMessage(status));
            }

            List<(string, string)> found = [];
            int count = ArrayCount(result);
            for (int i = 0; i < count; i++)
            {
                nint attributes = ArrayItem(result, i);
                string? service = ToManagedString(DictionaryValue(attributes, c.AttrService));
                if (service is not null && IsUnder(service, prefix))
                {
                    found.Add((service, ToManagedString(DictionaryValue(attributes, c.AttrAccount)) ?? string.Empty));
                }
            }

            return found;
        }
        finally
        {
            Release(result);
            Release(query);
        }
    }

    // Keychain attributes are case-sensitive, so is the match.
    private static bool IsUnder(string service, string prefix) =>
        service.Equals(prefix, StringComparison.Ordinal) || service.StartsWith(prefix + "/", StringComparison.Ordinal);

    // Guard: these helpers must never be able to touch the user's real items (service "LayerPeek").
    private static void RequireTestPrefix(string prefix)
    {
        if (!IsUnder(prefix, ServiceRoot))
        {
            throw new ArgumentException($"Only services under '{ServiceRoot}' may be enumerated or deleted by tests.", nameof(prefix));
        }
    }

    private static void SetInteractionAllowed(bool allowed)
    {
        int status = SecKeychainSetUserInteractionAllowed(allowed ? (byte)1 : (byte)0);
        if (status != Success)
        {
            throw new KeychainException(status, ErrorMessage(status));
        }
    }

    /// <summary><c>kSecUnlockStateStatus</c>.</summary>
    private const uint UnlockStateStatus = 1;

    // The SecKeychain API is deprecated (file-based keychain only), but the store targets the file-based keychain and this
    // is the only way to see its lock state and to switch its dialogs off. Test code only; applies to this process only.
    [LibraryImport("/System/Library/Frameworks/Security.framework/Security")]
    private static unsafe partial int SecKeychainCopyDefault(nint* keychain);

    [LibraryImport("/System/Library/Frameworks/Security.framework/Security")]
    private static partial int SecKeychainGetStatus(nint keychain, out uint keychainStatus);

    [LibraryImport("/System/Library/Frameworks/Security.framework/Security")]
    private static partial int SecKeychainSetUserInteractionAllowed(byte state);
}
