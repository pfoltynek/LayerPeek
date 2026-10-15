using System.Runtime.Versioning;

using PrusaTrayDiagnostic.App.Platform.Windows;
using PrusaTrayDiagnostic.Core.Secrets;

namespace PrusaTrayDiagnostic.App.Tests.Platform.Windows;

/// <summary>
/// Integration tests against the real Windows Credential Manager of the current user.
/// Every test uses a unique target prefix and removes everything under it; <see cref="CredentialCleanupFixture"/>
/// additionally sweeps leftovers of crashed runs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialSecretStoreTests : IAsyncDisposable
{
    private readonly string _prefix = TestCredentials.NewPrefix();
    private readonly WindowsCredentialSecretStore _store;

    public WindowsCredentialSecretStoreTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows Credential Manager is available only on Windows.");
        _store = new WindowsCredentialSecretStore(_prefix, CredentialManagerNative.CredPersistSession);
    }

    [Fact]
    public void TestCleanupRefusesProductionPrefix()
    {
        Assert.Throws<ArgumentException>(() => TestCredentials.DeleteAll(WindowsCredentialSecretStore.DefaultTargetPrefix));
        Assert.Throws<ArgumentException>(() => TestCredentials.FindTargets("PrusaTrayDiagnostic.TestsX"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync()
    {
        // Skipped instances (non-Windows) never created anything.
        if (OperatingSystem.IsWindows())
        {
            TestCredentials.DeleteAll(_prefix);
        }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GetMissingKeyReturnsNull()
    {
        Assert.Null(await _store.GetAsync("missing", Ct));
    }

    [Theory]
    [InlineData("hunter2")]
    [InlineData("")]
    [InlineData("Příliš žluťoučký kůň 🐎 \0 with NUL")]
    public async Task SetThenGetRoundTripsSecret(string secret)
    {
        string key = "roundtrip";

        await _store.SetAsync(key, secret, Ct);

        Assert.Equal(secret, await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task SetOverwritesExistingSecret()
    {
        string key = "overwrite";

        await _store.SetAsync(key, "old", Ct);
        await _store.SetAsync(key, "new", Ct);

        Assert.Equal("new", await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task DeleteReportsWhetherSecretExisted()
    {
        string key = "delete";
        await _store.SetAsync(key, "value", Ct);

        Assert.True(await _store.DeleteAsync(key, Ct));
        Assert.False(await _store.DeleteAsync(key, Ct));
        Assert.Null(await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task KeysAreIsolatedByPrefix()
    {
        WindowsCredentialSecretStore other = new(TestCredentials.NewPrefix(), CredentialManagerNative.CredPersistSession);
        string key = "isolated";

        await _store.SetAsync(key, "mine", Ct);

        Assert.Null(await other.GetAsync(key, Ct));
    }

    [Fact]
    public async Task SetMaximumSizeSecretSucceeds()
    {
        string key = "max";
        string secret = new('x', WindowsCredentialSecretStore.MaxSecretBytes);

        await _store.SetAsync(key, secret, Ct);

        Assert.Equal(secret, await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task SetTooLargeSecretIsRejected()
    {
        string secret = new('x', WindowsCredentialSecretStore.MaxSecretBytes + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetAsync("too-large", secret, Ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task InvalidKeyIsRejected(string? key)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.GetAsync(key!, Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.SetAsync(key!, "x", Ct));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.DeleteAsync(key!, Ct));
    }

    [Fact]
    public async Task InvalidUnicodeSecretIsRejectedWithoutStoringAnything()
    {
        // Built in code: xUnit would replace lone surrogates in [InlineData] while serializing test cases.
        string[] secrets = [new string((char)0xD800, 1), "abc" + (char)0xDC00 + "def"];
        string key = "invalid-unicode";

        foreach (string secret in secrets)
        {
            ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() => _store.SetAsync(key, secret, Ct));

            Assert.DoesNotContain(secret, ex.Message, StringComparison.Ordinal);
            Assert.Null(await _store.GetAsync(key, Ct));
        }
    }

    [Fact]
    public void CancelledTokenIsReportedThroughTask()
    {
        CancellationToken cancelled = new(canceled: true);

        Assert.True(_store.GetAsync("c", cancelled).IsCanceled);
        Assert.True(_store.SetAsync("c", "x", cancelled).IsCanceled);
        Assert.True(_store.DeleteAsync("c", cancelled).IsCanceled);
    }

    [Fact]
    public async Task NullSecretIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.SetAsync("null", null!, Ct));
    }

    [Fact]
    public async Task PrinterSecretKeyCanBeStored()
    {
        string key = SecretKeys.ForPrinter("mk4");

        await _store.SetAsync(key, "maker-password", Ct);

        Assert.Equal("maker-password", await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task TestCleanupRemovesEverythingUnderPrefix()
    {
        await _store.SetAsync("a", "1", Ct);
        await _store.SetAsync("nested/b", "2", Ct);
        Assert.Equal(2, TestCredentials.FindTargets(_prefix).Count);

        Assert.Equal(2, TestCredentials.DeleteAll(_prefix));

        Assert.Empty(TestCredentials.FindTargets(_prefix));
    }
}
