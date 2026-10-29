using System.Runtime.Versioning;

using LayerPeek.App.Platform.MacOS;
using LayerPeek.Core.Secrets;

using static LayerPeek.App.Platform.MacOS.KeychainNative;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// Integration tests against the real login Keychain of the current user, only under the test service root
/// <see cref="TestKeychain.ServiceRoot"/> (never the production <c>LayerPeek</c>). Every test uses a unique service prefix
/// and removes everything under it; the assembly fixture additionally sweeps leftovers of crashed runs and switches the
/// Keychain dialogs of the test process off.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class KeychainSecretStoreTests : IAsyncDisposable
{
    private readonly string _prefix = TestKeychain.NewPrefix();
    private readonly KeychainSecretStore _store;

    public KeychainSecretStoreTests()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The macOS Keychain is available only on macOS.");
        _store = new KeychainSecretStore(_prefix);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync()
    {
        // Skipped instances (other OS) never created anything.
        if (OperatingSystem.IsMacOS())
        {
            TestKeychain.DeleteAll(_prefix);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Skips a test that writes into the Keychain when the Keychain refuses writes without a dialog.</summary>
    private static void RequireWritableKeychain()
    {
        if (TestKeychain.SkipReason is { } reason)
        {
            Assert.Skip(reason);
        }
    }

    [Fact]
    public void TestCleanupRefusesProductionPrefix()
    {
        Assert.Throws<ArgumentException>(() => TestKeychain.DeleteAll(KeychainSecretStore.DefaultServicePrefix));
        Assert.Throws<ArgumentException>(() => TestKeychain.FindItems("LayerPeek.TestsX"));
        // Case-sensitive like the Keychain: "layerpeek.tests" is not the test root.
        Assert.Throws<ArgumentException>(() => TestKeychain.DeleteAll("layerpeek.tests"));
    }

    [Fact]
    public void DefaultServiceIsTheProductionPrefix() =>
        // Only constructed; the Keychain is not touched.
        Assert.Equal("LayerPeek", new KeychainSecretStore().Service);

    [Fact]
    public async Task GetMissingKeyReturnsNull()
    {
        // Reading works also on a keychain that refuses writes.
        Assert.Null(await _store.GetAsync("missing", Ct));
    }

    [Fact]
    public async Task DeleteMissingKeyReturnsFalse()
    {
        Assert.False(await _store.DeleteAsync("missing", Ct));
    }

    [Theory]
    [InlineData("hunter2")]
    [InlineData("")]
    [InlineData("Příliš žluťoučký kůň 🐎 \0 with NUL")]
    public async Task SetThenGetRoundTripsSecret(string secret)
    {
        RequireWritableKeychain();
        string key = "roundtrip";

        await _store.SetAsync(key, secret, Ct);

        Assert.Equal(secret, await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task SetOverwritesExistingSecret()
    {
        RequireWritableKeychain();
        string key = "overwrite";

        await _store.SetAsync(key, "old", Ct);
        await _store.SetAsync(key, "new", Ct);

        Assert.Equal("new", await _store.GetAsync(key, Ct));
        // Replaced, not added next to it.
        Assert.Single(TestKeychain.FindItems(_prefix));
    }

    [Fact]
    public async Task DeleteReportsWhetherSecretExisted()
    {
        RequireWritableKeychain();
        string key = "delete";
        await _store.SetAsync(key, "value", Ct);

        Assert.True(await _store.DeleteAsync(key, Ct));
        Assert.False(await _store.DeleteAsync(key, Ct));
        Assert.Null(await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task KeysDifferingOnlyInCaseAddressTheSameSecret()
    {
        RequireWritableKeychain();

        await _store.SetAsync("printer/MK4-Dílna", "first", Ct);
        Assert.Equal("first", await _store.GetAsync("PRINTER/mk4-dÍLNA", Ct));

        await _store.SetAsync("Printer/mk4-dílna", "second", Ct);
        Assert.Equal("second", await _store.GetAsync("printer/MK4-DÍLNA", Ct));
        Assert.Single(TestKeychain.FindItems(_prefix));

        Assert.True(await _store.DeleteAsync("PRINTER/MK4-DÍLNA", Ct));
        Assert.Null(await _store.GetAsync("printer/mk4-dílna", Ct));
    }

    [Fact]
    public void KeyNormalizationMatchesOrdinalIgnoreCase()
    {
        string[] keys = ["printer/mk4", "PRINTER/MK4", "printer/Dílna-ŽLUŤOUČKÝ", "printer/ß", "printer/ı", "printer/I"];

        foreach (string a in keys)
        {
            foreach (string b in keys)
            {
                Assert.Equal(
                    StringComparer.OrdinalIgnoreCase.Equals(a, b),
                    string.Equals(KeychainSecretStore.ToAccount(a), KeychainSecretStore.ToAccount(b), StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public async Task KeysAreIsolatedByPrefix()
    {
        RequireWritableKeychain();
        string otherPrefix = TestKeychain.NewPrefix();
        KeychainSecretStore other = new(otherPrefix);
        string key = "isolated";
        try
        {
            await _store.SetAsync(key, "mine", Ct);
            await other.SetAsync(key, "theirs", Ct);

            Assert.Equal("mine", await _store.GetAsync(key, Ct));
            Assert.Equal("theirs", await other.GetAsync(key, Ct));
            Assert.True(await other.DeleteAsync(key, Ct));
            Assert.Equal("mine", await _store.GetAsync(key, Ct));
        }
        finally
        {
            TestKeychain.DeleteAll(otherPrefix);
        }
    }

    [Fact]
    public async Task SetMaximumSizeSecretSucceeds()
    {
        RequireWritableKeychain();
        string key = "max";
        string secret = new('x', KeychainSecretStore.MaxSecretBytes);

        await _store.SetAsync(key, secret, Ct);

        Assert.Equal(secret, await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task SetTooLargeSecretIsRejected()
    {
        string secret = new('x', KeychainSecretStore.MaxSecretBytes + 1);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.SetAsync("too-large", secret, Ct));
        Assert.Equal(2560, KeychainSecretStore.MaxSecretBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void InvalidKeyIsRejectedImmediately(string? key)
    {
        // Synchronous throw (not a faulted task), like the Windows store.
        Assert.ThrowsAny<ArgumentException>(() => { _ = _store.GetAsync(key!, Ct); });
        Assert.ThrowsAny<ArgumentException>(() => { _ = _store.SetAsync(key!, "x", Ct); });
        Assert.ThrowsAny<ArgumentException>(() => { _ = _store.DeleteAsync(key!, Ct); });
    }

    [Fact]
    public void InvalidPrefixIsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => new KeychainSecretStore(null!));
        Assert.ThrowsAny<ArgumentException>(() => new KeychainSecretStore(" "));
    }

    [Fact]
    public async Task InvalidUnicodeSecretIsRejectedWithoutStoringAnything()
    {
        // Built in code: xUnit would replace lone surrogates in [InlineData] while serializing test cases.
        string[] secrets = [new string((char)0xD800, 1), "abc" + (char)0xDC00 + "def"];
        string key = "invalid-unicode";

        foreach (string secret in secrets)
        {
            // Thrown immediately, before the Keychain is touched.
            ArgumentException ex = Assert.Throws<ArgumentException>(() => { _ = _store.SetAsync(key, secret, Ct); });

            Assert.DoesNotContain(secret, ex.Message, StringComparison.Ordinal);
            Assert.Null(await _store.GetAsync(key, Ct));
        }

        Assert.Empty(TestKeychain.FindItems(_prefix));
    }

    [Fact]
    public async Task ForeignNonUtf8ItemFailsWithoutQuotingItsBytes()
    {
        RequireWritableKeychain();
        // Written by another program: 0xC3 0x28 is not UTF-8. The decoder's message would quote these bytes, and logged
        // exceptions go to the log file whole (T-024).
        WriteRawItem(_prefix, KeychainSecretStore.ToAccount("foreign"), [0x41, 0xC3, 0x28, 0x42]);

        SecretStoreException ex = await Assert.ThrowsAsync<SecretStoreException>(() => _store.GetAsync("foreign", Ct));

        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("C3", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KeychainFailureNamesTheStatusButNoSecret()
    {
        KeychainException inner = new(InteractionNotAllowed, ErrorMessage(InteractionNotAllowed));

        Assert.Equal(InteractionNotAllowed, inner.Status);
        Assert.Contains("-25308", inner.Message, StringComparison.Ordinal);
        Assert.NotNull(ErrorMessage(InteractionNotAllowed));
    }

    [Fact]
    public void CancelledTokenIsReportedThroughTask()
    {
        CancellationToken cancelled = new(canceled: true);

        Assert.True(_store.GetAsync("c", cancelled).IsCanceled);
        Assert.True(_store.SetAsync("c", "x", cancelled).IsCanceled);
        Assert.True(_store.DeleteAsync("c", cancelled).IsCanceled);
        Assert.Empty(TestKeychain.FindItems(_prefix));
    }

    [Fact]
    public void NullSecretIsRejectedImmediately()
    {
        Assert.Throws<ArgumentNullException>(() => { _ = _store.SetAsync("null", null!, Ct); });
    }

    [Fact]
    public async Task PrinterSecretKeyCanBeStored()
    {
        RequireWritableKeychain();
        string key = SecretKeys.ForPrinter("mk4");

        await _store.SetAsync(key, "maker-password", Ct);

        Assert.Equal("maker-password", await _store.GetAsync(key, Ct));
    }

    [Fact]
    public async Task TestCleanupRemovesEverythingUnderPrefix()
    {
        RequireWritableKeychain();
        await _store.SetAsync("a", "1", Ct);
        await _store.SetAsync("nested/b", "2", Ct);
        // A leftover of a "crashed" run under another test prefix is found by the root sweep too.
        string crashed = TestKeychain.NewPrefix();
        await new KeychainSecretStore(crashed).SetAsync("left-over", "3", Ct);
        Assert.Equal(2, TestKeychain.FindItems(_prefix).Count);
        Assert.Contains(TestKeychain.FindItems(), item => item.Service == crashed);

        Assert.Equal(2, TestKeychain.DeleteAll(_prefix));
        Assert.Empty(TestKeychain.FindItems(_prefix));
        Assert.Single(TestKeychain.FindItems(crashed));

        Assert.True(TestKeychain.DeleteAll() >= 1);
        Assert.Empty(TestKeychain.FindItems());
    }

    private static void WriteRawItem(string service, string account, byte[] password)
    {
        Constants c = Const;
        nint serviceRef = CreateString(service);
        nint accountRef = CreateString(account);
        nint data = CreateWipeableData(password);
        nint attributes = 0;
        try
        {
            attributes = CreateDictionary(
                [c.Class, c.AttrService, c.AttrAccount, c.ValueData],
                [c.ClassGenericPassword, serviceRef, accountRef, data]);
            unsafe
            {
                Assert.Equal(Success, SecItemAdd(attributes, null));
            }
        }
        finally
        {
            Release(attributes);
            ZeroAndRelease(data);
            Release(accountRef);
            Release(serviceRef);
        }
    }
}
