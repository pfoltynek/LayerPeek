using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using LayerPeek.App.Platform.Windows;

namespace LayerPeek.App.Tests.Platform.Windows;

/// <summary>
/// All credentials created by tests live under <see cref="TargetRoot"/>, so they can be found and removed
/// even when a previous test run crashed before its own cleanup ran.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class TestCredentials
{
    public const string TargetRoot = "LayerPeek.Tests";

    /// <summary>Unique target prefix for one test class instance.</summary>
    public static string NewPrefix() => $"{TargetRoot}/{Guid.NewGuid():N}";

    /// <summary>Deletes every generic credential whose target starts with <paramref name="prefix"/>.</summary>
    /// <returns>Number of deleted credentials.</returns>
    public static int DeleteAll(string prefix = TargetRoot)
    {
        RequireTestPrefix(prefix);
        int deleted = 0;
        foreach (string target in FindTargets(prefix))
        {
            if (CredentialManagerNative.CredDelete(target, CredentialManagerNative.CredTypeGeneric, 0))
            {
                deleted++;
            }
            else if (Marshal.GetLastPInvokeError() is int error and not CredentialManagerNative.ErrorNotFound)
            {
                throw new Win32Exception(error);
            }
        }

        return deleted;
    }

    /// <summary>Targets of all generic credentials whose target starts with <paramref name="prefix"/>.</summary>
    public static List<string> FindTargets(string prefix = TargetRoot)
    {
        RequireTestPrefix(prefix);
        if (!CredEnumerate($"{prefix}/*", 0, out uint count, out CredentialManagerNative.Credential** credentials))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == CredentialManagerNative.ErrorNotFound ? [] : throw new Win32Exception(error);
        }

        try
        {
            List<string> targets = new((int)count);
            for (uint i = 0; i < count; i++)
            {
                CredentialManagerNative.Credential* credential = credentials[i];
                if (credential->Type == CredentialManagerNative.CredTypeGeneric)
                {
                    targets.Add(new string(credential->TargetName));
                }
            }

            return targets;
        }
        finally
        {
            CredentialManagerNative.CredFree(credentials);
        }
    }

    // Guard: these helpers must never be able to touch the user's real credentials (prefix "LayerPeek/").
    private static void RequireTestPrefix(string prefix)
    {
        if (!prefix.Equals(TargetRoot, StringComparison.OrdinalIgnoreCase) && !prefix.StartsWith(TargetRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Only prefixes under '{TargetRoot}' may be enumerated or deleted by tests.", nameof(prefix));
        }
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredEnumerateW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredEnumerate(string filter, uint flags, out uint count, out CredentialManagerNative.Credential** credentials);
}
