using System.Runtime.Versioning;

namespace LayerPeek.App.E2E.Tests.Infrastructure;

/// <summary>The targets of the test entries under one prefix (names only, never a secret).</summary>
internal sealed record CredentialsState(IReadOnlyList<string> Targets);

/// <summary>
/// Session-only test entries a test app writes to the real Credential Manager under the prefix of one test (T-067,
/// T-073), the key of the change. The test app writes them itself; the restore deletes every entry under the prefix
/// (also entries a crashed run left), so the restored state is always "none". The entries are CRED_PERSIST_SESSION:
/// even a restore that never runs loses them at the next logoff.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CredentialsKind() : SystemChangeKind<CredentialsState>("Credentials", SystemChangeTraits.Credentials, SystemChangeJson.Default.CredentialsState)
{
    public override Task<CredentialsState> ReadStateAsync(string? key, CancellationToken cancellationToken) =>
        Task.FromResult(new CredentialsState(TestCredentials.Targets(Prefix(key))));

    /// <summary>Restored means no test entry under the prefix is left.</summary>
    public override bool Matches(CredentialsState original, CredentialsState current) => current.Targets.Count == 0;

    public override string Describe(string? key, CredentialsState original) =>
        $"Session-only test entries '{key}/...' in Credential Manager (before: {(original.Targets.Count == 0 ? "none" : $"{original.Targets.Count} left by a crashed run")}). "
        + "By hand: Control Panel > Credential Manager > Windows Credentials, remove the generic credentials "
        + $"'{AppSwitches.TestCredentialPrefixRoot}...' (they also go at the next sign-out).";

    /// <summary>Nothing to apply: the test app writes its entries itself.</summary>
    protected override Task ApplyCoreAsync(string? key, CredentialsState state, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The test app writes its credentials itself; this change is only restored.");

    protected override Task RestoreCoreAsync(string? key, CredentialsState original, CancellationToken cancellationToken)
    {
        TestCredentials.DeleteAll(Prefix(key));
        return Task.CompletedTask;
    }

    /// <summary>The key is the prefix of the test, always under the test root (checked again by <see cref="TestCredentials"/>).</summary>
    private static string Prefix(string? key) =>
        key is not null && TestCredentials.IsTestPrefix(key) && key.Length > AppSwitches.TestCredentialPrefixRoot.Length
            ? key + "/"
            : throw new InvalidOperationException($"'{key}' is not a credential prefix of the tests.");
}
