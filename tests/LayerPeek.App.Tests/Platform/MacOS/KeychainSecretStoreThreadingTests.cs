using System.Runtime.Versioning;

using LayerPeek.App.Platform.MacOS;

namespace LayerPeek.App.Tests.Platform.MacOS;

/// <summary>
/// The Keychain calls of <see cref="KeychainSecretStore"/> never run on the caller's thread (decided 2026-10-29): a call
/// that waits for a Keychain dialog must not block the UI thread. Uses <see cref="BlockingKeychainItems"/>, not the
/// real Keychain. The UI-thread variant is <c>KeychainSecretStoreUiThreadTests</c> in LayerPeek.App.UiTests.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class KeychainSecretStoreThreadingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public KeychainSecretStoreThreadingTests()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "The Keychain store exists only on macOS.");
    }

    [Fact]
    public async Task EveryOperationReturnsBeforeTheBlockedKeychainCallFinishes()
    {
        using BlockingKeychainItems items = new();
        KeychainSecretStore store = new("LayerPeek.Tests/threading", items);

        // Each call returns at once with a pending task while its Keychain call is blocked (a dialog is open).
        Task set = BlockingKeychainItems.FromCaller(() => store.SetAsync("printer/MK4", "secret", CancellationToken.None));
        Assert.True(await items.WaitUntilBlockedAsync(Timeout));
        Task<string?> get = BlockingKeychainItems.FromCaller(() => store.GetAsync("PRINTER/mk4", CancellationToken.None));
        Assert.True(await items.WaitUntilBlockedAsync(Timeout));
        Assert.False(set.IsCompleted);
        Assert.False(get.IsCompleted);

        items.Release();
        await set.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        // Get may have run before or after Set; ask again for the stored value.
        await get.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("secret", await store.GetAsync("printer/mk4", CancellationToken.None).WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.True(await store.DeleteAsync("Printer/Mk4", CancellationToken.None).WaitAsync(Timeout, TestContext.Current.CancellationToken));

        Assert.Equal(4, items.CallThreads.Count);
        Assert.Equal(0, items.InlineCalls);
    }

    [Fact]
    public async Task DeleteDoesNotRunInsideTheCallersCall()
    {
        using BlockingKeychainItems items = new();
        KeychainSecretStore store = new("LayerPeek.Tests/threading", items);
        Task<bool> delete = BlockingKeychainItems.FromCaller(() => store.DeleteAsync("printer/a", CancellationToken.None));
        Assert.True(await items.WaitUntilBlockedAsync(Timeout));
        Assert.False(delete.IsCompleted);

        items.Release();
        Assert.False(await delete.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Equal(0, items.InlineCalls);
    }

    [Fact]
    public void ArgumentErrorsAndCancellationStayOnTheCallersThread()
    {
        using BlockingKeychainItems items = new();
        KeychainSecretStore store = new("LayerPeek.Tests/threading", items);

        // Thrown synchronously, without a Keychain call.
        Assert.Throws<ArgumentException>(() => { _ = store.GetAsync(" ", CancellationToken.None); });
        Assert.Throws<ArgumentException>(() => { _ = store.SetAsync("a", new string('x', KeychainSecretStore.MaxSecretBytes + 1), CancellationToken.None); });
        Assert.True(store.DeleteAsync("a", new CancellationToken(canceled: true)).IsCanceled);
        Assert.Empty(items.CallThreads);
    }
}
